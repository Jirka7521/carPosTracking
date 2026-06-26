#include "mqtt/MqttClient.h"

#include "esp_crt_bundle.h"
#include "esp_log.h"

static const char* TAG = "MqttClient";

MqttClient::MqttClient(const char* uri, const char* username,
                       const char* password, const char* clientId)
    : uri_(uri),
      username_(username),
      password_(password),
      clientId_(clientId),
      client_(nullptr),
      connected_(false) {}

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
