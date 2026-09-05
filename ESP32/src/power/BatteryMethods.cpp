#include "power/BatteryMethods.h"

#include "esp_log.h"

static const char* TAG = "BatteryMethods";

// One point on the Li-ion discharge curve: pack voltage (mV) -> SoC (%).
// Deliberately kept distinct from the finer table in BatteryMonitor: this is the
// curve the published percent was calibrated against, and swapping it for
// another would silently move every reported figure. Ascending by voltage.
struct CurvePoint {
  uint16_t mv;
  int8_t   pct;
};

static constexpr CurvePoint kCurve[] = {
    {3000, 0},  {3500, 5},  {3600, 10}, {3650, 20},
    {3700, 30}, {3750, 40}, {3800, 50}, {3850, 60},
    {3900, 70}, {3950, 80}, {4000, 90}, {4150, 100},
};
static constexpr std::size_t kCurveCount = sizeof(kCurve) / sizeof(kCurve[0]);

// Smallest median absolute deviation the outlier trim will work from, in raw
// counts. A window taken while nothing else on the board moved can come back
// with every sample on the same count, i.e. MAD = 0 - and a zero-width window
// would then reject every sample that is not EXACTLY the median, which is the
// opposite of what a quiet window deserves. Two counts is a shade under 2 mV at
// the pack after the divider: below the ADC's own noise, so it never widens a
// window that the data itself has already opened.
static constexpr uint32_t kMinMadCounts = 2;

BatteryMethods::BatteryMethods(AdcSampler& adc, BatteryWindowSampler& window,
                               float vbatDivider, uint32_t madFactor,
                               uint32_t noReadingMv)
    : adc_(adc),
      window_(window),
      vbatDivider_(vbatDivider),
      madFactor_(madFactor),
      noReadingMv_(noReadingMv),
      values_{},
      work_{} {}

bool BatteryMethods::sample(BatteryMethodsSample& out) {
  out = BatteryMethodsSample();

  // ---------------------------------------------------------------------------
  // 1. Drain the window.
  //
  //    Raw counts, not millivolts, and all of them before any conversion to
  //    voltage: that is what lets the trim below run on the counts themselves,
  //    so the median is taken over the samples that survived it rather than over
  //    the sag the trim exists to delete. Exactly one rawToMv() call follows, on
  //    the one count that came out of it.
  //
  //    Taking the window RESETS it, so the next one starts here - which is why
  //    this call belongs before the publish rather than after it.
  // ---------------------------------------------------------------------------
  std::size_t taken = 0;
  if (!window_.takeWindow(values_, kMaxSamples, taken) || taken == 0) {
    ESP_LOGW(TAG, "no ADC conversions succeeded - battery reading absent");
    return false;
  }

  // ---------------------------------------------------------------------------
  // 2. Delete the droop, then take the median. This is the one place where the
  //    window stops describing what the RAIL did while we were awake and starts
  //    describing what the PACK sits at.
  // ---------------------------------------------------------------------------
  taken = rejectOutliers(values_, taken, work_);

  const uint32_t rawMedian = medianOf(values_, taken);  // sorts values_ in place

  int converted = 0;
  if (!adc_.hasCalibration() ||
      !adc_.rawToMv(static_cast<int>(rawMedian), converted)) {
    // Without the eFuse calibration the count cannot be turned into a voltage
    // this may be trusted to publish, so nothing is reported rather than a
    // nominal-full-scale guess.
    ESP_LOGW(TAG, "ADC calibration unavailable - battery reading absent");
    return false;
  }

  const uint32_t medianMv =
      static_cast<uint32_t>(converted * vbatDivider_ + 0.5f);

  // "Is there a battery in front of this pin at all?" On USB power the sense pin
  // is cut off from the cell and reads ~0 (LilyGO issue #128), which must be
  // reported as ABSENT rather than as a pack about to die.
  if (medianMv < noReadingMv_) {
    ESP_LOGI(TAG, "pack sense reads %u mV - no battery in front of the pin",
             (unsigned)medianMv);
    return false;
  }

  out.millivolts = static_cast<uint16_t>(medianMv);
  out.percent    = percentCurve(medianMv);
  out.valid      = true;
  return true;
}

