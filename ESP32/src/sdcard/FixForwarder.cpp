#include "sdcard/FixForwarder.h"

#include "esp_log.h"

static const char* TAG = "FixForwarder";

FixForwarder::FixForwarder(TelemetryPublisher& publisher, MqttClient& mqtt,
                           FixQueue& queue, const char* topic,
                           uint32_t ackTimeoutMs, std::size_t maxBurst)
    : publisher_(publisher),
      mqtt_(mqtt),
      queue_(queue),
      topic_(topic),
      ackTimeoutMs_(ackTimeoutMs),
      // A burst of at least one; guard against a mis-set config value.
      maxBurst_(maxBurst == 0 ? 1 : maxBurst) {}

std::string FixForwarder::buildArrayMessage(
    const std::vector<std::string>& envs) {
  // Each envelope is already a compact JSON object, so the array is just the
  // envelopes joined by commas inside brackets - no re-parsing needed.
  std::size_t total = 2;  // the surrounding [ ]
  for (const std::string& e : envs) {
    total += e.size() + 1;  // envelope + its trailing comma/bracket
  }

  std::string msg;
  msg.reserve(total);
  msg.push_back('[');
  for (std::size_t i = 0; i < envs.size(); ++i) {
    if (i != 0) {
      msg.push_back(',');
    }
    msg.append(envs[i]);
  }
  msg.push_back(']');
  return msg;
}

bool FixForwarder::drainQueue() {
  // Repeatedly ship the oldest slice of the queue. We stop the moment a burst is
  // not acked so nothing is ever deleted before the broker has it.
  while (!queue_.isEmpty()) {
    std::vector<std::string> batch;
    if (!queue_.peekBatch(maxBurst_, batch) || batch.empty()) {
      ESP_LOGE(TAG, "drain: could not read queue - will retry later");
      return false;
    }

    const std::string message = buildArrayMessage(batch);
    if (!mqtt_.publishConfirmed(topic_, message, ackTimeoutMs_)) {
      ESP_LOGW(TAG, "drain: burst of %u not acked - %u still queued",
               (unsigned)batch.size(), (unsigned)queue_.size());
      return false;
    }

    // Delivered: drop exactly what we just sent and continue with the rest.
    if (!queue_.popFront(batch.size())) {
      ESP_LOGE(TAG, "drain: failed to pop delivered burst from queue");
      return false;
    }
    ESP_LOGI(TAG, "drain: delivered burst of %u (%u remaining)",
             (unsigned)batch.size(), (unsigned)queue_.size());
  }
  return true;
}

void FixForwarder::process(const TelemetrySample& sample) {
  // 1. Seal the sample into the exact envelope that would be transmitted - the
  //    same bytes are used whether we send now or store for later.
  std::string envelope;
  if (!publisher_.sealSample(sample, envelope)) {
    ESP_LOGE(TAG, "could not seal sample - dropping this reading");
    return;
  }

  // 2. Offline: persist and stop. The fix is safe on the card until link-up.
  if (!mqtt_.isConnected()) {
    if (queue_.enqueue(envelope)) {
      ESP_LOGI(TAG, "offline - fix queued to SD (%u pending)",
               (unsigned)queue_.size());
    } else {
      ESP_LOGE(TAG, "offline and SD queue write failed - fix lost");
    }
    return;
  }

  // 3. Online with a backlog: make the new fix part of the backlog, then drain
  //    the whole thing in one or more bursts. Anything not acked stays queued.
  if (!queue_.isEmpty()) {
    if (!queue_.enqueue(envelope)) {
      ESP_LOGE(TAG, "SD queue write failed - trying to drain existing backlog");
    }
    drainQueue();
    return;
  }

  // 4. Online, nothing backlogged: publish this single fix as an array-of-one
  //    and confirm it. Only a failed delivery falls back to the SD card, so the
  //    healthy path never writes to the card at all.
  const std::string message = buildArrayMessage({envelope});
  if (mqtt_.publishConfirmed(topic_, message, ackTimeoutMs_)) {
    ESP_LOGI(TAG, "fix delivered (%u bytes)", (unsigned)message.size());
    return;
  }

  ESP_LOGW(TAG, "delivery not acked - queuing fix to SD");
  if (!queue_.enqueue(envelope)) {
    ESP_LOGE(TAG, "SD queue write failed - fix lost");
  }
}
