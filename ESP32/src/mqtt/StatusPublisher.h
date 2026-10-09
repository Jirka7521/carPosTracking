#pragma once

// =============================================================================
//  StatusPublisher  -  Seal and publish the device's own presence messages.
// -----------------------------------------------------------------------------
//  Responsibility (single!): format one status message ("online", or "offline
//  because ..."), end-to-end encrypt it exactly like a fix, and publish it to
//  the status topic. It decides nothing about WHEN - that is PresenceReporter -
//  and nothing about how serious a reason is - that is the API.
//
//      reason --(this class formats)--> plaintext JSON
//             --(PayloadCrypto)-------> sealed envelope, wrapped as [envelope]
//             --(MqttClient)----------> devices/<id>/status
//
//  Why sealed, when nothing in it is a position: the project's promise is that
//  the broker only ever sees ciphertext from the device. A status message is
//  small and cheap to seal, and keeping the promise whole is simpler than
//  explaining an exception to it. The array wrapping is what lets the API reuse
//  its envelope decoder unchanged - a position batch is an array too.
//
//  The plaintext (snake_case, mirroring the API's DeviceStatusPayloadDto):
//
//      {"device":"GNSS01","type":"offline","reason":"sleep",
//       "time_utc":"2026-10-09T12:00:00Z","battery_pct":57,"sleep_s":300}
//      {"device":"GNSS01","type":"online","reset_reason":"PANIC"}
//
//  Every member but device/type is optional and simply left out when unknown.
//  time_utc in particular is only sent while DeviceClock trusts itself - a
//  confidently wrong timestamp is worse than none, and the API stamps its own
//  receive time either way.
//
//  It also seals the LAST WILL. That one is built once, before the connection
//  exists, and is replayed by the broker at some unknown later moment - so it
//  carries no time and no battery, only "connection_lost".
// =============================================================================

#include <cstdint>
#include <string>

#include "crypto/PayloadCrypto.h"
#include "mqtt/MqttClient.h"
#include "mqtt/OfflineReason.h"
#include "util/DeviceClock.h"

class StatusPublisher {
 public:
  // Borrows its collaborators and the string config - all must outlive this
  // object.
  //   mqtt      : transport used to publish
  //   crypto    : end-to-end encryption of the payload
  //   clock     : stamps time_utc while it is trusted
  //   topic     : the status topic, e.g. "devices/GNSS01/status"
  //   deviceId  : id placed inside the payload (e.g. "GNSS01")
  //   timeoutMs : how long one confirmed publish may wait for the broker's ack
  StatusPublisher(MqttClient& mqtt, PayloadCrypto& crypto, DeviceClock& clock,
                  const char* topic, const char* deviceId, uint32_t timeoutMs);

  // The topic every message (and the Last Will) goes to.
  const char* topic() const { return topic_; }

  // Seal the Last Will into `messageOut` - the complete MQTT payload, already
  // wrapped in its array. Returns false if sealing failed (e.g. no public key).
  bool sealLastWill(std::string& messageOut);

  // Announce that the device is (back) online. `resetReason` is the chip's
  // reset cause (BootJournal::resetCauseName()), or nullptr to leave it out -
  // it is only worth sending once per boot. Blocks until the broker confirms
  // or the timeout passes; returns true only on a confirmed delivery.
  bool publishOnline(const char* resetReason);

  // Say why the device is about to go offline. Blocks like publishOnline().
  //   sleepS     : how long it expects to be gone, in seconds; 0 = unknown
  //   batteryPct : last known percent (0 = the charging sentinel), -1 = omit
  //   detail     : short machine code for an Error ("gnss_init"), or nullptr
  bool publishOffline(OfflineReason reason, uint32_t sleepS, int batteryPct,
                      const char* detail);

 private:
  // The wire word for each reason - must match DeviceStatusValidator in the API.
  static const char* reasonName(OfflineReason reason);

  // Build the plaintext JSON. Any optional argument at its "absent" value
  // (nullptr / 0 / -1, and withTime false) is left out of the document.
  std::string buildJson(const char* type, const char* reason, bool withTime,
                        uint32_t sleepS, int batteryPct,
                        const char* resetReason, const char* detail) const;

  // Seal `plaintext` and wrap the envelope as a one-element JSON array.
  bool seal(const std::string& plaintext, std::string& messageOut);

  // Seal and publish with confirmation; `what` names the message in the log.
  bool sealAndPublish(const char* what, const std::string& plaintext);

  MqttClient&    mqtt_;
  PayloadCrypto& crypto_;
  DeviceClock&   clock_;
  const char*    topic_;
  const char*    deviceId_;
  uint32_t       timeoutMs_;
};