uint32_t BatteryMethods::medianOf(uint32_t* values, std::size_t n) {
  for (std::size_t i = 1; i < n; ++i) {
    const uint32_t key = values[i];
    std::size_t    j   = i;
    while (j > 0 && values[j - 1] > key) {
      values[j] = values[j - 1];
      --j;
    }
    values[j] = key;
  }
  return (n % 2) ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
}

std::size_t BatteryMethods::rejectOutliers(uint32_t* values, std::size_t n,
                                           uint32_t* scratch) {
  // Nothing to work with: one or two samples have no majority to be the odd one
  // out of, and a disabled factor means the caller wants the raw window.
  if (madFactor_ == 0 || n < 3) {
    return n;
  }

  // Median absolute deviation, in two passes of the median we already have. The
  // deviations go in `scratch` because medianOf() sorts what it is handed, and
  // sorting `values` a second time by deviation would scramble the counts.
  const uint32_t median = medianOf(values, n);
  for (std::size_t i = 0; i < n; ++i) {
    scratch[i] = (values[i] > median) ? (values[i] - median)
                                      : (median - values[i]);
  }
  uint32_t mad = medianOf(scratch, n);
  if (mad < kMinMadCounts) {
    mad = kMinMadCounts;  // see kMinMadCounts: a quiet burst is not an error
  }

  // Why the pack's own spread and not a fixed millivolt threshold: this way the
  // window needs no per-board tuning. A quiet rail keeps it tight, a noisy one
  // opens it by itself, and a transmit droop lands far outside either.
  const uint32_t limit = mad * madFactor_;

  // COUNT before compacting. Compaction overwrites the front of `values`, so
  // doing it first would leave nothing to fall back to if the count turns out
  // too low - the array would be neither the survivors nor the full burst.
  std::size_t kept = 0;
  for (std::size_t i = 0; i < n; ++i) {
    const uint32_t deviation =
        (values[i] > median) ? (values[i] - median) : (median - values[i]);
    if (deviation <= limit) {
      ++kept;
    }
  }

  // Fewer than half survived: the pack is genuinely collapsing, or the sag
  // lasted the whole window. Either way there is no quiet majority to fall back
  // on, and a "clean" reading assembled from a handful of samples would be a
  // guess dressed up as a measurement. Hand back the honest low window instead,
  // and say so - a device logging this every cycle is telling you the pack is
  // sagging for most of the time it is awake, not that a droop slipped through.
  //
  // `values` is left sorted rather than in sample order, which no caller minds:
  // the median re-sorts it.
  if (kept * 2 < n) {
    ESP_LOGW(TAG,
             "outlier trim kept only %u of %u samples - using the full window",
             (unsigned)kept, (unsigned)n);
    return n;
  }

  // Now compact, walking a sorted array: the survivors are a contiguous run, so
  // this only ever shifts them down toward the front.
  std::size_t write = 0;
  for (std::size_t i = 0; i < n; ++i) {
    const uint32_t deviation =
        (values[i] > median) ? (values[i] - median) : (median - values[i]);
    if (deviation <= limit) {
      values[write++] = values[i];
    }
  }

  return write;
}

int8_t BatteryMethods::percentCurve(uint32_t mv) {
  if (mv <= kCurve[0].mv) return 0;
  if (mv >= kCurve[kCurveCount - 1].mv) return 100;

  for (std::size_t i = 0; i + 1 < kCurveCount; ++i) {
    if (mv >= kCurve[i].mv && mv <= kCurve[i + 1].mv) {
      const uint32_t span    = kCurve[i + 1].mv - kCurve[i].mv;
      const uint32_t above   = mv - kCurve[i].mv;
      const uint32_t pctSpan = kCurve[i + 1].pct - kCurve[i].pct;
      return static_cast<int8_t>(kCurve[i].pct + (above * pctSpan) / span);
    }
  }
  return 0;
}
