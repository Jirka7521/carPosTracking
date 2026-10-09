#include "events/EventLog.h"

#include "esp_log.h"
#include "esp_timer.h"

static const char* TAG = "EventLog";

EventLog::EventLog(MqttClient& mqtt, FixQueue& queue, const char* topic,
                   uint32_t timeoutMs, std::size_t maxBurst, uint32_t retryMs,
                   uint32_t budgetMs)
    : mqtt_(mqtt),
      queue_(queue),
      topic_(topic),
      timeoutMs_(timeoutMs),
      maxBurst_(maxBurst > 0 ? maxBurst : 1),
      retryMs_(retryMs),
      budgetMs_(budgetMs),
      nextFlushUs_(0),
      pacedConnectCount_(0) {}

void EventLog::submit(const std::string& envelope) {
  // The common case, link up and nothing older waiting: straight out, without
  // a card write per event.
  if (queue_.isEmpty() && mqtt_.isConnected()) {
    if (publishBurst({envelope})) {
      return;
    }
    // The link is up but not answering. Keep the event, and do not let the
    // flush() below try again at once - it would only wait out the same timeout.
    nextFlushUs_       = esp_timer_get_time() + static_cast<int64_t>(retryMs_) * 1000LL;
    pacedConnectCount_ = mqtt_.connectCount();
  }

  if (queue_.enqueue(envelope)) {
    ESP_LOGI(TAG, "event kept on the card (%u waiting)",
             (unsigned)queue_.size());
    flush();  // older ones first; a no-op while disconnected or paced
    return;
  }

  // No card. Ordering no longer matters - there is nothing to be behind.
  if (mqtt_.isConnected() && publishBurst({envelope})) {
    return;
  }
  ESP_LOGW(TAG, "event dropped - no link and no card to keep it on");
}

void EventLog::flush() {
  if (queue_.isEmpty() || !mqtt_.isConnected()) {
    return;
  }
  const uint32_t connects = mqtt_.connectCount();
  const int64_t  startUs  = esp_timer_get_time();
  if (connects == pacedConnectCount_ && startUs < nextFlushUs_) {
    return;  // the last attempt failed, and the link has not been renewed since
  }
  pacedConnectCount_ = connects;

  const int64_t deadlineUs = startUs + static_cast<int64_t>(budgetMs_) * 1000LL;
  std::size_t   sent       = 0;
  std::vector<std::string> burst;

  while (!queue_.isEmpty()) {
    burst.clear();
    if (!queue_.peekBatch(maxBurst_, burst) || burst.empty()) {
      ESP_LOGW(TAG, "could not read the event queue");
      break;
    }
    if (!publishBurst(burst)) {
      break;
    }
    // Delivered. A failed pop re-sends the burst next time: one duplicate row
    // in a history list, which the API accepts rather than dedupes.
    if (!queue_.popFront(burst.size())) {
      ESP_LOGW(TAG, "could not pop %u delivered event(s)",
               (unsigned)burst.size());
      break;
    }
    sent += burst.size();

    if (esp_timer_get_time() >= deadlineUs) {
      // Out of time, not out of luck: the next call resumes at once.
      ESP_LOGI(TAG, "sent %u event(s) from the card, %u still waiting",
               (unsigned)sent, (unsigned)queue_.size());
      nextFlushUs_ = 0;
      return;
    }
  }

  if (queue_.isEmpty()) {
    ESP_LOGI(TAG, "sent %u event(s) from the card - backlog cleared",
             (unsigned)sent);
    nextFlushUs_ = 0;
    return;
  }

  // Stalled: a dead link, a silent broker or an unreadable card. Wait before
  // trying again, unless a reconnect comes first.
  nextFlushUs_ = esp_timer_get_time() + static_cast<int64_t>(retryMs_) * 1000LL;
}

bool EventLog::publishBurst(const std::vector<std::string>& envelopes) {
  // The same array shape as a position batch and every other status message,
  // so the API's envelope decoder takes a burst of events unchanged.
  std::size_t total = 2;
  for (const std::string& envelope : envelopes) {
    total += envelope.size() + 1;
  }
  std::string message;
  message.reserve(total);
  message.push_back('[');
  for (std::size_t i = 0; i < envelopes.size(); ++i) {
    if (i > 0) {
      message.push_back(',');
    }
    message.append(envelopes[i]);
  }
  message.push_back(']');

  if (!mqtt_.publishConfirmed(topic_, message, timeoutMs_)) {
    ESP_LOGW(TAG, "%u event(s) not confirmed by the broker",
             (unsigned)envelopes.size());
    return false;
  }
  return true;
}
