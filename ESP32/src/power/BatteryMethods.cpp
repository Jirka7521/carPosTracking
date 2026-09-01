#include "power/BatteryMethods.h"

#include <cmath>
#include <cstdlib>
#include <cstring>

#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "BatteryMethods";

// One point on the rig's Li-ion discharge curve: pack voltage (mV) -> SoC (%).
// Deliberately the SAME table the Arduino comparison rig used, not the finer one
// in BatteryMonitor: this class exists to compare models, so its "curve" column
// has to stay the model it is being compared against. Ascending by voltage.
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

// Full-scale count of the 12-bit ADC, and the nominal full-scale voltage at
// 12 dB attenuation. Only the *uncalibrated* source uses these - that is the
// whole point of it: it is what you get with no eFuse data and no correction.
static constexpr uint32_t kAdcFullScaleCounts = 4095;
static constexpr uint32_t kAdcFullScaleMv     = 3300;

// How far the pack voltage must rise across the trend window to call it
// charging. 20 mV is comfortably above the sample-to-sample noise of a
// calibrated read and well below a real charge slope.
static constexpr uint32_t kTrendRiseMv = 20;

// Smallest median absolute deviation the outlier trim will work from, in raw
// counts. A burst taken while nothing else on the board moved can come back with
// every sample on the same count, i.e. MAD = 0 - and a zero-width window would
// then reject every sample that is not EXACTLY the median, which is the opposite
// of what a quiet burst deserves. Two counts is a shade under 2 mV at the pack
// after the divider: below the ADC's own noise, so it never widens a window that
// the data itself has already opened.
static constexpr uint32_t kMinMadCounts = 2;

BatteryMethods::BatteryMethods(AdcSampler& adc, Sim7000Modem& modem,
                               int vbatPin, int solarPin, float vbatDivider,
                               float solarDivider, std::size_t samples,
                               uint32_t sampleGapMs, uint32_t madFactor,
                               uint32_t emptyMv, uint32_t fullMv,
                               uint32_t inputThresholdMv, uint32_t noReadingMv)
    : adc_(adc),
      modem_(modem),
      vbatPin_(vbatPin),
      solarPin_(solarPin),
      vbatDivider_(vbatDivider),
      solarDivider_(solarDivider),
      samples_(samples == 0 ? 1
                            : (samples > kMaxSamples ? kMaxSamples : samples)),
      sampleGapMs_(sampleGapMs),
      madFactor_(madFactor),
      emptyMv_(emptyMv),
      fullMv_(fullMv),
      inputThresholdMv_(inputThresholdMv),
      noReadingMv_(noReadingMv) {}

bool BatteryMethods::begin() {
  if (!adc_.addPin(vbatPin_)) {
    ESP_LOGW(TAG, "pack sense GPIO%d unavailable - no ADC sources", vbatPin_);
    return false;
  }

  // The charge-input pin is a bonus channel: without it we lose two columns and
  // one of the three charging detectors, which is not worth failing over.
  solarReady_ = adc_.addPin(solarPin_);
  if (!solarReady_) {
    ESP_LOGW(TAG, "charge-input GPIO%d unavailable - solar columns omitted",
             solarPin_);
  }

  ESP_LOGI(TAG,
           "battery methods ready (pack GPIO%d, input GPIO%d, %u samples, "
           "calibration %s)",
           vbatPin_, solarPin_, (unsigned)samples_,
           adc_.hasCalibration() ? "on" : "off");
  return true;
}

