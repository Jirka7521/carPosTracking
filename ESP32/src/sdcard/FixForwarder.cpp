#include "sdcard/FixForwarder.h"

#include <cstdio>

#include "cJSON.h"
#include "esp_log.h"
#include "esp_timer.h"

static const char* TAG = "FixForwarder";

FixForwarder::FixForwarder(TelemetryPublisher& publisher, MqttClient& mqtt,
                           AckWatcher& ackWatcher, FixQueue& queue,
                           RetryQueue& retryQueue, const char* topic,
                           uint32_t ackTimeoutMs, uint32_t apiAckTimeoutMs,
                           std::size_t maxBurst, uint32_t flushRetryMs)
    : publisher_(publisher),
      mqtt_(mqtt),
      ackWatcher_(ackWatcher),
      queue_(queue),
      retryQueue_(retryQueue),
      topic_(topic),
      ackTimeoutMs_(ackTimeoutMs),
      apiAckTimeoutMs_(apiAckTimeoutMs),
      // A burst of at least one; guard against a mis-set config value.
      maxBurst_(maxBurst == 0 ? 1 : maxBurst),
      flushRetryMs_(flushRetryMs),
      nextFlushUs_(0),
      wasConnected_(false) {}

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

std::string FixForwarder::extractEnvelopeId(const std::string& envelope) {
  cJSON* root = cJSON_Parse(envelope.c_str());
  if (root == nullptr) {
    return std::string();
  }

  std::string  id;
  const cJSON* item = cJSON_GetObjectItemCaseSensitive(root, "id");
  if (cJSON_IsString(item)) {
    id.assign(item->valuestring);
  }
  cJSON_Delete(root);
  return id;
}

std::string FixForwarder::gnssTimeUtc(const GnssTime& time) {
  if (!time.valid) {
    return std::string();
  }

  char iso[32];
  std::snprintf(iso, sizeof(iso), "%04u-%02u-%02uT%02u:%02u:%02uZ", time.year,
                time.month, time.day, time.hour, time.minute, time.second);
  return std::string(iso);
}

std::size_t FixForwarder::leadingResolved(
    const std::vector<AckWatcher::AckResult>& results) {
  std::size_t count = 0;
  for (const AckWatcher::AckResult& result : results) {
    if (result.verdict == AckWatcher::AckVerdict::Unknown) {
      break;
    }
    count++;
  }
  return count;
}

bool FixForwarder::deliverBatch(
    const std::vector<std::string>& envelopes,
    std::vector<AckWatcher::AckResult>& resultsOut) {
  resultsOut.assign(envelopes.size(),
                    AckWatcher::AckResult{AckWatcher::AckVerdict::Unknown, ""});

  const std::string message = buildArrayMessage(envelopes);
  if (!mqtt_.publishConfirmed(topic_, message, ackTimeoutMs_)) {
    return false;  // never reached the broker; caller keeps everything
  }

  // Acks turned off (kAckEnabled false): the broker ack is all the confirmation
  // there is, so treat the burst as delivered exactly as the pre-ack firmware
  // did. Without this the wait below would time out on every fix and the queue
  // would grow forever.
  if (apiAckTimeoutMs_ == 0) {
    resultsOut.assign(envelopes.size(),
                      AckWatcher::AckResult{AckWatcher::AckVerdict::Stored, ""});
    return true;
  }

  // The broker has it. Now find out what the API did with it. Envelopes sealed
  // before the ack protocol existed carry no id and can never be resolved, so
  // they are not worth waiting for - they are treated as delivered on the broker
  // ack alone, exactly as the old firmware did.
  std::vector<std::string> ids;
  std::vector<std::size_t> idPositions;
  ids.reserve(envelopes.size());
  idPositions.reserve(envelopes.size());
  for (std::size_t i = 0; i < envelopes.size(); ++i) {
    const std::string id = extractEnvelopeId(envelopes[i]);
    if (id.empty()) {
      resultsOut[i] = AckWatcher::AckResult{AckWatcher::AckVerdict::Stored, ""};
    } else {
      ids.push_back(id);
      idPositions.push_back(i);
    }
  }

  if (ids.empty()) {
    return true;
  }

  std::vector<AckWatcher::AckResult> acked;
  ackWatcher_.waitForAck(ids, acked, apiAckTimeoutMs_);
  for (std::size_t i = 0; i < idPositions.size() && i < acked.size(); ++i) {
    resultsOut[idPositions[i]] = acked[i];
  }
  return true;
}

