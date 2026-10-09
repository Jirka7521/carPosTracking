#include "mqtt/StatusPublisher.h"

#include "cJSON.h"
#include "esp_log.h"
#include "util/CivilTime.h"

static const char* TAG = "StatusPublisher";

// The two message types. Spelled once so the Will and the live messages cannot
// disagree on them - the API rejects anything else.
static constexpr char kTypeOnline[]  = "online";
static constexpr char kTypeOffline[] = "offline";

StatusPublisher::StatusPublisher(MqttClient& mqtt, PayloadCrypto& crypto,
                                 DeviceClock& clock, const char* topic,
                                 const char* deviceId, uint32_t timeoutMs)
    : mqtt_(mqtt),
      crypto_(crypto),
      clock_(clock),
      topic_(topic),
      deviceId_(deviceId),
      timeoutMs_(timeoutMs) {}

const char* StatusPublisher::reasonName(OfflineReason reason) {
  switch (reason) {
    case OfflineReason::Sleep:
      return "sleep";
    case OfflineReason::PowerOff:
      return "power_off";
    case OfflineReason::BatteryLow:
      return "battery_low";
    case OfflineReason::Error:
      return "error";
    case OfflineReason::ConnectionLost:
      return "connection_lost";
  }
  return "error";  // unreachable; keeps the compiler sure every path returns
}

std::string StatusPublisher::buildJson(const char* type, const char* reason,
                                       bool withTime, uint32_t sleepS,
                                       int batteryPct, const char* resetReason,
                                       const char* detail) const {
  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    return std::string();
  }

  // Field names must match the API's DeviceStatusPayloadDto ([JsonPropertyName]).
  cJSON_AddStringToObject(root, "device", deviceId_);
  cJSON_AddStringToObject(root, "type", type);
  if (reason != nullptr) {
    cJSON_AddStringToObject(root, "reason", reason);
  }

  // Only a clock we trust gets to stamp the message - see the banner. The API
  // keeps its own receive time as the authoritative one regardless.
  if (withTime && clock_.isTrusted()) {
    int64_t epoch = 0;
    if (clock_.nowUtc(epoch)) {
      cJSON_AddStringToObject(root, "time_utc",
                              CivilTime::formatIso(epoch).c_str());
    }
  }

  if (batteryPct >= 0) {
    cJSON_AddNumberToObject(root, "battery_pct", batteryPct);
  }
  if (sleepS > 0) {
    cJSON_AddNumberToObject(root, "sleep_s", static_cast<double>(sleepS));
  }
  if (resetReason != nullptr && resetReason[0] != '\0') {
    cJSON_AddStringToObject(root, "reset_reason", resetReason);
  }
  if (detail != nullptr && detail[0] != '\0') {
    cJSON_AddStringToObject(root, "detail", detail);
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

bool StatusPublisher::seal(const std::string& plaintext,
                           std::string& messageOut) {
  if (plaintext.empty()) {
    ESP_LOGE(TAG, "failed to build status JSON");
    return false;
  }
  std::string envelope;
  if (!crypto_.encrypt(plaintext, envelope)) {
    ESP_LOGE(TAG, "status encryption failed");
    return false;
  }
  // One-element array: the shape the API's envelope decoder already expects
  // from a position batch, so both topics share it unchanged.
  messageOut.clear();
  messageOut.reserve(envelope.size() + 2);
  messageOut.push_back('[');
  messageOut.append(envelope);
  messageOut.push_back(']');
  return true;
}

bool StatusPublisher::sealAndPublish(const char* what,
                                     const std::string& plaintext) {
  std::string message;
  if (!seal(plaintext, message)) {
    return false;
  }
  // Confirmed (QoS 2, waits for PUBCOMP) because an offline message is followed
  // immediately by a DISCONNECT and a power-down: "handed to the client" is not
  // enough when the client is about to be switched off with it still queued.
  if (!mqtt_.publishConfirmed(topic_, message, timeoutMs_)) {
    ESP_LOGW(TAG, "%s status not confirmed by the broker", what);
    return false;
  }
  ESP_LOGI(TAG, "published %s status to %s (%u bytes)", what, topic_,
           (unsigned)message.size());
  return true;
}

bool StatusPublisher::sealLastWill(std::string& messageOut) {
  // No time and no battery: the broker publishes this at an unknown moment,
  // possibly hours after it was sealed, so anything time-bound would be stale.
  const std::string plaintext =
      buildJson(kTypeOffline, reasonName(OfflineReason::ConnectionLost),
                /*withTime=*/false, 0, -1, nullptr, nullptr);
  return seal(plaintext, messageOut);
}

bool StatusPublisher::publishOnline(const char* resetReason) {
  const std::string plaintext =
      buildJson(kTypeOnline, nullptr, /*withTime=*/true, 0, -1, resetReason,
                nullptr);
  return sealAndPublish("online", plaintext);
}

bool StatusPublisher::publishOffline(OfflineReason reason, uint32_t sleepS,
                                     int batteryPct, const char* detail) {
  const std::string plaintext =
      buildJson(kTypeOffline, reasonName(reason), /*withTime=*/true, sleepS,
                batteryPct, nullptr, detail);
  return sealAndPublish(reasonName(reason), plaintext);
}
