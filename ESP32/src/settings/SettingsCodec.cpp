#include "settings/SettingsCodec.h"

#include <cmath>

#include "cJSON.h"
#include "esp_log.h"

static const char* TAG = "SettingsCodec";

const char* const SettingsCodec::kIntervalKey = "interval_s";
const char* const SettingsCodec::kSleepKey    = "sleep_between";

std::string SettingsCodec::encode(const DeviceSettings& settings) {
  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    ESP_LOGE(TAG, "encode: out of memory");
    return std::string();
  }

  cJSON_AddNumberToObject(root, kIntervalKey,
                          static_cast<double>(settings.intervalSeconds()));
  cJSON_AddBoolToObject(root, kSleepKey, settings.sleepBetweenSends());

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
  DeviceSettings decoded = settings;
  bool           sawKnownKey = false;

  const cJSON* interval = cJSON_GetObjectItemCaseSensitive(root, kIntervalKey);
  if (interval != nullptr) {
    // Reject NaN/inf and negatives before the cast: converting those to an
    // unsigned integer is undefined behaviour, and a negative interval is
    // meaningless anyway.
    if (cJSON_IsNumber(interval) && std::isfinite(interval->valuedouble) &&
        interval->valuedouble >= 0.0) {
      decoded.setIntervalSeconds(
          static_cast<uint32_t>(interval->valuedouble));
      sawKnownKey = true;
    } else {
      ESP_LOGW(TAG, "decode: '%s' is not a non-negative number - ignored",
               kIntervalKey);
    }
  }

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
    ESP_LOGW(TAG, "decode: no usable '%s' or '%s' field", kIntervalKey,
             kSleepKey);
    return false;
  }

  settings = decoded;
  return true;
}
