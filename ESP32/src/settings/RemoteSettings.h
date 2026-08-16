#pragma once

// =============================================================================
//  RemoteSettings  -  Keep DeviceSettings in step with the broker's config topic.
// -----------------------------------------------------------------------------
//  Responsibility (single!): subscribe to the config topic, and turn whatever
//  lands there into a validated, persisted DeviceSettings. It is the glue
//  between MqttClient (bytes arrive), SettingsCodec (bytes -> settings) and
//  SettingsStore (settings -> card), so main() only ever asks it for current().
//
//  Threading - the reason this class is not just a callback:
//    MqttClient dispatches on the esp-mqtt event task. Writing to the SD card
//    from there would block that task behind slow SPI IO, stalling keep-alives
//    and acks. So the callback does the minimum: it copies the payload under a
//    mutex and sets a flag. The application task later calls poll(), which does
//    the parsing, the clamping and the card write. All the slow work happens on
//    the caller's thread, and current() is only ever mutated there.
//
//  Retained config, and why it matters:
//    With sleep_between on, the device is awake for a handful of seconds per
//    cycle. It will practically never be connected at the moment someone
//    publishes a live config. Publish the config message with the RETAIN flag so
//    the broker replays it the instant we subscribe - that is what
//    waitForUpdate() is listening for right after connect.
// =============================================================================

#include <cstdint>
#include <string>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "mqtt/MqttClient.h"
#include "settings/DeviceSettings.h"
#include "settings/SettingsStore.h"

class RemoteSettings {
 public:
  // Borrows all collaborators and `topic` (all must outlive this object).
  RemoteSettings(MqttClient& mqtt, SettingsStore& store, const char* topic);
  ~RemoteSettings();

  // Seed the in-memory settings with `initial` (normally what SettingsStore
  // loaded from the card), install the message handler and subscribe.
  //
  // Call this BEFORE MqttClient::begin(): the subscription is remembered and
  // issued on connect, which guarantees a handler is in place before the broker
  // can replay a retained message.
  bool begin(const DeviceSettings& initial);

  // The settings currently in force. Only poll() ever changes this, so a caller
  // that owns the polling loop needs no locking.
  const DeviceSettings& current() const { return current_; }

  // Apply a config message if one has arrived since the last call. Parses,
  // clamps, and - only when the values actually differ from what we already had
  // - writes them through to the card. Returns true if a message was applied
  // (whether or not it changed anything), false if none was waiting or it was
  // unusable.
  bool poll();

  // Poll for up to `timeoutMs` waiting for the broker to deliver a config.
  // Returns true as soon as one is applied. A false return is not an error: it
  // just means we carry on with the cached settings.
  bool waitForUpdate(uint32_t timeoutMs);

 private:
  // Runs on the esp-mqtt event task. Keep it short: copy and flag, nothing more.
  void onMessage(const std::string& topic, const std::string& payload);

  // Take the queued payload, if any, leaving the slot empty. Returns false when
  // nothing was pending.
  bool takePending(std::string& payloadOut);

  MqttClient&    mqtt_;
  SettingsStore& store_;
  const char*    topic_;

  DeviceSettings current_;  // application-task only

  SemaphoreHandle_t mutex_;          // guards the two fields below
  std::string       pendingPayload_;
  bool              pending_;
};
