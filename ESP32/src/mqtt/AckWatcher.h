#pragma once

// =============================================================================
//  AckWatcher  -  Learn which fixes the API actually stored.
// -----------------------------------------------------------------------------
//  Responsibility (single!): subscribe to the delivery-ack topic, open each
//  encrypted ack, and answer one question for the forwarder - "what happened to
//  the envelopes I just sent?". It is the glue between MqttClient (bytes
//  arrive), AckCrypto (bytes -> plaintext) and FixForwarder (act on the
//  verdict).
//
//  Why this class exists at all:
//    MqttClient::publishConfirmed only proves the BROKER took a message. If the
//    API is down, the device is unprovisioned, or a value fails validation, the
//    broker still ACKs and the fix used to be deleted from the SD card
//    regardless - silently losing it. A verdict here is the difference between
//    "Mosquitto has it" and "it is in the positions table".
//
//  Threading - the reason this is not just a callback (same shape as
//  RemoteSettings, and for the same reason, only more so):
//    MqttClient dispatches on the esp-mqtt event task. Opening an ack costs an
//    RSA-3072 private-key operation, which would stall that task's keep-alives
//    and acks for a hundred milliseconds or more. So the callback does the
//    minimum - copy the ciphertext under a mutex - and poll(), on the
//    application task, does the decrypt, the parse and the bookkeeping.
//
//  Verdicts are accumulated, not consumed on arrival: an ack for a burst can
//  land while we are still assembling the next one, so waitForAck() checks a
//  map that the polls keep filling in rather than racing a single slot.
// =============================================================================

#include <cstdint>
#include <map>
#include <string>
#include <vector>

#include "crypto/AckCrypto.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "mqtt/MqttClient.h"

class AckWatcher {
 public:
  // What the API said about one envelope.
  enum class AckVerdict {
    Unknown,   // no ack covering this id has arrived (yet)
    Stored,    // it is in the positions table (freshly inserted or a duplicate)
    Rejected,  // the API refused it; `reason` says why
  };

  // One envelope's verdict, plus the reason when it was rejected.
  struct AckResult {
    AckVerdict  verdict;
    std::string reason;  // empty unless verdict == Rejected
  };

  // Borrows all collaborators and the strings (all must outlive this object).
  //   mqtt     : transport we subscribe through
  //   crypto   : opens the encrypted acks
  //   topic    : ack topic to subscribe to (e.g. "devices/GNSS01/ack")
  //   deviceId : our own id, checked against each ack so a misrouted one is
  //              ignored rather than clearing the wrong device's queue
  AckWatcher(MqttClient& mqtt, AckCrypto& crypto, const char* topic,
             const char* deviceId);
  ~AckWatcher();

  // Install the message handler and subscribe.
  //
  // Call this BEFORE MqttClient::begin(), for the same reason RemoteSettings
  // does: the subscription is remembered and issued on connect, so a handler is
  // always in place before the broker can deliver anything.
  bool begin();

  // Apply any acks that have arrived since the last call. Returns true if at
  // least one was applied. Cheap when nothing is waiting, so it is safe to call
  // in a tight-ish poll loop.
  bool poll();

  // Wait up to `timeoutMs` for a verdict on every id in `ids`.
  //
  // Fills `resultsOut` with one entry per id, in the same order, and returns
  // true only when every one of them was resolved. A false return is not an
  // error - it means some ids are still Unknown, and the caller should leave
  // those fixes on the card and try again next cycle. Resolved ids are consumed,
  // so asking twice about the same id answers Unknown the second time.
  bool waitForAck(const std::vector<std::string>& ids,
                  std::vector<AckResult>& resultsOut, uint32_t timeoutMs);

 private:
  // Runs on the esp-mqtt event task. Keep it short: copy and queue, nothing more
  // - the RSA work happens in poll() on the application task.
  void onMessage(const std::string& topic, const std::string& payload);

  // Take every queued ciphertext, leaving the queue empty. Returns false when
  // nothing was pending.
  bool takePending(std::vector<std::string>& payloadsOut);

  // Decrypt and parse one ack, merging its verdicts into `resolved_`.
  // Returns false when the ack was unusable (bad crypto, bad JSON, wrong
  // device); such an ack is dropped, never partially applied.
  bool applyAck(const std::string& envelopeJson);

  MqttClient& mqtt_;
  AckCrypto&  crypto_;
  const char* topic_;
  const char* deviceId_;

  // Verdicts awaiting collection, keyed by envelope id. Application-task only.
  std::map<std::string, AckResult> resolved_;

  SemaphoreHandle_t        mutex_;  // guards pendingPayloads_
  std::vector<std::string> pendingPayloads_;
};
