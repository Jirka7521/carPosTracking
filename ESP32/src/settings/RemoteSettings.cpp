#include "settings/RemoteSettings.h"

#include "esp_log.h"
#include "freertos/task.h"
#include "settings/SettingsCodec.h"

static const char* TAG = "RemoteSettings";

// How often waitForUpdate() re-checks while waiting for the retained config.
static constexpr uint32_t kWaitPollStepMs = 50;

// QoS 1 is the right level for config: the broker keeps trying until we have it,
// and a duplicate delivery is harmless because applying the same settings twice
// changes nothing (and, thanks to the equality check in poll(), does not even
// rewrite the card).
static constexpr int kConfigSubscribeQos = 1;

RemoteSettings::RemoteSettings(MqttClient& mqtt, SettingsStore& store,
                               const char* topic)
    : mqtt_(mqtt),
      store_(store),
      topic_(topic),
      mutex_(xSemaphoreCreateMutex()),
      pending_(false) {}

RemoteSettings::~RemoteSettings() {
  if (mutex_ != nullptr) {
    vSemaphoreDelete(mutex_);
  }
}

bool RemoteSettings::begin(const DeviceSettings& initial) {
  current_ = initial;

  if (mutex_ == nullptr) {
    ESP_LOGE(TAG, "could not create mutex - remote config disabled.");
    return false;
  }

  mqtt_.setMessageHandler(
      [this](const std::string& topic, const std::string& payload) {
        onMessage(topic, payload);
      });

  // Deferred until the link is up; MqttClient replays it on every connect.
  return mqtt_.subscribe(topic_, kConfigSubscribeQos);
}

void RemoteSettings::onMessage(const std::string& topic,
                               const std::string& payload) {
  // We only ever subscribe to one topic, but check anyway - a wildcard
  // subscription added later should not silently feed us the wrong document.
  if (topic != topic_) {
    return;
  }

  if (xSemaphoreTake(mutex_, portMAX_DELAY) != pdTRUE) {
    return;
  }
  // A config that arrives while an older one is still unprocessed simply
  // replaces it: only the newest configuration is of any interest.
  pendingPayload_ = payload;
  pending_        = true;
  xSemaphoreGive(mutex_);
}

bool RemoteSettings::takePending(std::string& payloadOut) {
  if (mutex_ == nullptr) {
    return false;
  }
  if (xSemaphoreTake(mutex_, portMAX_DELAY) != pdTRUE) {
    return false;
  }
  const bool had = pending_;
  if (had) {
    payloadOut.swap(pendingPayload_);
    pendingPayload_.clear();
    pending_ = false;
  }
  xSemaphoreGive(mutex_);
  return had;
}

bool RemoteSettings::poll() {
  std::string payload;
  if (!takePending(payload)) {
    return false;  // nothing new
  }

  // Seed from the settings in force, so a document carrying only one of the two
  // keys leaves the other as it was.
  DeviceSettings incoming = current_;
  if (!SettingsCodec::decode(payload.data(), payload.size(), incoming)) {
    ESP_LOGW(TAG, "ignoring unusable config message on %s", topic_);
    return false;
  }
  incoming.clampToLimits();

  if (incoming == current_) {
    ESP_LOGI(TAG, "config message matches current settings - nothing to do.");
    return true;
  }

  ESP_LOGI(TAG, "new settings: interval=%us sleep_between=%s",
           (unsigned)incoming.intervalSeconds(),
           incoming.sleepBetweenSends() ? "yes" : "no");

  // Adopt them immediately; caching is best-effort. If the card write fails we
  // still honour the new settings for this run and will re-fetch them from the
  // broker after the next reboot.
  current_ = incoming;
  store_.save(current_);
  return true;
}

bool RemoteSettings::waitForUpdate(uint32_t timeoutMs) {
  uint32_t waited = 0;
  while (waited < timeoutMs) {
    if (poll()) {
      return true;
    }
    vTaskDelay(pdMS_TO_TICKS(kWaitPollStepMs));
    waited += kWaitPollStepMs;
  }
  ESP_LOGI(TAG,
           "no config from broker within %ums - continuing with cached "
           "settings (is it published retained?)",
           (unsigned)timeoutMs);
  return false;
}
