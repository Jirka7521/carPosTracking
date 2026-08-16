#pragma once

// =============================================================================
//  TelemetryPublisher  -  Turn a telemetry sample into an encrypted MQTT message.
// -----------------------------------------------------------------------------
//  Responsibility (single!): take one TelemetrySample (position + battery +
//  accelerometer), format it as the agreed JSON, encrypt that JSON end-to-end,
//  and publish the envelope to the topic. It is the glue that wires the sensors
//  + crypto + transport together, while each of those stays a small, independent
//  class:
//
//      TelemetrySample --(this class formats)--> plaintext JSON
//                      --(PayloadCrypto)-------> encrypted envelope
//                      --(MqttClient)----------> broker topic
//
//  The plaintext JSON field names match the API's PositionPayloadDto exactly, so
//  the ingest subscriber decodes it directly. Battery/accel fields are only
//  emitted when their reading is valid, so an older decoder still parses the
//  position fields it knows.
// =============================================================================

#include "crypto/PayloadCrypto.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetrySample.h"

class TelemetryPublisher {
 public:
  // Borrows its collaborators and the string config - all must outlive this
  // object.
  //   mqtt     : transport used to publish
  //   crypto   : end-to-end encryption of the payload
  //   topic    : topic path to publish to (e.g. "/devices/GNSS01/possition")
  //   deviceId : id placed inside the payload (e.g. "GNSS01")
  TelemetryPublisher(MqttClient& mqtt, PayloadCrypto& crypto, const char* topic,
                     const char* deviceId);

  // Format `sample` (position + battery + accel), encrypt it and publish it.
  // Returns true if the encrypted message was handed to the broker.
  bool publishSample(const TelemetrySample& sample);

  // Format `sample` and encrypt it into the wire envelope, WITHOUT publishing.
  // On success writes the envelope to `envelopeOut` and returns true. This is
  // what the store-and-forward path (FixForwarder) uses so it can either
  // transmit the envelope now or persist the very same bytes to the SD queue.
  bool sealSample(const TelemetrySample& sample, std::string& envelopeOut) const;

 private:
  // Build the compact plaintext JSON for `sample`. Field names mirror the API's
  // PositionPayloadDto so both sides agree on the format.
  std::string buildPayloadJson(const TelemetrySample& sample) const;

  MqttClient&    mqtt_;
  PayloadCrypto& crypto_;
  const char*    topic_;
  const char*    deviceId_;
};
