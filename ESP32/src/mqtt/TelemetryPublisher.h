#pragma once

// =============================================================================
//  TelemetryPublisher  -  Turn a GNSS fix into an encrypted MQTT message.
// -----------------------------------------------------------------------------
//  Responsibility (single!): take one GnssFix, format it as the agreed JSON,
//  encrypt that JSON end-to-end, and publish the envelope to the topic. It is
//  the glue that wires GNSS + crypto + transport together, while each of those
//  stays a small, independent class:
//
//      GnssFix --(this class formats)--> plaintext JSON
//              --(PayloadCrypto)-------> encrypted envelope
//              --(MqttClient)----------> broker topic
//
//  The plaintext JSON field names match the desktop GnssPayload exactly, so the
//  Python subscriber decodes it directly.
// =============================================================================

#include "crypto/PayloadCrypto.h"
#include "gnss/GnssData.h"
#include "mqtt/MqttClient.h"

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

  // Format `fix` (latitude, longitude, speed, altitude, time), encrypt it and
  // publish it. Returns true if the encrypted message was handed to the broker.
  bool publishFix(const GnssFix& fix);

 private:
  // Build the compact plaintext JSON for `fix`. Field names mirror the desktop
  // GnssPayload so both sides agree on the format.
  std::string buildPayloadJson(const GnssFix& fix) const;

  MqttClient&    mqtt_;
  PayloadCrypto& crypto_;
  const char*    topic_;
  const char*    deviceId_;
};