bool FixForwarder::drainQueue(const std::string& nowUtc) {
  // Repeatedly ship the oldest slice of the queue. We stop the moment a burst is
  // not fully resolved, so nothing is deleted before the API has confirmed it.
  while (!queue_.isEmpty()) {
    std::vector<std::string> batch;
    if (!queue_.peekBatch(maxBurst_, batch) || batch.empty()) {
      ESP_LOGE(TAG, "drain: could not read queue - will retry later");
      return false;
    }

    std::vector<AckWatcher::AckResult> results;
    if (!deliverBatch(batch, results)) {
      ESP_LOGW(TAG, "drain: burst of %u not acked by the broker - %u still queued",
               (unsigned)batch.size(), (unsigned)queue_.size());
      return false;
    }

    // Only an unbroken run from the head can leave a FIFO. In practice the API
    // resolves a whole message at once, so this is normally the entire batch.
    std::size_t resolved = leadingResolved(results);
    if (resolved == 0) {
      ESP_LOGW(TAG, "drain: no API verdict for this burst - %u still queued",
               (unsigned)queue_.size());
      return false;
    }

    // Park the rejected ones before dropping them from the live queue, so a
    // failure here never loses the fix. add() refuses when there is no GNSS
    // clock to schedule against - which a fixless flush can well hit - so its
    // result decides how far we may pop: everything ahead of the unparkable fix
    // is still safely delivered, the fix itself stays queued for a cycle that
    // does have a clock.
    for (std::size_t i = 0; i < resolved; ++i) {
      if (results[i].verdict == AckWatcher::AckVerdict::Rejected &&
          !retryQueue_.add(batch[i], nowUtc, results[i].reason.c_str())) {
        ESP_LOGW(TAG,
                 "drain: a rejected fix could not be parked for retry - it "
                 "stays in the live queue");
        resolved = i;
        break;
      }
    }
    if (resolved == 0) {
      return false;  // nothing in this burst may be removed yet
    }

    if (!queue_.popFront(resolved)) {
      ESP_LOGE(TAG, "drain: failed to pop delivered burst from queue");
      return false;
    }
    ESP_LOGI(TAG, "drain: %u of %u confirmed (%u remaining)", (unsigned)resolved,
             (unsigned)batch.size(), (unsigned)queue_.size());

    if (resolved < batch.size()) {
      return false;  // a gap - leave the rest for the next cycle
    }
  }
  return true;
}

void FixForwarder::drainRetries(const std::string& nowUtc) {
  if (retryQueue_.isEmpty() || !mqtt_.isConnected()) {
    return;
  }

  std::vector<RetryQueue::Entry> due;
  if (!retryQueue_.takeDue(nowUtc, maxBurst_, due) || due.empty()) {
    return;
  }

  // These entries are off the card now, so every one of them must end up either
  // confirmed stored or written back - there is no third option that does not
  // lose data.
  std::vector<std::string> envelopes;
  envelopes.reserve(due.size());
  for (const RetryQueue::Entry& entry : due) {
    envelopes.push_back(entry.envelope);
  }

  std::vector<AckWatcher::AckResult> results;
  const bool reachedBroker = deliverBatch(envelopes, results);

  std::size_t stored = 0;
  for (std::size_t i = 0; i < due.size(); ++i) {
    const bool accepted =
        reachedBroker && results[i].verdict == AckWatcher::AckVerdict::Stored;
    if (accepted) {
      stored++;
      continue;
    }

    const char* reason =
        reachedBroker && results[i].verdict == AckWatcher::AckVerdict::Rejected
            ? results[i].reason.c_str()
            : "NoVerdict";
    retryQueue_.add(due[i].envelope, nowUtc, reason, due[i].firstUtc,
                    due[i].attempts);
  }

  if (stored > 0) {
    ESP_LOGI(TAG, "retry: %u of %u previously rejected fix(es) accepted.",
             (unsigned)stored, (unsigned)due.size());
  }
}

