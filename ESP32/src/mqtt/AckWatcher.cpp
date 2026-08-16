#include "mqtt/AckWatcher.h"

#include <algorithm>
#include <cstring>

#include "cJSON.h"
#include "esp_log.h"
#include "freertos/task.h"

static const char* TAG = "AckWatcher";

namespace {

  // How often waitForAck() re-checks while waiting.
  constexpr uint32_t kWaitPollStepMs = 20;

  // QoS 1 for acks: the broker keeps trying until we have it, and a duplicate
  // delivery is harmless because verdicts are keyed by envelope id - applying
  // the same one twice changes nothing.
  constexpr int kAckSubscribeQos = 1;

  // Ceiling on uncollected verdicts. Acks are normally consumed moments after
  // they arrive, so a map this large means something is wrong (a burst we gave
  // up on, or acks for envelopes from an earlier boot). Dropping them keeps a
  // stuck device from slowly eating its heap.
  constexpr std::size_t kMaxResolvedEntries = 256;

  // Cap on queued ciphertexts, for the same reason - the event task must never
  // be able to grow this without bound if the application task stops polling.
  constexpr std::size_t kMaxPendingPayloads = 8;

}  // namespace

AckWatcher::AckWatcher(MqttClient& mqtt, AckCrypto& crypto, const char* topic,
                       const char* deviceId)
    : mqtt_(mqtt),
      crypto_(crypto),
      topic_(topic),
      deviceId_(deviceId),
      mutex_(xSemaphoreCreateMutex()) {}

AckWatcher::~AckWatcher() {
  if (mutex_ != nullptr) {
    vSemaphoreDelete(mutex_);
  }
}

bool AckWatcher::begin() {
  if (mutex_ == nullptr) {
    ESP_LOGE(TAG, "could not create mutex - delivery acks disabled.");
    return false;
  }

  mqtt_.addMessageHandler(
      [this](const std::string& topic, const std::string& payload) {
        onMessage(topic, payload);
      });

  // Deferred until the link is up; MqttClient replays it on every connect.
  return mqtt_.subscribe(topic_, kAckSubscribeQos);
}

void AckWatcher::onMessage(const std::string& topic,
                           const std::string& payload) {
  // MqttClient hands every message to every handler, so this filter is what
  // keeps the config document out of the ack parser.
  if (topic != topic_) {
    return;
  }

  if (xSemaphoreTake(mutex_, portMAX_DELAY) != pdTRUE) {
    return;
  }
  // Unlike a config message, acks must NOT replace one another: two bursts can
  // be acked separately and both verdicts matter, so they queue up.
  if (pendingPayloads_.size() < kMaxPendingPayloads) {
    pendingPayloads_.push_back(payload);
  }
  xSemaphoreGive(mutex_);
}

bool AckWatcher::takePending(std::vector<std::string>& payloadsOut) {
  if (mutex_ == nullptr) {
    return false;
  }
  if (xSemaphoreTake(mutex_, portMAX_DELAY) != pdTRUE) {
    return false;
  }
  const bool had = !pendingPayloads_.empty();
  if (had) {
    payloadsOut.swap(pendingPayloads_);
    pendingPayloads_.clear();
  }
  xSemaphoreGive(mutex_);
  return had;
}

