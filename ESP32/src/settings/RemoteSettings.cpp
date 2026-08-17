#include "settings/RemoteSettings.h"

#include "esp_log.h"
#include "esp_timer.h"
#include "settings/SettingsCodec.h"

static const char* TAG = "RemoteSettings";

// The single bit in events_: "a config payload is waiting in pendingPayload_".
static constexpr EventBits_t kConfigArrivedBit = BIT0;

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
      nextResyncUs_(0),
      events_(xEventGroupCreate()),
      mutex_(xSemaphoreCreateMutex()),
      pending_(false) {}

RemoteSettings::~RemoteSettings() {
  if (mutex_ != nullptr) {
    vSemaphoreDelete(mutex_);
  }
  if (events_ != nullptr) {
    vEventGroupDelete(events_);
  }
}

bool RemoteSettings::begin(const DeviceSettings& initial) {
  current_ = initial;

  if (mutex_ == nullptr || events_ == nullptr) {
    ESP_LOGE(TAG, "could not create sync primitives - remote config disabled.");
    return false;
  }

  mqtt_.addMessageHandler(
      [this](const std::string& topic, const std::string& payload) {
        onMessage(topic, payload);
      });

  // Deferred until the link is up; MqttClient replays it on every connect.
  return mqtt_.subscribe(topic_, kConfigSubscribeQos);
}

void RemoteSettings::onMessage(const std::string& topic,
                               const std::string& payload) {
  // Load-bearing: MqttClient hands every message to every handler, so this is
  // what stops us trying to parse a delivery ack as a config document.
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

  // Wake the application task, which is almost certainly blocked in
  // waitForUpdate() waiting out the reporting interval. Set *after* releasing
  // the mutex so the woken task never immediately blocks on a lock we still
  // hold. This is the whole of the fast path - no parsing, no card write, and
  // nothing that could stall the esp-mqtt event task behind slow IO.
  xEventGroupSetBits(events_, kConfigArrivedBit);
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

  // Seed from the settings in force, so a document carrying only some of the
  // keys leaves the rest as they were.
  DeviceSettings incoming = current_;
  if (!SettingsCodec::decode(payload.data(), payload.size(), incoming)) {
    ESP_LOGW(TAG, "ignoring unusable config message on %s", topic_);
    return false;
  }
  incoming.clampToLimits();

  // operator== compares the settings only, so the version is checked separately.
  // A version bump with identical values still has to be adopted: the number is
  // echoed back in every report, and that is how the dashboard learns the change
  // arrived. Skipping it here would leave the device reporting a stale revision
  // for ever, which looks exactly like an unreachable device.
  if (incoming == current_ && incoming.version() == current_.version()) {
    ESP_LOGI(TAG, "config message matches current settings - nothing to do.");
    return true;
  }

  ESP_LOGI(TAG,
           "new settings (v%u): interval=%us sleep_between=%s fix_timeout=%us "
           "queue_max=%u retry=%uh/%uh config_check=%us",
           (unsigned)incoming.version(), (unsigned)incoming.intervalSeconds(),
           incoming.sleepBetweenSends() ? "yes" : "no",
           (unsigned)incoming.fixTimeoutSeconds(),
           (unsigned)incoming.queueMaxFixes(),
           (unsigned)incoming.retryIntervalHours(),
           (unsigned)incoming.retryMaxAgeHours(),
           (unsigned)incoming.configCheckSeconds());

  // Adopt them immediately; caching is best-effort. If the card write fails we
  // still honour the new settings for this run and will re-fetch them from the
  // broker after the next reboot.
  current_ = incoming;
  store_.save(current_);
  return true;
}

bool RemoteSettings::waitForUpdate(uint32_t timeoutMs) {
  if (events_ == nullptr) {
    return false;
  }

  // A payload may already be sitting there from before this call (it arrived
  // during the last acquire, say). Take it without blocking at all.
  if (poll()) {
    return true;
  }

  // Deadline rather than a running total: an unusable message consumes part of
  // the timeout and we go round again, so "how long is left" has to be measured
  // against the clock, not against how many times we have looped.
  const int64_t deadlineUs =
      esp_timer_get_time() + static_cast<int64_t>(timeoutMs) * 1000LL;

  while (true) {
    const int64_t remainingUs = deadlineUs - esp_timer_get_time();
    if (remainingUs <= 0) {
      return false;
    }

    // The task is genuinely asleep here for up to the whole remaining time -
    // no polling - and is woken by onMessage() the moment a config lands.
    // pdTRUE clears the bit on exit so the next wait starts clean.
    const EventBits_t bits =
        xEventGroupWaitBits(events_, kConfigArrivedBit, pdTRUE, pdFALSE,
                            pdMS_TO_TICKS(remainingUs / 1000));

    if ((bits & kConfigArrivedBit) == 0) {
      return false;  // timed out with nothing delivered
    }
    if (poll()) {
      return true;
    }
    // Signalled but the document was unusable (poll() has logged it). Keep
    // waiting out the rest of the timeout rather than reporting failure early.
  }
}

bool RemoteSettings::resyncIfDue(uint32_t intervalSeconds) {
  // Disabled, or the link is down - either way there is nothing useful to do.
  // Note we do NOT arm the timer while offline, so the first resync after a
  // long outage happens promptly rather than one full interval later.
  if (intervalSeconds == 0 || !mqtt_.isConnected()) {
    return false;
  }

  const int64_t nowUs = esp_timer_get_time();
  if (nowUs < nextResyncUs_) {
    return false;
  }
  nextResyncUs_ = nowUs + static_cast<int64_t>(intervalSeconds) * 1000000LL;

  // A plain re-SUBSCRIBE, not an unsubscribe/subscribe pair: the broker must
  // re-send matching retained messages on a repeat SUBSCRIBE and must not
  // interrupt the flow of publications while doing it (MQTT 3.1.1 [MQTT-3.8.4-3]).
  // So this asks for a fresh copy at the cost of one packet, and there is no
  // moment where we are unsubscribed and a live config could slip past.
  if (!mqtt_.subscribe(topic_, kConfigSubscribeQos)) {
    ESP_LOGW(TAG, "config re-check could not subscribe to %s", topic_);
    return false;
  }
  ESP_LOGD(TAG, "asked the broker to re-send the retained config on %s", topic_);
  return true;
}
