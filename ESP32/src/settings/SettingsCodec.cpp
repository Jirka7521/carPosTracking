#include "settings/SettingsCodec.h"

#include <cmath>

#include "cJSON.h"
#include "esp_log.h"

static const char* TAG = "SettingsCodec";

const char* const SettingsCodec::kVersionKey       = "version";
const char* const SettingsCodec::kIntervalKey      = "interval_s";
const char* const SettingsCodec::kSleepKey         = "sleep_between";
const char* const SettingsCodec::kFixTimeoutKey    = "fix_timeout_s";
const char* const SettingsCodec::kQueueMaxFixesKey = "queue_max_fixes";
const char* const SettingsCodec::kRetryIntervalKey = "retry_interval_h";
const char* const SettingsCodec::kRetryMaxAgeKey   = "retry_max_age_h";
const char* const SettingsCodec::kConfigCheckKey   = "config_check_s";
const char* const SettingsCodec::kMotionKey        = "motion";
const char* const SettingsCodec::kEnabledKey       = "enabled";
const char* const SettingsCodec::kThresholdKey     = "threshold_mg";
const char* const SettingsCodec::kSpeedKey         = "speed_kmph";
const char* const SettingsCodec::kWakeWaitKey      = "wake_wait_s";
const char* const SettingsCodec::kStopWaitKey      = "stop_wait_s";
const char* const SettingsCodec::kMovingKey        = "moving";

void SettingsCodec::encodeInto(cJSON* object, const DeviceSettings& settings,
                               bool includeVersion) {
  // Version first, and only when we actually have one: a device that has never
  // received a config message should not claim to be running revision 0. A
  // profile inside a schedule bundle has no revision at all, which is why the
  // caller can suppress the key outright.
  if (includeVersion && settings.version() != 0) {
    cJSON_AddNumberToObject(object, kVersionKey,
                            static_cast<double>(settings.version()));
  }

  encodeMode(object, settings.standby());
  encodeShared(object, settings);
  encodeMotion(object, settings.motion());
}

void SettingsCodec::encodeMode(cJSON* object, const ModeSettings& mode) {
  cJSON_AddNumberToObject(object, kIntervalKey,
                          static_cast<double>(mode.intervalSeconds()));
  cJSON_AddBoolToObject(object, kSleepKey, mode.sleepBetweenSends());
  cJSON_AddNumberToObject(object, kFixTimeoutKey,
                          static_cast<double>(mode.fixTimeoutSeconds()));
}

void SettingsCodec::encodeShared(cJSON* root, const DeviceSettings& settings) {
  cJSON_AddNumberToObject(root, kQueueMaxFixesKey,
                          static_cast<double>(settings.queueMaxFixes()));
  cJSON_AddNumberToObject(root, kRetryIntervalKey,
                          static_cast<double>(settings.retryIntervalHours()));
  cJSON_AddNumberToObject(root, kRetryMaxAgeKey,
                          static_cast<double>(settings.retryMaxAgeHours()));
  cJSON_AddNumberToObject(root, kConfigCheckKey,
                          static_cast<double>(settings.configCheckSeconds()));
}

void SettingsCodec::encodeMotion(cJSON* root, const MotionSettings& motion) {
  // Always written, even when the feature is off: the card copy is what a
  // device reboots onto with no network, and a document that dropped the block
  // while it was disabled would forget the thresholds somebody had tuned.
  cJSON* object = cJSON_AddObjectToObject(root, kMotionKey);
  if (object == nullptr) {
    ESP_LOGE(TAG, "encode: out of memory for '%s'", kMotionKey);
    return;
  }

  cJSON_AddBoolToObject(object, kEnabledKey, motion.enabled());
  cJSON_AddNumberToObject(object, kThresholdKey,
                          static_cast<double>(motion.thresholdMg()));
  cJSON_AddNumberToObject(object, kSpeedKey,
                          static_cast<double>(motion.speedKmph()));
  cJSON_AddNumberToObject(object, kWakeWaitKey,
                          static_cast<double>(motion.wakeWaitSeconds()));
  cJSON_AddNumberToObject(object, kStopWaitKey,
                          static_cast<double>(motion.stopWaitSeconds()));

  cJSON* moving = cJSON_AddObjectToObject(object, kMovingKey);
  if (moving == nullptr) {
    ESP_LOGE(TAG, "encode: out of memory for '%s'", kMovingKey);
    return;
  }
  encodeMode(moving, motion.moving());
}

std::string SettingsCodec::encode(const DeviceSettings& settings) {
  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    ESP_LOGE(TAG, "encode: out of memory");
    return std::string();
  }

  encodeInto(root, settings, /*includeVersion=*/true);

  std::string out;
  char*       printed = cJSON_PrintUnformatted(root);
  if (printed != nullptr) {
    out = printed;
    cJSON_free(printed);
  } else {
    ESP_LOGE(TAG, "encode: serialisation failed");
  }
  cJSON_Delete(root);
  return out;
}

bool SettingsCodec::readUint(const cJSON* root, const char* key,
                             uint32_t& out) {
  const cJSON* item = cJSON_GetObjectItemCaseSensitive(root, key);
  if (item == nullptr) {
    return false;  // absent is not an error - the document is a partial update
  }

  // Reject NaN/inf and negatives before the cast: converting those to an
  // unsigned integer is undefined behaviour, and a negative interval (or cap,
  // or timeout) is meaningless anyway.
  if (!cJSON_IsNumber(item) || !std::isfinite(item->valuedouble) ||
      item->valuedouble < 0.0) {
    ESP_LOGW(TAG, "decode: '%s' is not a non-negative number - ignored", key);
    return false;
  }

  out = static_cast<uint32_t>(item->valuedouble);
  return true;
}

