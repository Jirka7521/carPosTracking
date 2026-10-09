#include "mqtt/StatusPublisher.h"

#include "cJSON.h"
#include "esp_log.h"
#include "util/CivilTime.h"

static const char* TAG = "StatusPublisher";

// The message types. Spelled once so the Will and the live messages cannot
// disagree on them - the API rejects anything else.
static constexpr char kTypeOnline[]  = "online";
static constexpr char kTypeOffline[] = "offline";
static constexpr char kTypeWake[]    = "wake";
static constexpr char kTypeMotion[]  = "motion";

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
    case OfflineReason::SleepNoMotion:
      return "sleep_no_motion";
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

const char* StatusPublisher::wakeName(WakeCause cause) {
  switch (cause) {
    case WakeCause::Timer:
      return "timer";
    case WakeCause::Accelerometer:
      return "accelerometer";
    case WakeCause::PowerSwitch:
      return "power_switch";
  }
  return "timer";  // unreachable; keeps the compiler sure every path returns
}

const char* StatusPublisher::motionName(MotionChange change) {
  switch (change) {
    case MotionChange::Checking:
      return "checking";
    case MotionChange::Activity:
      return "activity";
    case MotionChange::MotionOn:
      return "motion_on";
    case MotionChange::Moving:
      return "moving";
    case MotionChange::NoMotion:
      return "no_motion";
    case MotionChange::Stopped:
      return "stopped";
    case MotionChange::MotionOff:
      return "motion_off";
  }
  return "checking";  // unreachable; keeps the compiler sure every path returns
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

bool StatusPublisher::sealBare(const std::string& plaintext,
                               std::string& envelopeOut) {
  if (plaintext.empty()) {
    ESP_LOGE(TAG, "failed to build status JSON");
    return false;
  }
  if (!crypto_.encrypt(plaintext, envelopeOut)) {
    ESP_LOGE(TAG, "status encryption failed");
    return false;
  }
  return true;
}

bool StatusPublisher::seal(const std::string& plaintext,
                           std::string& messageOut) {
  std::string envelope;
  if (!sealBare(plaintext, envelope)) {
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
  // Confirmed (QoS 2, waits for PUBCOMP) because PresenceReporter only stops
  // re-sending the reset reason once a delivery is confirmed - "handed to the
  // client" would clear it while it could still die in the outbox.
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

bool StatusPublisher::sealOffline(OfflineReason reason, uint32_t sleepS,
                                  int batteryPct, const char* detail,
                                  std::string& envelopeOut) {
  const std::string plaintext =
      buildJson(kTypeOffline, reasonName(reason), /*withTime=*/true, sleepS,
                batteryPct, nullptr, detail);
  return sealBare(plaintext, envelopeOut);
}

bool StatusPublisher::sealWake(WakeCause cause, std::string& envelopeOut) {
  const std::string plaintext =
      buildJson(kTypeWake, wakeName(cause), /*withTime=*/true, 0, -1, nullptr,
                nullptr);
  return sealBare(plaintext, envelopeOut);
}

bool StatusPublisher::sealMotion(MotionChange change,
                                 std::string& envelopeOut) {
  const std::string plaintext =
      buildJson(kTypeMotion, motionName(change), /*withTime=*/true, 0, -1,
                nullptr, nullptr);
  return sealBare(plaintext, envelopeOut);
}
