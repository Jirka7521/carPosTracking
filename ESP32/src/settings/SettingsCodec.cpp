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

std::string SettingsCodec::encode(const DeviceSettings& settings) {
  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    ESP_LOGE(TAG, "encode: out of memory");
    return std::string();
  }

  // Version first, and only when we actually have one: a device that has never
  // received a config message should not claim to be running revision 0.
  if (settings.version() != 0) {
    cJSON_AddNumberToObject(root, kVersionKey,
                            static_cast<double>(settings.version()));
  }

  cJSON_AddNumberToObject(root, kIntervalKey,
                          static_cast<double>(settings.intervalSeconds()));
  cJSON_AddBoolToObject(root, kSleepKey, settings.sleepBetweenSends());
  cJSON_AddNumberToObject(root, kFixTimeoutKey,
                          static_cast<double>(settings.fixTimeoutSeconds()));
  cJSON_AddNumberToObject(root, kQueueMaxFixesKey,
                          static_cast<double>(settings.queueMaxFixes()));
  cJSON_AddNumberToObject(root, kRetryIntervalKey,
                          static_cast<double>(settings.retryIntervalHours()));
  cJSON_AddNumberToObject(root, kRetryMaxAgeKey,
                          static_cast<double>(settings.retryMaxAgeHours()));
  cJSON_AddNumberToObject(root, kConfigCheckKey,
                          static_cast<double>(settings.configCheckSeconds()));

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

  // Decode into a copy so a document that turns out to carry nothing usable
  // cannot half-update the caller's settings.
  DeviceSettings decoded     = settings;
  bool           sawKnownKey = false;
  uint32_t       value       = 0;

  if (readUint(root, kVersionKey, value)) {
    decoded.setVersion(value);
    sawKnownKey = true;
  }
  if (readUint(root, kIntervalKey, value)) {
    decoded.setIntervalSeconds(value);
    sawKnownKey = true;
  }
  if (readUint(root, kFixTimeoutKey, value)) {
    decoded.setFixTimeoutSeconds(value);
    sawKnownKey = true;
  }
  if (readUint(root, kQueueMaxFixesKey, value)) {
    decoded.setQueueMaxFixes(value);
    sawKnownKey = true;
  }
  if (readUint(root, kRetryIntervalKey, value)) {
    decoded.setRetryIntervalHours(value);
    sawKnownKey = true;
  }
  if (readUint(root, kRetryMaxAgeKey, value)) {
    decoded.setRetryMaxAgeHours(value);
    sawKnownKey = true;
  }
  if (readUint(root, kConfigCheckKey, value)) {
    decoded.setConfigCheckSeconds(value);
    sawKnownKey = true;
  }

  // The one boolean, so it does not fit the readUint helper above.
  const cJSON* sleep = cJSON_GetObjectItemCaseSensitive(root, kSleepKey);
  if (sleep != nullptr) {
    if (cJSON_IsBool(sleep)) {
      decoded.setSleepBetweenSends(cJSON_IsTrue(sleep));
      sawKnownKey = true;
    } else {
      ESP_LOGW(TAG, "decode: '%s' is not a boolean - ignored", kSleepKey);
    }
  }

  cJSON_Delete(root);

  if (!sawKnownKey) {
    ESP_LOGW(TAG, "decode: document carried no field we recognise");
    return false;
  }

  settings = decoded;
  return true;
}