bool SettingsCodec::readBool(const cJSON* root, const char* key, bool& out) {
  const cJSON* item = cJSON_GetObjectItemCaseSensitive(root, key);
  if (item == nullptr) {
    return false;  // absent, exactly as for readUint()
  }
  if (!cJSON_IsBool(item)) {
    ESP_LOGW(TAG, "decode: '%s' is not a boolean - ignored", key);
    return false;
  }
  out = cJSON_IsTrue(item);
  return true;
}

bool SettingsCodec::decode(const char* json, std::size_t length,
                           DeviceSettings& settings) {
  if (json == nullptr || length == 0) {
    return false;
  }

  // ParseWithLength, not Parse: an MQTT payload is a length-delimited byte range
  // and is not guaranteed to be NUL-terminated.
  cJSON* root = cJSON_ParseWithLength(json, length);
  if (root == nullptr) {
    ESP_LOGW(TAG, "decode: payload is not valid JSON");
    return false;
  }
  if (!cJSON_IsObject(root)) {
    ESP_LOGW(TAG, "decode: payload is not a JSON object");
    cJSON_Delete(root);
    return false;
  }

  const bool ok = decodeObject(root, settings);
  cJSON_Delete(root);
  return ok;
}

bool SettingsCodec::decodeObject(const cJSON* root, DeviceSettings& settings) {
  if (root == nullptr || !cJSON_IsObject(root)) {
    return false;
  }

  // Decode into a copy so a document that turns out to carry nothing usable
  // cannot half-update the caller's settings.
  DeviceSettings decoded     = settings;
  bool           sawKnownKey = false;
  uint32_t       value       = 0;

  if (readUint(root, kVersionKey, value)) {
    decoded.setVersion(value);
    sawKnownKey = true;
  }

  ModeSettings standby = decoded.standby();
  if (decodeMode(root, standby)) {
    decoded.setStandby(standby);
    sawKnownKey = true;
  }

  if (decodeShared(root, decoded)) {
    sawKnownKey = true;
  }

  MotionSettings motion = decoded.motion();
  if (decodeMotion(root, motion)) {
    decoded.setMotion(motion);
    sawKnownKey = true;
  }

  if (!sawKnownKey) {
    ESP_LOGW(TAG, "decode: document carried no field we recognise");
    return false;
  }

  settings = decoded;
  return true;
}

bool SettingsCodec::decodeMode(const cJSON* object, ModeSettings& mode) {
  bool     sawKnownKey = false;
  uint32_t value       = 0;
  bool     flag        = false;

  if (readUint(object, kIntervalKey, value)) {
    mode.setIntervalSeconds(value);
    sawKnownKey = true;
  }
  if (readBool(object, kSleepKey, flag)) {
    mode.setSleepBetweenSends(flag);
    sawKnownKey = true;
  }
  if (readUint(object, kFixTimeoutKey, value)) {
    mode.setFixTimeoutSeconds(value);
    sawKnownKey = true;
  }
  return sawKnownKey;
}

bool SettingsCodec::decodeShared(const cJSON* root, DeviceSettings& settings) {
  bool     sawKnownKey = false;
  uint32_t value       = 0;

  if (readUint(root, kQueueMaxFixesKey, value)) {
    settings.setQueueMaxFixes(value);
    sawKnownKey = true;
  }
  if (readUint(root, kRetryIntervalKey, value)) {
    settings.setRetryIntervalHours(value);
    sawKnownKey = true;
  }
  if (readUint(root, kRetryMaxAgeKey, value)) {
    settings.setRetryMaxAgeHours(value);
    sawKnownKey = true;
  }
  if (readUint(root, kConfigCheckKey, value)) {
    settings.setConfigCheckSeconds(value);
    sawKnownKey = true;
  }
  return sawKnownKey;
}

bool SettingsCodec::decodeMotion(const cJSON* root, MotionSettings& motion) {
  const cJSON* object = cJSON_GetObjectItemCaseSensitive(root, kMotionKey);
  if (object == nullptr) {
    return false;  // an older publisher: the block keeps what it had
  }
  if (!cJSON_IsObject(object)) {
    ESP_LOGW(TAG, "decode: '%s' is not an object - ignored", kMotionKey);
    return false;
  }

  bool     sawKnownKey = false;
  uint32_t value       = 0;
  bool     flag        = false;

  if (readBool(object, kEnabledKey, flag)) {
    motion.setEnabled(flag);
    sawKnownKey = true;
  }
  if (readUint(object, kThresholdKey, value)) {
    motion.setThresholdMg(value);
    sawKnownKey = true;
  }
  if (readUint(object, kSpeedKey, value)) {
    motion.setSpeedKmph(value);
    sawKnownKey = true;
  }
  if (readUint(object, kWakeWaitKey, value)) {
    motion.setWakeWaitSeconds(value);
    sawKnownKey = true;
  }
  if (readUint(object, kStopWaitKey, value)) {
    motion.setStopWaitSeconds(value);
    sawKnownKey = true;
  }

  // The moving set merges exactly like the top level: a partial "moving" changes
  // only the keys it carries. Only the three per-mode keys are read here; an
  // older document's moving queue/retry/re-check keys are simply not looked up.
  const cJSON* moving = cJSON_GetObjectItemCaseSensitive(object, kMovingKey);
  if (moving != nullptr) {
    if (cJSON_IsObject(moving)) {
      ModeSettings movingMode = motion.moving();
      if (decodeMode(moving, movingMode)) {
        motion.setMoving(movingMode);
        sawKnownKey = true;
      }
    } else {
      ESP_LOGW(TAG, "decode: '%s' is not an object - ignored", kMovingKey);
    }
  }

  return sawKnownKey;
}
