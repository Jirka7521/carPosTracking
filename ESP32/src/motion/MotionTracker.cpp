#include "motion/MotionTracker.h"

#include <limits>

#include "esp_attr.h"
#include "esp_log.h"
#include "esp_timer.h"

static const char* TAG = "Motion";

namespace {

// What survives a deep sleep - see "Surviving deep sleep" in the header. The
// magic word tells a real record from the garbage RTC memory holds after a
// power-on; the record is consumed by begin(), so it is only ever read once.
constexpr uint32_t kRtcMagic = 0x4D4F5431;  // "MOT1"

RTC_DATA_ATTR uint32_t rtcMagic           = 0;
RTC_DATA_ATTR uint8_t  rtcState           = 0;
RTC_DATA_ATTR int32_t  rtcStopRemainingMs = 0;

constexpr int64_t kUsPerMs = 1000LL;
constexpr int64_t kUsPerS  = 1000000LL;

}  // namespace

MotionTracker::MotionTracker()
    : enabled_(false),
      state_(State::Standby),
      deadlineUs_(0),
      checkStartedUs_(0),
      checkPublished_(false),
      checkAnchorPending_(false) {}

void MotionTracker::begin(const DeviceSettings& settings) {
  const esp_sleep_wakeup_cause_t cause = esp_sleep_get_wakeup_cause();

  // Take the record and clear it in one go: if this boot does not end in a
  // MOVING sleep, the next one must not find a stale "moving" left behind.
  const bool    wasMoving   = rtcMagic == kRtcMagic &&
                              rtcState == static_cast<uint8_t>(State::Moving);
  const int32_t remainingMs = rtcStopRemainingMs;
  rtcMagic           = kRtcMagic;
  rtcState           = static_cast<uint8_t>(State::Standby);
  rtcStopRemainingMs = 0;

  enabled_ = settings.motion().enabled();
  if (!enabled_) {
    state_ = State::Standby;
    ESP_LOGI(TAG, "motion wake off.");
    return;
  }

  if (wasMoving && cause == ESP_SLEEP_WAKEUP_TIMER) {
    // A timed sleep between moving reports. The remainder may already be spent
    // (negative): the window then closes after this cycle's fix, unless that fix
    // is itself fast - which is exactly the check a moving device should make.
    state_      = State::Moving;
    deadlineUs_ = esp_timer_get_time() +
                  (remainingMs > 0 ? remainingMs * kUsPerMs : 0);
    ESP_LOGI(TAG, "MOVING resumed after a timed sleep (%ds of the stop window "
                  "left).",
             (int)(remainingMs > 0 ? remainingMs / 1000 : 0));
    return;
  }

  startChecking(settings, wakeReason(cause));
}

void MotionTracker::update(const DeviceSettings& settings) {
  const bool wanted = settings.motion().enabled();
  if (wanted == enabled_) {
    return;
  }

  enabled_ = wanted;
  if (enabled_) {
    // Nothing says the car is parked, so find out rather than assume.
    startChecking(settings, "motion wake switched on");
  } else {
    state_              = State::Standby;
    checkAnchorPending_ = false;
    ESP_LOGI(TAG, "motion wake switched off - standby settings from now on.");
  }
}

const ModeSettings& MotionTracker::activeMode(
    const DeviceSettings& settings) const {
  if (enabled_ && state_ != State::Standby) {
    return settings.motion().moving();
  }
  return settings.standby();
}

bool MotionTracker::onFix(const DeviceSettings& settings, bool haveFix,
                          double speedKmph) {
  if (!enabled_) {
    return haveFix;  // the device as it always was: every fix goes out
  }
  if (!haveFix) {
    return false;  // no position, so no evidence either way and nothing to send
  }

  const MotionSettings& motion = settings.motion();
  if (speedKmph > static_cast<double>(motion.speedKmph())) {
    if (state_ != State::Moving) {
      ESP_LOGI(TAG, "%s -> MOVING (%.1f km/h).", stateName(state_), speedKmph);
    }
    state_              = State::Moving;
    deadlineUs_         = esp_timer_get_time() +
                          static_cast<int64_t>(motion.stopWaitSeconds()) * kUsPerS;
    checkAnchorPending_ = false;
    return true;
  }

  switch (state_) {
    case State::Checking:
      // One report per wake: the first fix says where the car is and carries the
      // battery reading. The rest of the check only watches the speed, so a
      // parked car is not published every few seconds for minutes on end.
      if (checkPublished_) {
        return false;
      }
      checkPublished_ = true;
      return true;
    case State::Moving:   // slow but still on a trip - a traffic light
    case State::Standby:  // a normal standby report
    default:
      return true;
  }
}

