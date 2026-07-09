#include "mqtt/MqttClient.h"

#include "esp_crt_bundle.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "MqttClient";

// How often publishConfirmed() re-checks for the broker's ack while waiting.
static constexpr uint32_t kAckPollStepMs = 20;

MqttClient::MqttClient(const char* uri, const char* username,
                       const char* password, const char* clientId)
    : uri_(uri),
      username_(username),
      password_(password),
      clientId_(clientId),
      client_(nullptr),
      connected_(false),
      lastAckedMsgId_(-1) {}

MqttClient::~MqttClient() {
  if (client_ != nullptr) {
    esp_mqtt_client_destroy(client_);
  }
}

bool MqttClient::begin() {
  esp_mqtt_client_config_t cfg = {};  // zero-initialise; we set what we need
  cfg.broker.address.uri = uri_;

  // For TLS schemes (wss/mqtts) verify the broker's certificate against the
  // built-in public CA bundle. Ignored for the plain ws/mqtt schemes.
  cfg.broker.verification.crt_bundle_attach = esp_crt_bundle_attach;

  // Only pass credentials we actually have, so an empty username/password is
  // treated as "no credential" rather than an empty string.
  if (username_ != nullptr && username_[0] != '\0') {
    cfg.credentials.username = username_;
  }
  if (password_ != nullptr && password_[0] != '\0') {
    cfg.credentials.authentication.password = password_;
  }
  if (clientId_ != nullptr && clientId_[0] != '\0') {
    cfg.credentials.client_id = clientId_;
  }

  client_ = esp_mqtt_client_init(&cfg);
  if (client_ == nullptr) {
    ESP_LOGE(TAG, "client init failed");
    return false;
  }

  esp_mqtt_client_register_event(client_, MQTT_EVENT_ANY,
                                 &MqttClient::eventHandler, this);

  const esp_err_t err = esp_mqtt_client_start(client_);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "client start failed: %s", esp_err_to_name(err));
    return false;
  }
  ESP_LOGI(TAG, "MQTT client started; connecting to broker...");
  return true;
}

bool MqttClient::isConnected() const { return connected_; }

bool MqttClient::publish(const std::string& topic, const std::string& payload) {
  if (client_ == nullptr || !connected_) {
    ESP_LOGW(TAG, "publish skipped - not connected");
    return false;
  }
  // QoS 2 (exactly once), no retain. Returns the message id, or -1 on failure.
  const int msgId = esp_mqtt_client_publish(client_, topic.c_str(),
                                            payload.data(), payload.size(),
                                            /*qos=*/2, /*retain=*/0);
  if (msgId < 0) {
    ESP_LOGW(TAG, "publish failed");
    return false;
  }
  return true;
}

bool MqttClient::publishConfirmed(const std::string& topic,
                                  const std::string& payload,
                                  uint32_t timeoutMs) {
  if (client_ == nullptr || !connected_) {
    ESP_LOGW(TAG, "publishConfirmed skipped - not connected");
    return false;
  }

  // Clear any previous ack before publishing so a stale id (e.g. a reused
  // message id after a reconnect, or a late ack for an earlier timed-out
  // message) can never be mistaken for this message's ack.
  lastAckedMsgId_ = -1;

  // QoS 2 (exactly once) so the broker returns a PUBLISHED (PUBCOMP) ack we can
  // wait on. The message id ties that ack back to this specific publish.
  const int msgId = esp_mqtt_client_publish(client_, topic.c_str(),
                                            payload.data(), payload.size(),
                                            /*qos=*/2, /*retain=*/0);
  if (msgId < 0) {
    ESP_LOGW(TAG, "publishConfirmed: enqueue failed");
    return false;
  }

  // Poll until the callback records this msgId as acked, the link drops, or we
  // run out of time. Publishes are serialised by the caller (the main loop), so
  // a single "last acked id" is enough to disambiguate.
  uint32_t waited = 0;
  while (waited < timeoutMs) {
    if (lastAckedMsgId_ == msgId) {
      return true;  // broker confirmed receipt
    }
    if (!connected_) {
      ESP_LOGW(TAG, "publishConfirmed: disconnected before ack (msg %d)", msgId);
      return false;
    }
    vTaskDelay(pdMS_TO_TICKS(kAckPollStepMs));
    waited += kAckPollStepMs;
  }
  ESP_LOGW(TAG, "publishConfirmed: no ack within %ums (msg %d)",
           (unsigned)timeoutMs, msgId);
  return false;
}

void MqttClient::eventHandler(void* arg, esp_event_base_t /*base*/,
                              int32_t eventId, void* eventData) {
  auto* self = static_cast<MqttClient*>(arg);
  auto  event = static_cast<esp_mqtt_event_handle_t>(eventData);

  switch (static_cast<esp_mqtt_event_id_t>(eventId)) {
    case MQTT_EVENT_CONNECTED:
      self->connected_ = true;
      ESP_LOGI(TAG, "connected to broker");
      break;
    case MQTT_EVENT_DISCONNECTED:
      self->connected_ = false;
      ESP_LOGW(TAG, "disconnected from broker (will retry)");
      break;
    case MQTT_EVENT_PUBLISHED:
      // Broker completed the QoS-2 handshake for this message id. Record it so
      // publishConfirmed() can release, letting the queue drop the delivered
      // envelope(s).
      if (event != nullptr) {
        self->lastAckedMsgId_ = event->msg_id;
      }
      break;
    case MQTT_EVENT_ERROR:
      ESP_LOGE(TAG, "MQTT error");
      if (event != nullptr && event->error_handle != nullptr &&
          event->error_handle->error_type == MQTT_ERROR_TYPE_TCP_TRANSPORT) {
        ESP_LOGE(TAG, "  transport error (TLS/socket): esp-tls 0x%x",
                 event->error_handle->esp_tls_last_esp_err);
      }
      break;
    default:
      break;
  }
}
