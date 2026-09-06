#pragma once

// =============================================================================
//  RemoteSchedule  -  Keep the ScheduleBundle in step with the broker's schedule
//                     topic.
// -----------------------------------------------------------------------------
//  Responsibility (single!): subscribe to the schedule topic and turn whatever
//  lands there into a validated, persisted ScheduleBundle. It is the glue
//  between MqttClient (bytes arrive), ScheduleCodec (bytes -> bundle) and
//  ScheduleStore (bundle -> card), so SettingsSelector only ever asks for
//  current().
//
//  This is the sibling of RemoteSettings and works exactly the same way, because
//  the problem is exactly the same one: a retained document on a topic, a device
//  that is asleep whenever it changes, and an event task that must not be made
//  to wait on the SD card.
//
//    1. PUSH on the open subscription - the fast path.
//    2. RETAINED REPLAY ON CONNECT - MqttClient re-issues every subscription on
//       each MQTT_EVENT_CONNECTED, which is what catches a device that was
//       asleep when the schedule was edited.
//    3. PERIODIC RE-CHECK - resyncIfDue() re-SUBSCRIBEs on purpose, the backstop
//       for a connection that looks alive and is delivering nothing. It matters
//       more here than for the config: the server deliberately does NOT
//       republish a bundle when it notices a device is behind (that would be a
//       publish every thirty seconds until the device next reports), so this is
//       what actually closes that loop.
//
//  Threading, as in RemoteSettings: onMessage() runs on the esp-mqtt event task
//  and does the minimum - copy the payload under a mutex, set an event bit -
//  while poll() does the parsing and the card write on the application task.
//
//  Why this is a separate class and not RemoteSettings generalised over a topic:
//  the two differ in more than their topic. A config document merges into the
//  settings in force; a bundle replaces the previous one whole. RemoteSettings is
//  working, load-bearing code, and folding a second format into it to save a
//  hundred lines would put the config path at risk for no functional gain. The
//  shared subscribe/mutex/event-group shell is a fair candidate for extraction
//  into a small RetainedTopicWatcher later, on its own.
// =============================================================================

#include <cstdint>
#include <string>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "mqtt/MqttClient.h"
#include "settings/ScheduleBundle.h"
#include "settings/ScheduleStore.h"
#include "settings/UpdateSignal.h"

class RemoteSchedule {
 public:
  // Borrows all collaborators and `topic` (all must outlive this object).
  RemoteSchedule(MqttClient& mqtt, ScheduleStore& store, UpdateSignal& signal,
                 const char* topic);
  ~RemoteSchedule();

  // Seed the in-memory bundle with `initial` (normally what ScheduleStore loaded
  // from the card), install the message handler and subscribe.
  //
  // Call this BEFORE MqttClient::begin(), for the same reason RemoteSettings
  // insists on it: the subscription is remembered and issued on connect, which
  // guarantees a handler is in place before the broker can replay a retained
  // message.
  bool begin(const ScheduleBundle& initial);

  // The bundle currently in force. Only poll() ever changes this, so a caller
  // that owns the polling loop needs no locking.
  const ScheduleBundle& current() const { return current_; }

  // Apply a bundle message if one has arrived since the last call. Parses and -
  // only when the revision actually differs from what we already have - writes
  // it through to the card. Returns true if a message was applied, false if none
  // was waiting or it was unusable.
  bool poll();

  // Ask the broker to replay the retained bundle, but at most once every
  // `intervalSeconds`. Cheap to call in a loop - a no-op until the interval is
  // up, and a no-op while the link is down. Zero disables it.
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
  ScheduleStore& store_;
  UpdateSignal&  signal_;
  const char*    topic_;

  ScheduleBundle current_;  // application-task only

  // When the next resyncIfDue() may act, as an esp_timer stamp (monotonic since
  // boot, and reset by the deep-sleep reboot - which is right, since a fresh
  // wake has just been handed the retained bundle anyway).
  int64_t nextResyncUs_;

  SemaphoreHandle_t mutex_;  // guards the two fields below
  std::string       pendingPayload_;
  bool              pending_;
};
