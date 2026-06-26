#include "mqtt/TelemetryPublisher.h"

#include <cstdio>

#include "cJSON.h"
#include "esp_log.h"

static const char* TAG = "TelemetryPublisher";

TelemetryPublisher::TelemetryPublisher(MqttClient& mqtt, PayloadCrypto& crypto,
                                       const char* topic, const char* deviceId)
    : mqtt_(mqtt), crypto_(crypto), topic_(topic), deviceId_(deviceId) {}

std::string TelemetryPublisher::buildPayloadJson(const GnssFix& fix) const {
  // Render the UTC timestamp as ISO-8601, the format the desktop expects.
  char isoTime[32];
  std::snprintf(isoTime, sizeof(isoTime), "%04u-%02u-%02uT%02u:%02u:%02uZ",
                fix.time.year, fix.time.month, fix.time.day, fix.time.hour,
                fix.time.minute, fix.time.second);

  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    return std::string();
  }

  // Field names must match desktop/gnss_payload.py (GnssPayload).
  cJSON_AddStringToObject(root, "device", deviceId_);
  cJSON_AddNumberToObject(root, "latitude_deg", fix.position.latitudeDeg);
  cJSON_AddNumberToObject(root, "longitude_deg", fix.position.longitudeDeg);
  cJSON_AddNumberToObject(root, "speed_kmph", fix.speedKmph);
  cJSON_AddNumberToObject(root, "altitude_m", fix.position.altitudeMeters);
  cJSON_AddStringToObject(root, "time_utc", isoTime);

  std::string json;
  char* printed = cJSON_PrintUnformatted(root);
  if (printed != nullptr) {
    json.assign(printed);
    cJSON_free(printed);
  }
  cJSON_Delete(root);
  return json;
}

bool TelemetryPublisher::publishFix(const GnssFix& fix) {
  // 1. Format the position as plaintext JSON.
  const std::string plaintext = buildPayloadJson(fix);
  if (plaintext.empty()) {
    ESP_LOGE(TAG, "failed to build payload JSON");
    return false;
  }

  // 2. End-to-end encrypt it (the broker will only ever see this envelope).
  std::string envelope;
  if (!crypto_.encrypt(plaintext, envelope)) {
    ESP_LOGE(TAG, "encryption failed - not publishing");
    return false;
  }

  // 3. Publish the encrypted envelope.
  if (!mqtt_.publish(topic_, envelope)) {
    return false;
  }
  ESP_LOGI(TAG, "published encrypted fix to %s (%u bytes)", topic_,
           (unsigned)envelope.size());
  return true;
}
