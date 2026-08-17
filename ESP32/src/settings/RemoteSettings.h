#pragma once

// =============================================================================
//  RemoteSettings  -  Keep DeviceSettings in step with the broker's config topic.
// -----------------------------------------------------------------------------
//  Responsibility (single!): subscribe to the config topic, and turn whatever
//  lands there into a validated, persisted DeviceSettings. It is the glue
//  between MqttClient (bytes arrive), SettingsCodec (bytes -> settings) and
//  SettingsStore (settings -> card), so main() only ever asks it for current().
//
//  Three ways a document gets here, one place it is applied:
//    1. PUSH - we hold an open subscription, so the broker delivers a live
//       publish unasked. This is the fast path and needs no request from us.
//    2. RETAINED REPLAY ON CONNECT - the API publishes retained, and MqttClient
//       re-subscribes on every MQTT_EVENT_CONNECTED (the session is clean, so the
//       broker forgets us on each disconnect). That is what catches a device that
//       was offline when the setting was saved: nothing is lost, it simply
//       arrives on reconnect.
//    3. PERIODIC RE-CHECK - resyncIfDue() re-subscribes on purpose. The backstop
//       for a connection that looks alive but is delivering nothing.
//  All three end in poll(), so there is exactly one parse/clamp/save path.
//
//  Threading - the reason this class is not just a callback:
//    MqttClient dispatches on the esp-mqtt event task. Writing to the SD card
//    from there would block that task behind slow SPI IO, stalling keep-alives
//    and acks. So the callback does the minimum: it copies the payload under a
//    mutex, sets a flag, and signals an event group. The application task later
//    calls poll(), which does the parsing, the clamping and the card write. All
//    the slow work happens on the caller's thread, and current() is only ever
//    mutated there.
//
//  Why an event group and not just the flag:
//    The application task spends nearly all its life blocked, waiting out the
//    reporting interval. Blocking on the event group instead of on a plain delay
//    costs exactly the same energy - a blocked task is a blocked task - but it
//    also wakes the instant a config arrives, which is what turns "applied within
//    one reporting interval" into "applied within a second". Same pattern as
//    WifiManager, which waits on its own event group for the IP.
// =============================================================================

#include <cstdint>
#include <string>

#include "freertos/FreeRTOS.h"
#include "freertos/event_groups.h"
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

  // Block for up to `timeoutMs` waiting for the broker to deliver a config, and
  // apply it. Returns true as soon as one is applied. A false return is not an
  // error: it just means we carry on with the settings we already have.
  //
  // This is a real block on the event group, not a poll loop - the task is
  // asleep for the whole timeout and is woken by the arriving message itself.
  // That makes it equally suitable for the few seconds after connect and for
  // waiting out a multi-minute reporting interval.
  //
  // A message that turns out to be unusable does not end the wait: the remaining
  // time is waited out, so one malformed publish cannot cost the caller its
  // timeout.
  bool waitForUpdate(uint32_t timeoutMs);

  // Ask the broker to replay the retained config, but at most once every
  // `intervalSeconds`. Cheap to call in a loop - it is a no-op until the interval
  // is up, and a no-op while the link is down. Zero disables it.
  //
  // Why this exists when push already works: a live publish only reaches us over
  // a connection that is genuinely alive. A half-open socket looks connected and
  // delivers nothing, and there is no way to tell from this side - so this is the
  // backstop that recovers from it.
  //
  // It is a plain SUBSCRIBE, not an unsubscribe/subscribe pair: MQTT 3.1.1
  // 3.8.4 requires a repeat SUBSCRIBE on an identical topic filter to re-send
  // matching retained messages *without* interrupting the flow of publications
  // [MQTT-3.8.4-3]. So this costs one packet instead of two and, unlike dropping
  // the subscription first, has no window in which a live config could be missed.
  //
  // Returns true when a resync was actually issued.
  bool resyncIfDue(uint32_t intervalSeconds);

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

  // When the next resyncIfDue() may act, as an esp_timer stamp (monotonic since
  // boot, and reset by the deep-sleep reboot - which is exactly right, since a
  // fresh wake has just been handed the retained config anyway).
  int64_t nextResyncUs_;

  // Signalled by onMessage() on the esp-mqtt event task, waited on by the
  // application task in waitForUpdate(). Carries no data - the payload itself
  // travels in pendingPayload_ under the mutex; this only says "look now".
  EventGroupHandle_t events_;

  SemaphoreHandle_t mutex_;          // guards the two fields below
  std::string       pendingPayload_;
  bool              pending_;
};