bool BatteryMethods::sample(BatteryMethodsSample& out) {
  out = BatteryMethodsSample();
  // Absent is -1 everywhere, so an unreadable source can never be mistaken for
  // a flat pack. The struct's own default of 0 would say exactly that.
  for (std::size_t s = 0; s < kBatterySourceCount; ++s) {
    for (std::size_t m = 0; m < kBatteryModelCount; ++m) {
      out.percent[s][m] = -1;
    }
  }

  // ---------------------------------------------------------------------------
  // 1. One burst of raw counts, converted four ways (see the header for why one
  //    burst and not two).
  //
  //    The conversions are SPACED, and every count is collected before any of
  //    them is turned into millivolts. Both are deliberate:
  //
  //      * the spacing (sampleGapMs_) is what lets a transmit droop be a
  //        MINORITY of the burst rather than all of it - see the header banner;
  //      * collecting first is what lets the trim below run on raw counts, so
  //        every source derives from the SAME surviving samples. Calibrating
  //        inside the collection loop, as this used to, would leave
  //        kSourceCalPerSample averaging samples the other sources had already
  //        thrown away, and its disagreement with kSourceCalMean would then
  //        measure the trim instead of the maths it exists to measure.
  // ---------------------------------------------------------------------------
  uint32_t    raw[kMaxSamples]  = {};
  uint32_t    work[kMaxSamples] = {};  // scratch the trim borrows for deviations
  std::size_t taken             = 0;

  for (std::size_t i = 0; i < samples_; ++i) {
    // Between conversions only - not before the first (the caller has already
    // paused for the rail to settle) and not after the last (a trailing delay
    // lengthens the sweep without widening the window it covers).
    if (i > 0 && sampleGapMs_ > 0) {
      vTaskDelay(pdMS_TO_TICKS(sampleGapMs_));
    }

    int value = 0;
    if (!adc_.readRaw(vbatPin_, value)) {
      continue;  // a failed conversion is dropped, not counted as a zero
    }
    raw[taken++] = static_cast<uint32_t>(value);
  }

  if (taken > 0) {
    // Delete the droop. This is the one line where the burst stops describing
    // what the RAIL did during the window and starts describing what the PACK
    // sits at; everything below works from the survivors.
    taken = rejectOutliers(raw, taken, work);

    uint32_t rawSum = 0;
    for (std::size_t i = 0; i < taken; ++i) {
      rawSum += raw[i];
    }
    const uint32_t rawMean = rawSum / taken;
    out.rawMean            = static_cast<uint16_t>(rawMean);

    // Source 1's input, taken here so it too sees only the survivors. One
    // failed conversion disqualifies the whole source rather than silently
    // averaging a short set - it is meant to be comparable to the mean beside
    // it, and a different sample count would break that.
    uint32_t calSum         = 0;
    bool     calPerSampleOk = adc_.hasCalibration();
    for (std::size_t i = 0; i < taken && calPerSampleOk; ++i) {
      int mv = 0;
      if (adc_.rawToMv(static_cast<int>(raw[i]), mv)) {
        calSum += static_cast<uint32_t>(mv);
      } else {
        calPerSampleOk = false;
      }
    }

    const uint32_t rawMedian = medianOf(raw, taken);  // sorts raw[] in place
    out.rawMedian            = static_cast<uint16_t>(rawMedian);

    // Source 0: no calibration at all - nominal full scale, nominal Vref.
    const uint32_t naiveMv = static_cast<uint32_t>(
        (static_cast<float>(rawMean) * kAdcFullScaleMv / kAdcFullScaleCounts) *
            vbatDivider_ +
        0.5f);

    // Sources 1-3: the same counts through the line-fitting calibration, taken
    // per sample / on the mean / on the median.
    uint32_t   perSampleMv = 0;
    uint32_t   meanMv      = 0;
    uint32_t   medianMv    = 0;
    int        converted   = 0;
    const bool haveCal     = adc_.hasCalibration();

    if (calPerSampleOk) {
      perSampleMv = static_cast<uint32_t>(
          (static_cast<float>(calSum) / taken) * vbatDivider_ + 0.5f);
    }
    if (haveCal && adc_.rawToMv(static_cast<int>(rawMean), converted)) {
      meanMv = static_cast<uint32_t>(converted * vbatDivider_ + 0.5f);
    }
    if (haveCal && adc_.rawToMv(static_cast<int>(rawMedian), converted)) {
      medianMv = static_cast<uint32_t>(converted * vbatDivider_ + 0.5f);
    }

    // "Is there a battery in front of this pin at all?" is decided once, on the
    // best number available, and then applied to every ADC source - so a capture
    // never shows three sources live and one dead for no visible reason. On USB
    // power they all fail this together, which is the expected result.
    const uint32_t reference = (meanMv > 0) ? meanMv : naiveMv;

    if (reference >= noReadingMv_) {
      out.mv[kSourceNaive]      = static_cast<uint16_t>(naiveMv);
      out.mvValid[kSourceNaive] = true;
      if (calPerSampleOk) {
        out.mv[kSourceCalPerSample]      = static_cast<uint16_t>(perSampleMv);
        out.mvValid[kSourceCalPerSample] = true;
      }
      if (meanMv > 0) {
        out.mv[kSourceCalMean]      = static_cast<uint16_t>(meanMv);
        out.mvValid[kSourceCalMean] = true;
      }
      if (medianMv > 0) {
        out.mv[kSourceCalMedian]      = static_cast<uint16_t>(medianMv);
        out.mvValid[kSourceCalMedian] = true;
      }
    }
  }

  // ---------------------------------------------------------------------------
  // 2. Source 4: the modem's own measurement, plus the two figures it reports
  //    that no ADC can give us - its charge status and its own percentage.
  // ---------------------------------------------------------------------------
  char resp[128] = {0};
  if (modem_.sendCommand("AT+CBC", resp, sizeof(resp), 1000, "OK")) {
    uint32_t modemMv = 0;
    int8_t   status  = -1;
    int8_t   percent = -1;
    // The 1000 mV floor is the rig's sanity check: the modem answers with a
    // zero-ish voltage while it is still waking, and that must not be logged as
    // a dead pack.
    if (parseCbc(resp, modemMv, status, percent) && modemMv > 1000) {
      out.mv[kSourceModem]      = static_cast<uint16_t>(modemMv);
      out.mvValid[kSourceModem] = true;
      out.modemStatus           = status;
      out.modemPercent          = percent;
    }
  }

  // ---------------------------------------------------------------------------
  // 3. Charge input (solar / VIN) on its own pin. Half the pack burst is plenty:
  //    this is a present/absent question, not a calibration.
  // ---------------------------------------------------------------------------
  if (solarReady_) {
    uint32_t    sum   = 0;
    std::size_t count = 0;
    for (std::size_t i = 0; i < samples_ / 2 + 1; ++i) {
      int value = 0;
      if (adc_.readRaw(solarPin_, value)) {
        sum += static_cast<uint32_t>(value);
        ++count;
      }
    }
    if (count > 0) {
      const uint32_t solarRaw = sum / count;
      out.solarRaw            = static_cast<uint16_t>(solarRaw);

      int converted = 0;
      if (adc_.hasCalibration() &&
          adc_.rawToMv(static_cast<int>(solarRaw), converted)) {
        out.solarMv = static_cast<uint16_t>(converted * solarDivider_ + 0.5f);
      } else {
        out.solarMv = static_cast<uint16_t>(
            (static_cast<float>(solarRaw) * kAdcFullScaleMv /
             kAdcFullScaleCounts) *
                solarDivider_ +
            0.5f);
      }
      out.inputPresent = out.solarMv >= inputThresholdMv_;
    }
  }

  // ---------------------------------------------------------------------------
  // 4. Score every source that answered, with every model.
  // ---------------------------------------------------------------------------
  for (std::size_t s = 0; s < kBatterySourceCount; ++s) {
    if (!out.mvValid[s]) {
      continue;
    }
    out.valid                     = true;
    out.percent[s][kModelLinear]  = percentLinear(out.mv[s]);
    out.percent[s][kModelCurve]   = percentCurve(out.mv[s]);
    out.percent[s][kModelSigmoid] = percentSigmoid(out.mv[s]);
  }

  // ---------------------------------------------------------------------------
  // 5. Trend detection, on the best voltage available: the calibrated mean when
  //    the ADC path is alive, otherwise whatever the modem said.
  // ---------------------------------------------------------------------------
  uint32_t trendMv = 0;
  if (out.mvValid[kSourceCalMean]) {
    trendMv = out.mv[kSourceCalMean];
  } else if (out.mvValid[kSourceNaive]) {
    trendMv = out.mv[kSourceNaive];
  } else if (out.mvValid[kSourceModem]) {
    trendMv = out.mv[kSourceModem];
  }
  if (trendMv > 0) {
    out.trendCharging = noteTrend(trendMv, out.trendUsable);
  }

  // ---------------------------------------------------------------------------
  // 6. The spread - how far apart the methods landed. This is the number the
  //    whole exercise exists to produce.
  // ---------------------------------------------------------------------------
  uint16_t minMv = 0, maxMv = 0;
  int8_t   minPct = 0, maxPct = 0;
  bool     haveMv = false, havePct = false;

  for (std::size_t s = 0; s < kBatterySourceCount; ++s) {
    if (!out.mvValid[s]) {
      continue;
    }
    if (!haveMv || out.mv[s] < minMv) minMv = out.mv[s];
    if (!haveMv || out.mv[s] > maxMv) maxMv = out.mv[s];
    haveMv = true;

    for (std::size_t m = 0; m < kBatteryModelCount; ++m) {
      const int8_t pct = out.percent[s][m];
      if (pct < 0) continue;
      if (!havePct || pct < minPct) minPct = pct;
      if (!havePct || pct > maxPct) maxPct = pct;
      havePct = true;
    }
  }
  // The modem's self-reported percentage is part of the disagreement too, even
  // though it has no voltage of its own to sit under.
  if (out.modemPercent >= 0) {
    if (!havePct || out.modemPercent < minPct) minPct = out.modemPercent;
    if (!havePct || out.modemPercent > maxPct) maxPct = out.modemPercent;
    havePct = true;
  }

  out.spreadMv  = haveMv ? static_cast<uint16_t>(maxMv - minMv) : 0;
  out.spreadPct = havePct ? static_cast<int8_t>(maxPct - minPct) : -1;

  return out.valid;
}

