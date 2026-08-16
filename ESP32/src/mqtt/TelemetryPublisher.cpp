#include "mqtt/TelemetryPublisher.h"

#include <cstdio>

#include "cJSON.h"
#include "esp_log.h"

static const char* TAG = "TelemetryPublisher";

TelemetryPublisher::TelemetryPublisher(MqttClient& mqtt, PayloadCrypto& crypto,
                                       const char* topic, const char* deviceId)
    : mqtt_(mqtt), crypto_(crypto), topic_(topic), deviceId_(deviceId) {}

std::string TelemetryPublisher::buildPayloadJson(
    const TelemetrySample& sample) const {
  const GnssFix& fix = sample.gnss;

  // Render the UTC timestamp as ISO-8601, the format the ingest side expects.
  char isoTime[32];
  std::snprintf(isoTime, sizeof(isoTime), "%04u-%02u-%02uT%02u:%02u:%02uZ",
                fix.time.year, fix.time.month, fix.time.day, fix.time.hour,
                fix.time.minute, fix.time.second);

  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    return std::string();
  }

  // Field names must match the API's PositionPayloadDto ([JsonPropertyName]).
  cJSON_AddStringToObject(root, "device", deviceId_);
  cJSON_AddNumberToObject(root, "latitude_deg", fix.position.latitudeDeg);
  cJSON_AddNumberToObject(root, "longitude_deg", fix.position.longitudeDeg);
  cJSON_AddNumberToObject(root, "speed_kmph", fix.speedKmph);
  cJSON_AddNumberToObject(root, "altitude_m", fix.position.altitudeMeters);
  cJSON_AddStringToObject(root, "time_utc", isoTime);

  // Battery, accelerometer and modem temperature only when their reading is
  // valid, so a failed or disabled sensor leaves the field absent rather than
  // sending a bogus 0. Battery is reported as a percent only - the raw pack
  // millivolts stay on the serial console (see BatteryData.h), never the wire.
  if (sample.battery.valid) {
    cJSON_AddNumberToObject(root, "battery_pct", sample.battery.percent);
  }
  if (sample.accel.valid) {
    cJSON_AddNumberToObject(root, "accel_x_g", sample.accel.xG);
    cJSON_AddNumberToObject(root, "accel_y_g", sample.accel.yG);
    cJSON_AddNumberToObject(root, "accel_z_g", sample.accel.zG);
  }
  if (sample.modem.valid) {
    cJSON_AddNumberToObject(root, "temp_c", sample.modem.temperatureC);
  }

  std::string json;
  char* printed = cJSON_PrintUnformatted(root);
  if (printed != nullptr) {
    json.assign(printed);
    cJSON_free(printed);
  }
  cJSON_Delete(root);
  return json;
}

bool TelemetryPublisher::sealSample(const TelemetrySample& sample,
                                   std::string& envelopeOut) const {
  // 1. Format the sample as plaintext JSON.
  const std::string plaintext = buildPayloadJson(sample);
  if (plaintext.empty()) {
    ESP_LOGE(TAG, "failed to build payload JSON");
    return false;
  }

  // 2. End-to-end encrypt it (the broker/SD card only ever see this envelope).
  if (!crypto_.encrypt(plaintext, envelopeOut)) {
    ESP_LOGE(TAG, "encryption failed");
    return false;
  }
  return true;
}

bool TelemetryPublisher::publishSample(const TelemetrySample& sample) {
  // Seal the sample into its encrypted envelope, then hand it to the broker.
  std::string envelope;
  if (!sealSample(sample, envelope)) {
    return false;
  }
  if (!mqtt_.publish(topic_, envelope)) {
    return false;
  }
  ESP_LOGI(TAG, "published encrypted fix to %s (%u bytes)", topic_,
           (unsigned)envelope.size());
  return true;
}
