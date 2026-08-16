#pragma once

// =============================================================================
//  FixForwarder  -  Deliver a fix now, or persist it for later; drain on link-up.
// -----------------------------------------------------------------------------
//  Responsibility (single!): decide, for each fix, whether it can be delivered
//  to the API right now or must be stored on the SD card, and - whenever the
//  link is back and a backlog exists - drain that backlog. It is the glue that
//  ties TelemetryPublisher (seal a fix), MqttClient (confirmed publish),
//  AckWatcher (did the API store it?), FixQueue (persistent store) and
//  RetryQueue (rejected fixes) together, so main.cpp stays a thin wiring layer.
//
//  Wire format: every message is a JSON *array* of envelopes - a single fix is
//  just an array of one, a drained backlog is an array of many. The API
//  therefore always parses the same shape.
//
//  TWO acks, and why both are needed:
//    * The BROKER ack (MqttClient::publishConfirmed, QoS 2) says Mosquitto has
//      the message. That is all it says.
//    * The API ack (AckWatcher) says the fix reached the positions table.
//      Without it, a message the API rejects - or never receives because it is
//      down - still gets a broker ack, and the fix used to be deleted from the
//      card regardless. That silent data loss is what this class now prevents.
//  A fix leaves the card only on the second ack.
//
//  Delivery rule:
//    * link down                -> the sealed fix is appended to the SD queue.
//    * link up, queue empty     -> publish [fix], wait for the broker ack, then
//                                  for the API's verdict. Only a confirmed
//                                  "stored" drops it; anything else queues it.
//    * link up, queue non-empty -> queue the fix too, then drain everything as
//                                  one-or-more bursts, deleting from the card
//                                  only what the API confirmed it stored.
//    * rejected by the API      -> moved to the RetryQueue and re-offered on a
//                                  schedule, because several reject reasons are
//                                  server-side and clear on their own.
//    * no verdict at all        -> left exactly where it is and retried next
//                                  cycle. Re-sending is safe: the API dedupes on
//                                  (device, fix time), so a lost ack costs one
//                                  duplicate delivery, never a duplicate row.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "mqtt/AckWatcher.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "mqtt/TelemetrySample.h"
#include "sdcard/FixQueue.h"
#include "sdcard/RetryQueue.h"

class FixForwarder {
 public:
  // Borrows all collaborators and `topic` (all must outlive this object).
  //   publisher    : seals a fix into its encrypted envelope
  //   mqtt         : confirmed (QoS-2) transport
  //   ackWatcher   : the API's per-envelope verdicts
  //   queue        : persistent SD-backed store of undelivered envelopes
  //   retryQueue   : scheduled store of envelopes the API rejected
  //   topic        : topic every message is published to
  //   ackTimeoutMs : how long to wait for the broker's delivery ack
  //   apiAckTimeoutMs : how long to then wait for the API's verdict
  //   maxBurst     : max envelopes per burst message (RAM/MQTT safety bound)
  FixForwarder(TelemetryPublisher& publisher, MqttClient& mqtt,
               AckWatcher& ackWatcher, FixQueue& queue, RetryQueue& retryQueue,
               const char* topic, uint32_t ackTimeoutMs,
               uint32_t apiAckTimeoutMs, std::size_t maxBurst);

  // Handle one telemetry sample end to end: publish it (with any backlog) or
  // store it, then re-offer any rejected fixes that have come due.
  void process(const TelemetrySample& sample);

 private:
  // Publish `envelopes` as one burst and collect the API's verdict for each.
  //
  // Fills `resultsOut` with one entry per envelope, in the same order. Returns
  // false when the burst never even reached the broker, in which case the
  // results are all Unknown and the caller must leave everything where it is.
  bool deliverBatch(const std::vector<std::string>& envelopes,
                    std::vector<AckWatcher::AckResult>& resultsOut);

  // Send queued envelopes in bursts of up to maxBurst_ until the queue is empty
  // or a burst is not fully resolved. Rejected fixes are moved to the retry
  // queue as they are encountered. Returns true if the queue was fully drained.
  bool drainQueue(const std::string& nowUtc);

  // Re-offer rejected fixes whose next-attempt time has passed. Anything still
  // unresolved (or rejected again) goes back into the retry queue - takeDue()
  // has already removed it from the card, so putting it back is what keeps it
  // from being lost.
  void drainRetries(const std::string& nowUtc);

  // Number of leading entries in `results` that the API resolved (stored or
  // rejected). The queue is a FIFO popped from the front, so only an unbroken
  // run from the head can be removed - a gap means an earlier envelope is still
  // unaccounted for and everything behind it must wait.
  static std::size_t leadingResolved(
      const std::vector<AckWatcher::AckResult>& results);

  // Wrap a list of envelope strings into one JSON array message.
  static std::string buildArrayMessage(const std::vector<std::string>& envs);

  // Pull the cleartext correlation id out of a sealed envelope. Returns an empty
  // string when there is none, which is how an envelope sealed by pre-ack
  // firmware (still sitting in the queue after an upgrade) is recognised.
  static std::string extractEnvelopeId(const std::string& envelope);

  // Render the sample's GNSS UTC time as ISO-8601, or an empty string when the
  // fix carries no valid time. That empty string is what tells RetryQueue it has
  // no clock and must not schedule anything.
  static std::string sampleTimeUtc(const TelemetrySample& sample);

  TelemetryPublisher& publisher_;
  MqttClient&         mqtt_;
  AckWatcher&         ackWatcher_;
  FixQueue&           queue_;
  RetryQueue&         retryQueue_;
  const char*         topic_;
  uint32_t            ackTimeoutMs_;
  uint32_t            apiAckTimeoutMs_;
  std::size_t         maxBurst_;
};
