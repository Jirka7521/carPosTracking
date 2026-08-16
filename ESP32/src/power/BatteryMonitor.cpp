#include "power/BatteryMonitor.h"

#include <cstdlib>
#include <cstring>

#include "esp_log.h"

static const char* TAG = "BatteryMonitor";

// One point on the single-cell Li-ion discharge curve: resting open-circuit
// voltage (mV) -> state of charge (%).
struct OcvPoint {
  uint16_t mv;
  uint8_t  pct;
};

// Resting-voltage -> SoC curve for one Li-ion cell. Li-ion is markedly
// non-linear: it sits around 3.7-3.9 V for most of the discharge and only falls
// off a cliff near empty, so the old straight-line map badly misread the middle
// (and any resting pack looked ~90 %). We piecewise-linearly interpolate between
// these points instead. Ascending by voltage so the search is a simple walk.
//
// NOTE: "open-circuit" means *at rest*. Under the SIM7000's ~2 A transmit bursts
// VBAT sags, so a reading taken mid-TX interpolates pessimistically low - the raw
// millivolts in the serial debug print make that visible for calibration.
static constexpr OcvPoint kOcvCurve[] = {
    {3300, 0},  {3400, 3},  {3550, 7},  {3650, 12}, {3700, 18},
    {3740, 25}, {3760, 32}, {3780, 40}, {3800, 48}, {3850, 55},
    {3900, 62}, {3950, 70}, {4000, 80}, {4100, 90}, {4200, 100},
};
static constexpr std::size_t kOcvCurveCount =
    sizeof(kOcvCurve) / sizeof(kOcvCurve[0]);

BatteryMonitor::BatteryMonitor(Sim7000Modem& modem, int chargeSensePin,
                               int chargeAdcThreshold, uint32_t emptyMv,
                               uint32_t fullMv)
    : modem_(modem),
      chargeSensePin_(chargeSensePin),
      chargeAdcThreshold_(chargeAdcThreshold),
      emptyMv_(emptyMv),
      fullMv_(fullMv) {}

bool BatteryMonitor::begin() {
  // Map the charge-sense GPIO to its ADC unit/channel (GPIO35 -> ADC1_CH7 on the
  // ESP32-WROVER). Doing it via the helper keeps the pin the single source of
  // truth, matching the rest of the config.
  adc_unit_t    unit    = ADC_UNIT_1;
  adc_channel_t channel = ADC_CHANNEL_0;
  esp_err_t     err = adc_oneshot_io_to_channel(chargeSensePin_, &unit, &channel);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "GPIO%d is not an ADC pin: %s", chargeSensePin_,
             esp_err_to_name(err));
    return false;
  }

  adc_oneshot_unit_init_cfg_t initCfg = {};
  initCfg.unit_id                     = unit;
  err = adc_oneshot_new_unit(&initCfg, &adcHandle_);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "ADC unit init failed: %s", esp_err_to_name(err));
    return false;
  }

  // Full-scale attenuation: we only need a coarse "is it near 0?" decision, and
  // when discharging the sense pin can swing across the full range.
  adc_oneshot_chan_cfg_t chanCfg = {};
  chanCfg.atten                  = ADC_ATTEN_DB_12;
  chanCfg.bitwidth               = ADC_BITWIDTH_DEFAULT;
  err = adc_oneshot_config_channel(adcHandle_, channel, &chanCfg);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "ADC channel config failed: %s", esp_err_to_name(err));
    return false;
  }

  adcChannel_ = channel;
  ready_      = true;
  ESP_LOGI(TAG, "BatteryMonitor ready (charge-sense GPIO%d)", chargeSensePin_);
  return true;
}