bool BatteryMethods::parseCbc(const char* response, uint32_t& mvOut,
                              int8_t& statusOut, int8_t& percentOut) {
  statusOut  = -1;
  percentOut = -1;

  const char* tag = std::strstr(response, "+CBC:");
  if (tag == nullptr) {
    return false;
  }
  tag += 5;  // skip past "+CBC:"

  // Copy just this line (up to CR/LF) into a small scratch buffer.
  char        line[48];
  std::size_t n = 0;
  while (*tag != '\0' && *tag != '\r' && *tag != '\n' && n + 1 < sizeof(line)) {
    line[n++] = *tag++;
  }
  line[n] = '\0';

  // "<bcs>,<bcl>,<mV>" on most firmware; a bare "<volts>" on some. The two
  // leading fields only exist in the first shape, hence the comma count.
  const char* firstComma = std::strchr(line, ',');
  const char* lastComma  = std::strrchr(line, ',');
  if (firstComma != nullptr && lastComma != firstComma) {
    statusOut  = static_cast<int8_t>(std::strtol(line, nullptr, 10));
    percentOut = static_cast<int8_t>(std::strtol(firstComma + 1, nullptr, 10));
  }

  const char* numStr = (lastComma != nullptr) ? lastComma + 1 : line;
  char*       endPtr = nullptr;
  const double value = std::strtod(numStr, &endPtr);
  if (endPtr == numStr) {
    return false;  // no number present
  }

  // A value below 100 is volts (e.g. 3.987); otherwise it is already millivolts.
  mvOut = (value < 100.0) ? static_cast<uint32_t>(value * 1000.0 + 0.5)
                          : static_cast<uint32_t>(value + 0.5);
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
  // out of, and a disabled factor means the caller wants the raw burst.
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
  // guess dressed up as a measurement. Hand back the honest low burst instead,
  // and say so - a device logging this every cycle is telling you its window is
  // too short for its radio, not that its pack is bad.
  //
  // `values` is left sorted rather than in sample order, which no caller minds:
  // the mean sums it and the median re-sorts it.
  if (kept * 2 < n) {
    ESP_LOGW(TAG,
             "outlier trim kept only %u of %u samples - using the full burst",
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

int8_t BatteryMethods::percentLinear(uint32_t mv) const {
  if (fullMv_ <= emptyMv_) {
    return 0;  // misconfigured window; a straight line has nowhere to go
  }
  if (mv <= emptyMv_) return 0;
  if (mv >= fullMv_) return 100;
  const uint32_t span = fullMv_ - emptyMv_;
  return static_cast<int8_t>(((mv - emptyMv_) * 100 + span / 2) / span);
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

int8_t BatteryMethods::percentSigmoid(uint32_t mv) {
  // The standard LiPo sigmoid approximation, carried over unchanged from the rig
  // so the two capture sets stay comparable.
  const float volts = mv / 1000.0f;
  float       p =
      123.0f - 123.0f / powf(1.0f + powf(1.38f * (volts / 4.2f), 80.0f), 0.165f);
  if (p < 0.0f) p = 0.0f;
  if (p > 100.0f) p = 100.0f;
  return static_cast<int8_t>(p + 0.5f);
}

bool BatteryMethods::noteTrend(uint32_t mv, bool& usableOut) {
  usableOut = (trendCount_ >= kTrendWindow);

  // trendIndex_ is the next slot to overwrite, which in a full ring is also the
  // OLDEST entry - so the comparison happens before the push, not after.
  const bool rising = usableOut && (mv > trend_[trendIndex_]) &&
                      ((mv - trend_[trendIndex_]) >= kTrendRiseMv);

  trend_[trendIndex_] = mv;
  trendIndex_         = (trendIndex_ + 1) % kTrendWindow;
  if (trendCount_ < kTrendWindow) {
    ++trendCount_;
  }
  return rising;
}