void FixForwarder::process(const TelemetrySample& sample) {
  // The GNSS UTC time is the only wall clock we have, and it is what the retry
  // schedule is measured in. Empty when this fix carries no valid time.
  const std::string nowUtc = gnssTimeUtc(sample.gnss.time);

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
  //    the whole thing in one or more bursts. Anything unconfirmed stays queued.
  if (!queue_.isEmpty()) {
    if (!queue_.enqueue(envelope)) {
      ESP_LOGE(TAG, "SD queue write failed - trying to drain existing backlog");
    }
    drainQueue(nowUtc);
    drainRetries(nowUtc);
    return;
  }

  // 4. Online, nothing backlogged: publish this single fix as an array-of-one
  //    and see it all the way through to the API's verdict. Only a confirmed
  //    store lets it go, so the healthy path still never writes to the card.
  std::vector<AckWatcher::AckResult> results;
  if (deliverBatch({envelope}, results) && !results.empty()) {
    if (results[0].verdict == AckWatcher::AckVerdict::Stored) {
      ESP_LOGI(TAG, "fix stored by the API.");
      drainRetries(nowUtc);
      return;
    }

    if (results[0].verdict == AckWatcher::AckVerdict::Rejected) {
      ESP_LOGW(TAG, "API rejected this fix (%s)", results[0].reason.c_str());
      if (retryQueue_.add(envelope, nowUtc, results[0].reason.c_str())) {
        drainRetries(nowUtc);
        return;
      }
      // No clock to schedule a retry with; fall through and keep it in the live
      // queue rather than dropping it.
    }
  }

  ESP_LOGW(TAG, "no confirmation from the API - queuing fix to SD");
  if (!queue_.enqueue(envelope)) {
    ESP_LOGE(TAG, "SD queue write failed - fix lost");
  }
}

void FixForwarder::flushBacklog(const GnssFix& fix) {
  // A live MQTT connection implies the WiFi link is up, and it is the only
  // signal that says the broker is actually reachable - so it is the same gate
  // the live path uses.
  const bool connected = mqtt_.isConnected();

  // Coming back from a disconnect is precisely the event worth retrying on, so
  // it cancels any pause left over from an attempt made on a dying link.
  if (connected && !wasConnected_) {
    nextFlushUs_ = 0;
  }
  wasConnected_ = connected;

  if (!connected) {
    return;
  }

  // Both sizes are cached in RAM, so the healthy path - nothing backlogged -
  // costs two comparisons and never touches the card, which is what makes this
  // safe to call on every GNSS poll.
  if (queue_.isEmpty() && retryQueue_.isEmpty()) {
    return;
  }

  const int64_t nowUs = esp_timer_get_time();
  if (nowUs < nextFlushUs_) {
    return;  // still pausing after an attempt that left work behind
  }

  // No position needed: only the fix's UTC time is read, and the modem knows
  // that long before it can compute a position. Empty is tolerable - see the
  // header - so we say so in the log rather than skipping the flush.
  const std::string nowUtc = gnssTimeUtc(fix.time);
  ESP_LOGI(TAG, "link up - flushing backlog (%u queued, %u awaiting retry)%s.",
           (unsigned)queue_.size(), (unsigned)retryQueue_.size(),
           nowUtc.empty() ? " [no GNSS clock this cycle]" : "");

  const bool drained = drainQueue(nowUtc);
  drainRetries(nowUtc);

  // Only a card with nothing left on it earns an immediate next attempt.
  // Anything else waits, so neither a broker that never acks nor a retry entry
  // that is simply not due yet can be hammered on every poll.
  const bool nothingLeft = drained && queue_.isEmpty() && retryQueue_.isEmpty();
  nextFlushUs_ =
      nothingLeft ? 0 : nowUs + static_cast<int64_t>(flushRetryMs_) * 1000;
}
