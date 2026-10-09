#include "power/LowBatteryGuard.h"

#include "esp_attr.h"
#include "esp_log.h"

static const char* TAG = "LowBatteryGuard";

// -----------------------------------------------------------------------------
//  RTC-backed latch - see the header for why it cannot be a plain static. The
//  magic word is the same guard ChargerWatcher and BootJournal use: RTC slow
//  memory comes up as garbage after the rail drops, so only a matching word
//  means "this is ours and it is real".
// -----------------------------------------------------------------------------
static constexpr uint32_t kRtcMagic = 0x10BA77E2U;  // "LOBATT"

RTC_DATA_ATTR static uint32_t rtcMagic;
RTC_DATA_ATTR static uint8_t  rtcLatched;

LowBatteryGuard::LowBatteryGuard(BatteryMethods& methods, bool enabled,
                                 uint32_t cutoffMv, uint32_t resumeMv)
    : enabled_(enabled),
      cutoffMv_(cutoffMv),
      resumeMv_(resumeMv),
      pending_(false),
      methods_(methods) {}

LowBatteryGuard::BootVerdict LowBatteryGuard::checkAtBoot() {
  if (!enabled_) {
    return BootVerdict::Run;
  }

  const bool latched = (rtcMagic == kRtcMagic) && (rtcLatched != 0);

  BatteryMethodsSample spot;
  if (!methods_.spotSample(spot)) {
    // No pack in front of the pin - USB is connected, i.e. charging - or the
    // ADC could not be read at all. Either way: run. Charging is exactly what a
    // latched device is waiting for, and a broken ADC must not brick tracking.
    if (latched) {
      ESP_LOGI(TAG, "no pack reading (charger connected?) - resuming");
      clearLatch();
    }
    return BootVerdict::Run;
  }

  if (latched) {
    if (spot.millivolts >= resumeMv_) {
      ESP_LOGI(TAG, "pack recovered to %u mV (resume at %u) - starting up",
               (unsigned)spot.millivolts, (unsigned)resumeMv_);
      clearLatch();
      return BootVerdict::Run;
    }
    ESP_LOGI(TAG, "pack at %u mV, still below %u mV - staying asleep",
             (unsigned)spot.millivolts, (unsigned)resumeMv_);
    return BootVerdict::Hold;
  }

  if (spot.millivolts < cutoffMv_) {
    // Not latched yet, so the dashboard has not been told. Start up far enough
    // to say so; the main loop shuts down at its first safe point.
    ESP_LOGW(TAG, "pack at %u mV at boot - below the %u mV cut-off",
             (unsigned)spot.millivolts, (unsigned)cutoffMv_);
    pending_ = true;
    return BootVerdict::CutOff;
  }
  return BootVerdict::Run;
}

void LowBatteryGuard::evaluateCycle(const BatteryMethodsSample& measured,
                                    const BatteryStatus& fw) {
  if (!enabled_ || pending_) {
    return;
  }
  if (fw.valid && fw.charging) {
    return;  // on the charger - see the header: charging always wins
  }

  // The window's median when it has one; otherwise the modem's AT+CBC voltage,
  // which BatteryMonitor leaves at 0 on the charging path. No voltage at all
  // means no verdict - fail open.
  uint32_t mv = 0;
  if (measured.valid) {
    mv = measured.millivolts;
  } else if (fw.valid) {
    mv = fw.millivolts;
  }
  if (mv == 0) {
    return;
  }

  if (mv < cutoffMv_) {
    ESP_LOGW(TAG, "pack at %u mV - below the %u mV cut-off, shutting down",
             (unsigned)mv, (unsigned)cutoffMv_);
    pending_ = true;
  }
}

void LowBatteryGuard::latch() {
  rtcMagic   = kRtcMagic;
  rtcLatched = 1;
}

void LowBatteryGuard::clearLatch() {
  rtcMagic   = kRtcMagic;
  rtcLatched = 0;
}
