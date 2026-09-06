#include "settings/RemoteSchedule.h"

#include "esp_log.h"
#include "esp_timer.h"
#include "settings/ScheduleCodec.h"

static const char* TAG = "RemoteSchedule";

// QoS 1, matching the config topic. The bundle is idempotent - applying the same
// revision twice changes nothing, and poll() below does not even rewrite the card
// for it - so at-least-once is the right trade and QoS 2 would buy a round trip
// for nothing.
static constexpr int kScheduleSubscribeQos = 1;

RemoteSchedule::RemoteSchedule(MqttClient& mqtt, ScheduleStore& store,
                               UpdateSignal& signal, const char* topic)
    : mqtt_(mqtt),
      store_(store),
      signal_(signal),
      topic_(topic),
      nextResyncUs_(0),
      mutex_(xSemaphoreCreateMutex()),
      pending_(false) {}

RemoteSchedule::~RemoteSchedule() {
  if (mutex_ != nullptr) {
    vSemaphoreDelete(mutex_);
  }
}

bool RemoteSchedule::begin(const ScheduleBundle& initial) {
  current_ = initial;

  if (mutex_ == nullptr) {
    ESP_LOGE(TAG, "could not create a mutex - remote schedules disabled.");
    return false;
  }

  mqtt_.addMessageHandler(
      [this](const std::string& topic, const std::string& payload) {
        onMessage(topic, payload);
      });

  // Deferred until the link is up; MqttClient replays it on every connect.
  return mqtt_.subscribe(topic_, kScheduleSubscribeQos);
}

void RemoteSchedule::onMessage(const std::string& topic,
                               const std::string& payload) {
  // Load-bearing: MqttClient hands every message to every handler, so this is
  // what stops us trying to parse a config document as a schedule bundle.
  if (topic != topic_) {
    return;
  }

  if (xSemaphoreTake(mutex_, portMAX_DELAY) != pdTRUE) {
    return;
  }
  // A bundle that arrives while an older one is still unprocessed simply
  // replaces it: only the newest schedule is of any interest.
  pendingPayload_ = payload;
  pending_        = true;
  xSemaphoreGive(mutex_);

  // Set after releasing the mutex, so the woken task never immediately blocks on
  // a lock we still hold. No parsing and no card write happen here - a bundle is
  // several kilobytes, and doing that work on the esp-mqtt event task would stall
  // keep-alives behind SPI IO.
  signal_.raise(UpdateSignal::kScheduleArrived);
}

bool RemoteSchedule::takePending(std::string& payloadOut) {
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

bool RemoteSchedule::poll() {
  std::string payload;
  if (!takePending(payload)) {
    return false;  // nothing new
  }

  // Not seeded from current_: a bundle replaces the previous one whole. Merging
  // would leave deleted rules in force, which is the one failure mode that looks
  // like the schedule working.
  ScheduleBundle incoming;
  if (!ScheduleCodec::decode(payload.data(), payload.size(), incoming)) {
    ESP_LOGW(TAG, "ignoring unusable schedule message on %s", topic_);
    return false;
  }

  // The revision is the whole comparison, unlike the settings document where the
  // values are compared as well. The server bumps it on every change and never
  // reuses one, so equal revisions mean an identical bundle - and re-writing
  // several kilobytes to the card on every retained replay, which happens on
  // every single wake, would be a great deal of pointless SPI traffic.
  if (current_.valid() && incoming.version() == current_.version()) {
    ESP_LOGD(TAG, "schedule v%u matches the one in force - nothing to do.",
             (unsigned)incoming.version());
    return true;
  }

  ESP_LOGI(TAG,
           "new schedule (v%u): %s, %u profile(s), %u rule(s), fallback slot %d%s",
           (unsigned)incoming.version(),
           incoming.enabled() ? "enabled" : "disabled",
           (unsigned)incoming.profiles().size(),
           (unsigned)incoming.rules().size(), incoming.fallbackSlot(),
           incoming.hasOverride() ? ", override in force" : "");

  // Adopt immediately; caching is best-effort. If the card write fails we still
  // honour the new schedule for this run and re-fetch it after the next reboot.
  current_ = incoming;
  store_.save(current_);
  return true;
}

bool RemoteSchedule::resyncIfDue(uint32_t intervalSeconds) {
  // Disabled, or the link is down - either way there is nothing useful to do.
  // The timer is deliberately NOT armed while offline, so the first resync after
  // a long outage happens promptly rather than one full interval later.
  if (intervalSeconds == 0 || !mqtt_.isConnected()) {
    return false;
  }

  const int64_t nowUs = esp_timer_get_time();
  if (nowUs < nextResyncUs_) {
    return false;
  }
  nextResyncUs_ = nowUs + static_cast<int64_t>(intervalSeconds) * 1000000LL;

  // A plain re-SUBSCRIBE, not an unsubscribe/subscribe pair: MQTT 3.1.1 requires
  // a repeat SUBSCRIBE on an identical filter to re-send matching retained
  // messages without interrupting the flow of publications [MQTT-3.8.4-3]. One
  // packet, and no window in which a live bundle could slip past.
  //
  // This is the device's own repair path, and it is the only one. The server
  // deliberately does not republish a bundle when it notices a device is behind:
  // it reconciles every thirty seconds, and a device that has not reported since
  // the last edit would draw a publish on every one of those passes.
  if (!mqtt_.subscribe(topic_, kScheduleSubscribeQos)) {
    ESP_LOGW(TAG, "schedule re-check could not subscribe to %s", topic_);
    return false;
  }
  ESP_LOGD(TAG, "asked the broker to re-send the retained schedule on %s",
           topic_);
  return true;
}