void MotionTracker::onActivity(const DeviceSettings& settings) {
  if (enabled_ && state_ == State::Standby) {
    startChecking(settings, "accelerometer activity");
  }
}

bool MotionTracker::evaluate() {
  if (!enabled_ || state_ == State::Standby) {
    return false;
  }
  if (esp_timer_get_time() < deadlineUs_) {
    return false;
  }

  if (state_ == State::Checking) {
    ESP_LOGI(TAG, "no fix faster than the speed limit within the wake window - "
                  "CHECKING -> STANDBY.");
    checkAnchorPending_ = true;
  } else {
    ESP_LOGI(TAG, "stationary for the whole stop window - MOVING -> STANDBY.");
  }
  state_ = State::Standby;
  return true;
}

int64_t MotionTracker::msUntilDeadline() const {
  if (!enabled_ || state_ == State::Standby) {
    return -1;
  }
  const int64_t remainingUs = deadlineUs_ - esp_timer_get_time();
  return remainingUs > 0 ? remainingUs / kUsPerMs : 0;
}

bool MotionTracker::takeCheckAnchor(int64_t& anchorUs) {
  if (!checkAnchorPending_) {
    return false;
  }
  checkAnchorPending_ = false;
  anchorUs            = checkStartedUs_;
  return true;
}

uint8_t MotionTracker::sleepWakeSteps(const DeviceSettings& settings) const {
  if (enabled_ && state_ == State::Standby) {
    return settings.motion().thresholdSteps();
  }
  return 0;
}

void MotionTracker::prepareForSleep(uint32_t sleepMs) {
  rtcMagic = kRtcMagic;
  if (enabled_ && state_ == State::Moving) {
    int64_t remainingMs = msUntilDeadline() - static_cast<int64_t>(sleepMs);
    if (remainingMs < std::numeric_limits<int32_t>::min()) {
      remainingMs = std::numeric_limits<int32_t>::min();
    }
    rtcState           = static_cast<uint8_t>(State::Moving);
    rtcStopRemainingMs = static_cast<int32_t>(remainingMs);
  } else {
    rtcState           = static_cast<uint8_t>(State::Standby);
    rtcStopRemainingMs = 0;
  }
}

const char* MotionTracker::stateName(State state) {
  switch (state) {
    case State::Standby:
      return "STANDBY";
    case State::Checking:
      return "CHECKING";
    case State::Moving:
      return "MOVING";
    default:
      return "?";
  }
}

void MotionTracker::startChecking(const DeviceSettings& settings,
                                  const char* reason) {
  const int64_t nowUs = esp_timer_get_time();
  const uint32_t waitS = settings.motion().wakeWaitSeconds();

  state_              = State::Checking;
  checkStartedUs_     = nowUs;
  deadlineUs_         = nowUs + static_cast<int64_t>(waitS) * kUsPerS;
  checkPublished_     = false;
  checkAnchorPending_ = false;

  ESP_LOGI(TAG, "CHECKING for movement for up to %us (%s).", (unsigned)waitS,
           reason);
}

const char* MotionTracker::wakeReason(esp_sleep_wakeup_cause_t cause) {
  switch (cause) {
    case ESP_SLEEP_WAKEUP_EXT1:
      return "accelerometer wake";
    case ESP_SLEEP_WAKEUP_TIMER:
      return "timer wake";
    case ESP_SLEEP_WAKEUP_EXT0:
      return "power switch on";
    case ESP_SLEEP_WAKEUP_UNDEFINED:
      return "power-on / reset";
    default:
      return "other wake";
  }
}
