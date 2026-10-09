#pragma once

// =============================================================================
//  EventLog  -  Deliver the device's history to the API, even from out of range.
// -----------------------------------------------------------------------------
//  Responsibility (single!): get sealed status events - offline, wake, motion -
//  to the status topic in the order they happened, whether or not the broker
//  is reachable at the moment they happen. It is the status-topic sibling of
//  FixForwarder, and deliberately a much smaller one.
//
//      submit()  link up, nothing waiting  -> publish now, confirmed (QoS 2)
//                otherwise                 -> append to the SD queue
//      flush()   link up, queue not empty  -> drain it in bursts, oldest first
//
//  Why it exists: MQTT runs over WiFi, and the moments worth recording - the
//  car starting to move, parking somewhere, going to sleep there - are exactly
//  the ones that happen out of range. A status message used to be dropped when
//  it could not go out at once; that left the history blank for every trip.
//
//  Storage is a FixQueue of its own, in its own file. FixQueue holds opaque
//  sealed envelopes one per line and knows nothing about fixes beyond the word
//  in its log lines, so the head-offset, cap and compaction logic is reused
//  rather than written twice. Only ciphertext touches the card, as for fixes.
//
//  ONE ack, not two. A fix waits for the API's own verdict before it leaves the
//  card (see FixForwarder); an event leaves on the broker's PUBCOMP alone. The
//  API holds a persistent QoS-2 session on the status topic, so the broker
//  keeps the message for it through an API outage, and a status message the
//  API rejects is rejected for good (an unknown word, a bad device id) -
//  retrying it would change nothing. The status path has no ack topic either.
//
//  Ordering is the point, so nothing jumps the queue: a new event goes straight
//  out only when the card holds nothing older.
//
//  Single-threaded: everything runs on the main task, for the same reasons as
//  PresenceReporter (publishConfirmed() and PayloadCrypto both demand it).
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "mqtt/MqttClient.h"
#include "sdcard/FixQueue.h"

class EventLog {
 public:
  // Borrows its collaborators and `topic` (all must outlive this object).
  //   mqtt      : confirmed (QoS-2) transport
  //   queue     : the SD-backed store of undelivered events (begun by main;
  //               an unmounted card simply refuses every append)
  //   topic     : the status topic, e.g. "devices/GNSS01/status"
  //   timeoutMs : how long one confirmed publish may wait for the broker
  //   maxBurst  : most envelopes per burst message (RAM bound - see Config.h)
  //   retryMs   : pause after a flush that failed, unless the link reconnects
  //   budgetMs  : how long one flush() may keep draining before it returns
  EventLog(MqttClient& mqtt, FixQueue& queue, const char* topic,
           uint32_t timeoutMs, std::size_t maxBurst, uint32_t retryMs,
           uint32_t budgetMs);

  // Deliver one sealed envelope, or keep it on the card behind anything older.
  // With no card and no link it is dropped (and logged) - the one way an event
  // is lost, and the same fate every status message had before this class.
  void submit(const std::string& envelope);

  // Drain the card while the link is up. Cheap when there is nothing to do: it
  // returns on in-memory counters without touching the card. Paced - after a
  // failed attempt it waits `retryMs` unless the link reconnects in between.
  void flush();

 private:
  // Publish `envelopes` as one JSON array and wait for the broker's ack.
  bool publishBurst(const std::vector<std::string>& envelopes);

  MqttClient& mqtt_;
  FixQueue&   queue_;
  const char* topic_;
  uint32_t    timeoutMs_;
  std::size_t maxBurst_;
  uint32_t    retryMs_;
  uint32_t    budgetMs_;

  // flush() pacing: no new attempt before `nextFlushUs_` (esp_timer clock)
  // unless MqttClient::connectCount() has moved past `pacedConnectCount_` -
  // a fresh connection is exactly the moment worth retrying on.
  int64_t  nextFlushUs_;
  uint32_t pacedConnectCount_;
};