bool BatteryMonitor::read(BatteryStatus& out) {
  out = BatteryStatus();  // reset to invalid; only a full success marks it valid
  if (!ready_) {
    return false;
  }

  // 1. Charging? The charger pulls the sense pin to ~0, so a low ADC reading
  //    means "on charge" and we report the sentinel percent = 0.
  int rawAdc = 0;
  if (adc_oneshot_read(adcHandle_, adcChannel_, &rawAdc) != ESP_OK) {
    ESP_LOGW(TAG, "charge-sense ADC read failed");
    return false;
  }
  if (rawAdc < chargeAdcThreshold_) {
    out.charging = true;
    out.percent  = 0;
    out.valid    = true;
    return true;
  }

  // 2. Discharging: ask the modem for the pack voltage and map it to a percent.
  char resp[128] = {0};
  if (!modem_.sendCommand("AT+CBC", resp, sizeof(resp), 1000, "OK")) {
    ESP_LOGW(TAG, "AT+CBC did not answer");
    return false;
  }

  uint32_t mv = 0;
  if (!parseCbcMillivolts(resp, mv)) {
    ESP_LOGW(TAG, "could not parse AT+CBC reply");
    return false;
  }

  out.percent    = voltageToPercent(mv);
  out.millivolts = static_cast<uint16_t>(mv);  // raw, for the debug print only
  out.charging   = false;
  out.valid      = true;
  return true;
}

bool BatteryMonitor::parseCbcMillivolts(const char* response, uint32_t& mvOut) {
  const char* tag = std::strstr(response, "+CBC:");
  if (tag == nullptr) {
    return false;
  }
  tag += 5;  // skip past "+CBC:"

  // Copy just this line (up to CR/LF) into a small scratch buffer.
  char line[48];
  std::size_t n = 0;
  while (*tag != '\0' && *tag != '\r' && *tag != '\n' && n + 1 < sizeof(line)) {
    line[n++] = *tag++;
  }
  line[n] = '\0';

  // The voltage is the last comma-separated field: "<mV>" on firmware that
  // reports "<bcs>,<bcl>,<mV>", or the whole thing on "<volts>"-only firmware.
  const char* lastComma = std::strrchr(line, ',');
  const char* numStr    = (lastComma != nullptr) ? lastComma + 1 : line;

  char*        endPtr = nullptr;
  const double value  = std::strtod(numStr, &endPtr);
  if (endPtr == numStr) {
    return false;  // no number present
  }

  // A value below 100 is volts (e.g. 3.987); otherwise it is already millivolts.
  mvOut = (value < 100.0) ? static_cast<uint32_t>(value * 1000.0 + 0.5)
                          : static_cast<uint32_t>(value + 0.5);
  return true;
}

uint8_t BatteryMonitor::voltageToPercent(uint32_t mv) const {
  // The configurable window is the outer clamp; the Li-ion curve shapes the
  // interior. 0 stays the charging sentinel, so a genuinely low-but-discharging
  // pack floors at 1 rather than ever reading 0.
  if (fullMv_ <= emptyMv_) {
    return 1;  // misconfigured window; avoid nonsense, stay non-sentinel
  }
  if (mv <= emptyMv_) {
    return 1;  // empty but discharging -> 1, never the charging sentinel 0
  }
  if (mv >= fullMv_) {
    return 100;
  }

  // Interior: piecewise-linear interpolation over kOcvCurve. Walk to the first
  // point at or above mv, then interpolate between it and the point below.
  uint8_t pct = 100;  // stays 100 only if mv sits above the whole table
  for (std::size_t i = 0; i < kOcvCurveCount; ++i) {
    if (mv < kOcvCurve[i].mv) {
      if (i == 0) {
        pct = kOcvCurve[0].pct;  // below the table's lowest point -> ~empty
      } else {
        const OcvPoint& lo = kOcvCurve[i - 1];
        const OcvPoint& hi = kOcvCurve[i];
        // Straight line between lo and hi. All values are small, so this uint32
        // arithmetic cannot overflow.
        const uint32_t span    = hi.mv - lo.mv;
        const uint32_t above   = mv - lo.mv;
        const uint32_t pctSpan = hi.pct - lo.pct;
        pct = static_cast<uint8_t>(lo.pct + (above * pctSpan) / span);
      }
      break;
    }
  }

  return pct < 1 ? 1 : pct;  // never the charging sentinel
}