bool AckWatcher::applyAck(const std::string& envelopeJson) {
  // 1. Open it. A failure here is the normal outcome for a forged or misaddressed
  //    ack - AckCrypto has already logged why.
  std::string plaintext;
  if (!crypto_.decrypt(envelopeJson, plaintext)) {
    return false;
  }

  cJSON* root = cJSON_Parse(plaintext.c_str());
  if (root == nullptr) {
    ESP_LOGW(TAG, "ack plaintext is not valid JSON");
    return false;
  }

  bool ok = false;
  do {
    // 2. It must be addressed to us. The ack is authenticated, so this is not a
    //    security check - it catches a broker or ACL misconfiguration feeding us
    //    another device's topic, which would otherwise clear the wrong fixes.
    const cJSON* device = cJSON_GetObjectItemCaseSensitive(root, "device");
    if (!cJSON_IsString(device) || strcmp(device->valuestring, deviceId_) != 0) {
      ESP_LOGW(TAG, "ignoring an ack addressed to another device");
      break;
    }

    // 3. Stored: the fix is in the positions table, freshly inserted or already
    //    present. Either way it is safe to drop from the card.
    const cJSON* stored = cJSON_GetObjectItemCaseSensitive(root, "stored");
    if (cJSON_IsArray(stored)) {
      const cJSON* item = nullptr;
      cJSON_ArrayForEach(item, stored) {
        if (cJSON_IsString(item)) {
          resolved_[item->valuestring] = AckResult{AckVerdict::Stored, ""};
        }
      }
    }

    // 4. Rejected: the API refused it and will keep refusing it in the same
    //    shape. The reason is carried through so the retry queue can log it.
    const cJSON* rejected = cJSON_GetObjectItemCaseSensitive(root, "rejected");
    if (cJSON_IsArray(rejected)) {
      const cJSON* item = nullptr;
      cJSON_ArrayForEach(item, rejected) {
        const cJSON* id     = cJSON_GetObjectItemCaseSensitive(item, "id");
        const cJSON* reason = cJSON_GetObjectItemCaseSensitive(item, "reason");
        if (cJSON_IsString(id)) {
          resolved_[id->valuestring] = AckResult{
              AckVerdict::Rejected,
              cJSON_IsString(reason) ? reason->valuestring : "Unspecified"};
        }
      }
    }

    ok = true;
  } while (false);

  cJSON_Delete(root);

  // Bound the map. Verdicts are normally collected within seconds; a backlog
  // this deep means nobody is asking for them.
  if (resolved_.size() > kMaxResolvedEntries) {
    ESP_LOGW(TAG, "dropping %u uncollected ack verdict(s)",
             (unsigned)resolved_.size());
    resolved_.clear();
  }
  return ok;
}

bool AckWatcher::poll() {
  std::vector<std::string> payloads;
  if (!takePending(payloads)) {
    return false;  // nothing new
  }

  bool applied = false;
  for (const std::string& payload : payloads) {
    if (applyAck(payload)) {
      applied = true;
    }
  }
  return applied;
}

bool AckWatcher::waitForAck(const std::vector<std::string>& ids,
                            std::vector<AckResult>& resultsOut,
                            uint32_t timeoutMs) {
  resultsOut.assign(ids.size(), AckResult{AckVerdict::Unknown, ""});
  if (ids.empty()) {
    return true;
  }

  uint32_t waited = 0;
  while (true) {
    poll();

    // Resolved when every id we asked about has a verdict. Checked after each
    // poll rather than once at the end so a prompt ack returns promptly - the
    // main loop is blocked here, and with sleep_between on the whole waking
    // window is only a few seconds long.
    bool allResolved = true;
    for (std::size_t i = 0; i < ids.size(); ++i) {
      if (resultsOut[i].verdict != AckVerdict::Unknown) {
        continue;  // already collected on an earlier iteration
      }
      const std::map<std::string, AckResult>::const_iterator found =
          resolved_.find(ids[i]);
      if (found != resolved_.end()) {
        resultsOut[i] = found->second;
      } else {
        allResolved = false;
      }
    }

    if (allResolved) {
      break;
    }
    if (waited >= timeoutMs) {
      ESP_LOGW(TAG, "no delivery ack for %u of %u envelope(s) within %ums",
               (unsigned)std::count_if(
                   resultsOut.begin(), resultsOut.end(),
                   [](const AckResult& r) {
                     return r.verdict == AckVerdict::Unknown;
                   }),
               (unsigned)ids.size(), (unsigned)timeoutMs);
      break;
    }

    vTaskDelay(pdMS_TO_TICKS(kWaitPollStepMs));
    waited += kWaitPollStepMs;
  }

  // Consume what we collected, so a later burst cannot be cleared by a stale
  // verdict for a reused id.
  bool allResolved = true;
  for (std::size_t i = 0; i < ids.size(); ++i) {
    if (resultsOut[i].verdict == AckVerdict::Unknown) {
      allResolved = false;
    } else {
      resolved_.erase(ids[i]);
    }
  }
  return allResolved;
}
