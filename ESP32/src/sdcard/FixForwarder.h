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
//
//  Draining does NOT need a position fix. process() is only called for a fix we
//  actually have, but a car parked in a garage may never get one - and the
//  backlog collected on the way there must still go out the moment the broker is
//  reachable. flushBacklog() is that door: it drains what is already on the card
//  on any cycle, lock or no lock. See its comment for the clock caveat.
//
//  Pacing rule (why a fixless device drains its WHOLE backlog, not one burst):
//  a flush is paced on what the attempt ACHIEVED, not on whether it finished.
//  Any attempt that moved data off the card - or that merely ran out of its time
//  budget - is repeated on the very next poll, so a long backlog leaves in
//  back-to-back bursts. Only an attempt that achieved nothing at all waits out
//  `flushRetryMs`. See flushBacklog() and DrainStop below.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "gnss/GnssData.h"
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
  //   flushRetryMs : pause after a flushBacklog() that achieved nothing
  //   flushBudgetMs: how long one flushBacklog() may keep draining before it
  //                  hands the CPU back (it resumes on the next call)
  FixForwarder(TelemetryPublisher& publisher, MqttClient& mqtt,
               AckWatcher& ackWatcher, FixQueue& queue, RetryQueue& retryQueue,
               const char* topic, uint32_t ackTimeoutMs,
               uint32_t apiAckTimeoutMs, std::size_t maxBurst,
               uint32_t flushRetryMs, uint32_t flushBudgetMs);

  // Handle one telemetry sample end to end: publish it (with any backlog) or
  // store it, then re-offer any rejected fixes that have come due.
  void process(const TelemetrySample& sample);

  // Drain whatever is already on the card, with no new sample to publish. This
  // is what makes a backlog independent of the position lock: the caller may
  // (and should) call it on every cycle and while still waiting for a fix, so
  // queued fixes leave as soon as the link is back rather than waiting for the
  // next lock - which, parked indoors, may never come.
  //
  // `fix` is used ONLY for its GNSS UTC time, the device's only wall clock. The
  // modem reports that time as soon as it decodes any satellite, well before it
  // can compute a position, so a fixless poll usually still carries one. When it
  // does not, the live queue still drains - only the retry file, which is
  // scheduled in wall-clock time, sits the cycle out.
  //
  // Cheap enough to call in a poll loop: with nothing queued, or the link down,
  // it returns on the in-memory counters without touching the card.
  //
  // One call drains for at most `flushBudgetMs` and is then repeated by the next
  // call, so however deep the backlog is it keeps moving without ever monopolising
  // the caller's poll loop. It waits `flushRetryMs` only after an attempt that
  // achieved nothing - a dead link, an unreadable card, or an API that has gone
  // quiet - and even then an MQTT reconnect cancels the wait, that reconnect
  // being precisely the event worth retrying on immediately.
  void flushBacklog(const GnssFix& fix);

 private:
  // Why drainQueue() came back. The caller pauses on some of these and retries
  // at once on others, so the distinction has to survive the return - "the queue
  // is not empty" alone cannot tell a stalled attempt from a working one.
  enum class DrainStop {
    Empty,      // queue fully drained - nothing more to do
    Budget,     // time budget spent mid-drain - resume on the next call
    NoVerdict,  // burst reached the broker, the API has not answered (yet)
    Transport,  // burst never even reached the broker - the link is unwell
    CardError   // could not read from / pop the queue file
  };
  // Publish `envelopes` as one burst and collect the API's verdict for each.
  //
  // Fills `resultsOut` with one entry per envelope, in the same order. Returns
  // false when the burst never even reached the broker, in which case the
  // results are all Unknown and the caller must leave everything where it is.
  bool deliverBatch(const std::vector<std::string>& envelopes,
                    std::vector<AckWatcher::AckResult>& resultsOut);

  // Send queued envelopes in bursts of up to maxBurst_ until the queue is empty,
  // a burst is not fully resolved, or the time budget runs out. Rejected fixes
  // are moved to the retry queue as they are encountered.
  //   deadlineUs: esp_timer stamp past which no NEW burst is started; 0 means no
  //               budget. Checked between bursts only - one already in flight is
  //               always seen through - so the true worst case is the budget
  //               plus one burst's worth of ack waiting.
  //   poppedOut : incremented by the number of envelopes this call actually
  //               removed from the card. That count, not the return value, is
  //               what tells the caller whether the attempt made progress.
  DrainStop drainQueue(const std::string& nowUtc, int64_t deadlineUs,
                       std::size_t& poppedOut);

  // Re-offer rejected fixes whose next-attempt time has passed. Anything still
  // unresolved (or rejected again) goes back into the retry queue - takeDue()
  // has already removed it from the card, so putting it back is what keeps it
  // from being lost. `storedOut` is incremented by the number of entries the API
  // finally accepted, so retry progress counts toward pacing too.
  void drainRetries(const std::string& nowUtc, std::size_t& storedOut);

  // Whether a retry drain is worth attempting after a live-queue drain that
  // stopped for the given reason. False when the link or the card has just
  // proved unusable: the retry drain would fail identically, and it pays for the
  // attempt by rewriting the whole retry file.
  static bool drainWorthRetrying(DrainStop stop);

  // Decide when the next drain attempt may run, from what this one achieved.
  // Shared by both callers of drainQueue() so the backlog is paced by one rule
  // rather than two - `nowUs` is the timestamp the attempt was judged against.
  void applyDrainPacing(DrainStop stop, std::size_t moved, int64_t nowUs);

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

  // Render a GNSS UTC time as ISO-8601, or an empty string when it is not valid.
  // That empty string is what tells RetryQueue it has no clock and must not
  // schedule anything.
  static std::string gnssTimeUtc(const GnssTime& time);

  TelemetryPublisher& publisher_;
  MqttClient&         mqtt_;
  AckWatcher&         ackWatcher_;
  FixQueue&           queue_;
  RetryQueue&         retryQueue_;
  const char*         topic_;
  uint32_t            ackTimeoutMs_;
  uint32_t            apiAckTimeoutMs_;
  std::size_t         maxBurst_;
  uint32_t            flushRetryMs_;
  uint32_t            flushBudgetMs_;

  // flushBacklog() pacing. `nextFlushUs_` is an esp_timer stamp (monotonic since
  // boot, and reset by the deep-sleep reboot - a fresh wake always flushes at
  // once, which is what we want). `wasConnected_` exists only to spot the
  // disconnected -> connected edge and cancel the pause on it.
  int64_t nextFlushUs_;
  bool    wasConnected_;

  // Consecutive flushes that reached the broker but got no verdict back and
  // moved nothing. A handful of prompt re-attempts catches an ack that merely
  // arrived late; past that the full pause takes over, so an ack path that is
  // genuinely broken cannot turn into a continuous re-publish loop. Reset by any
  // attempt that makes progress or fails for a different reason.
  uint8_t noVerdictRetries_;
};
