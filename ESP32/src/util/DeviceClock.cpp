#include "util/DeviceClock.h"

#include <sys/time.h>

#include "esp_attr.h"
#include "esp_log.h"
#include "util/CivilTime.h"

static const char* TAG = "DeviceClock";

namespace {

  // RTC slow memory: survives deep sleep, is lost on a power-on or brownout
  // reset. The same pattern BootJournal and ChargerWatcher use, and for the same
  // reason - this is state that must outlive the reboot at the end of every
  // sleeping cycle but must NOT outlive a battery change.
  //
  // The magic word is what distinguishes "seeded, and here is when" from the
  // arbitrary bytes that greet a cold boot.
  constexpr uint32_t kRtcMagic = 0x434C4B31;  // "CLK1"

  RTC_DATA_ATTR uint32_t rtcMagic         = 0;
  RTC_DATA_ATTR int64_t  rtcLastSeedEpoch = 0;

}  // namespace

DeviceClock::DeviceClock(uint32_t trustWindowSeconds)
    : trustWindowSeconds_(trustWindowSeconds), seeded_(false) {}

void DeviceClock::begin() {
  seeded_ = (rtcMagic == kRtcMagic);

  if (!seeded_) {
    ESP_LOGI(TAG, "no clock carried over - waiting for a GNSS fix to seed it");
    return;
  }

  int64_t now = 0;
  if (nowUtc(now)) {
    // Cast to int rather than printing the int64 directly: nano printf
    // (CONFIG_NEWLIB_NANO_FORMAT) has reduced %ll support, and an age in seconds
    // has no need of 64 bits.
    ESP_LOGI(TAG, "clock resumed at %s (seeded %ds ago)",
             CivilTime::formatIso(now).c_str(),
             (int)secondsSinceSeed());
  }
}

bool DeviceClock::seedFromGnss(const GnssTime& time) {
  if (!time.valid) {
    return false;
  }

  const int64_t epoch =
      CivilTime::daysFromCivil(time.year, time.month, time.day) *
          CivilTime::kSecondsPerDay +
      static_cast<int64_t>(time.hour) * CivilTime::kSecondsPerHour +
      static_cast<int64_t>(time.minute) * CivilTime::kSecondsPerMinute +
      static_cast<int64_t>(time.second);

  // A modem that has powered up but not yet locked will cheerfully report a date
  // in 1980. Accepting it would not merely be inaccurate - it would make every
  // schedule window resolve against the wrong week, and isTrusted() would say
  // yes, because the seed would be brand new.
  if (epoch < kMinPlausibleEpoch) {
    ESP_LOGW(TAG, "ignoring an implausible GNSS time (%04u-%02u-%02u)",
             (unsigned)time.year, (unsigned)time.month, (unsigned)time.day);
    return false;
  }

  struct timeval tv = {};
  tv.tv_sec         = static_cast<time_t>(epoch);
  tv.tv_usec        = 0;
  if (settimeofday(&tv, nullptr) != 0) {
    ESP_LOGW(TAG, "settimeofday failed - the clock stays as it was");
    return false;
  }

  rtcLastSeedEpoch = epoch;
  rtcMagic         = kRtcMagic;

  if (!seeded_) {
    ESP_LOGI(TAG, "clock seeded from GNSS: %s",
             CivilTime::formatIso(epoch).c_str());
  }
  seeded_ = true;
  return true;
}

bool DeviceClock::nowUtc(int64_t& epochOut) const {
  if (!seeded_) {
    return false;
  }

  struct timeval tv = {};
  if (gettimeofday(&tv, nullptr) != 0) {
    return false;
  }

  epochOut = static_cast<int64_t>(tv.tv_sec);
  return true;
}

bool DeviceClock::isTrusted() const {
  if (!seeded_) {
    return false;
  }

  // A zero window means "never expire", matching how kRetryMaxAgeHours treats
  // zero elsewhere in this firmware.
  if (trustWindowSeconds_ == 0) {
    return true;
  }

  const int64_t age = secondsSinceSeed();

  // A negative age means the clock has gone backwards since the seed, which can
  // only happen if something else set it. Treat it as untrustworthy rather than
  // as very fresh.
  return age >= 0 && age <= static_cast<int64_t>(trustWindowSeconds_);
}

int64_t DeviceClock::secondsSinceSeed() const {
  int64_t now = 0;
  if (!nowUtc(now)) {
    return -1;
  }
  return now - rtcLastSeedEpoch;
}
