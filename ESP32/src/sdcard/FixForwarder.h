#pragma once

// =============================================================================
//  FixForwarder  -  Deliver a fix now, or persist it for later; drain on link-up.
// -----------------------------------------------------------------------------
//  Responsibility (single!): decide, for each fix, whether it can be delivered
//  to the broker right now or must be stored on the SD card, and - whenever the
//  link is back and a backlog exists - drain that backlog. It is the glue that
//  ties TelemetryPublisher (seal a fix), MqttClient (confirmed publish) and
//  FixQueue (persistent store) together, so main.cpp stays a thin wiring layer.
//
//  Wire format: every message is a JSON *array* of envelopes - a single fix is
//  just an array of one, a drained backlog is an array of many. The broker/
//  subscriber therefore always parses the same shape.
//
//  Delivery rule (matches the requirement "save it only when it was not sent"):
//    * link down                -> the sealed fix is appended to the SD queue.
//    * link up, queue empty     -> publish [fix] and wait for the QoS-2 ack;
//                                  only if that fails is the fix queued. So the
//                                  healthy steady state never touches the card.
//    * link up, queue non-empty -> queue the fix too, then drain everything as
//                                  one-or-more bursts, deleting each burst from
//                                  the card only after its ack.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "mqtt/TelemetrySample.h"
#include "sdcard/FixQueue.h"

class FixForwarder {
 public:
  // Borrows all collaborators and `topic` (all must outlive this object).
  //   publisher    : seals a fix into its encrypted envelope
  //   mqtt         : confirmed (QoS-2) transport
  //   queue        : persistent SD-backed store of undelivered envelopes
  //   topic        : topic every message is published to
  //   ackTimeoutMs : how long to wait for the broker's delivery ack
  //   maxBurst     : max envelopes per burst message (RAM/MQTT safety bound)
  FixForwarder(TelemetryPublisher& publisher, MqttClient& mqtt, FixQueue& queue,
               const char* topic, uint32_t ackTimeoutMs, std::size_t maxBurst);

  // Handle one telemetry sample end to end: publish it (with any backlog) or
  // store it.
  void process(const TelemetrySample& sample);

 private:
  // Send queued envelopes in bursts of up to maxBurst_ until the queue is empty
  // or a burst fails to be acked (in which case the survivors stay on the card
  // for the next attempt). Returns true if the queue was fully drained.
  bool drainQueue();

  // Wrap a list of envelope strings into one JSON array message.
  static std::string buildArrayMessage(const std::vector<std::string>& envs);

  TelemetryPublisher& publisher_;
  MqttClient&         mqtt_;
  FixQueue&           queue_;
  const char*         topic_;
  uint32_t            ackTimeoutMs_;
  std::size_t         maxBurst_;
};
