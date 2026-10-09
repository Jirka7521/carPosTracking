#pragma once

// =============================================================================
//  PresenceReporter  -  Decide when the device says "online" and "goodbye".
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn connection events and sleep decisions into
//  the right status message at the right moment. StatusPublisher does the
//  formatting and sealing; this class only knows WHEN.
//
//    online   once per new MQTT connection. The first one of a boot also
//             carries the chip's reset reason, which is how a crash, a
//             watchdog or a brown-out reaches the dashboard: the device cannot
//             report those while they happen, but it can say so on the way
//             back up.
//    offline  on request, from DeepSleepController, just before the clean
//             DISCONNECT - the explicit "going to sleep / switched off /
//             battery low / error". Under MQTT 3.1.1 that clean DISCONNECT
//             also makes the broker discard the Last Will, so a planned sleep
//             is never misreported as a lost connection.
//
//  The two travel differently. "online" is published live or not at all - it
//  describes the connection it rides on. "offline" goes through EventLog: said
//  out of range it waits on the SD card, and reaches the history with the next
//  connection instead of being lost. That backlog is drained BEFORE a new
//  connection is announced, so whatever it holds lands ahead of the "online"
//  that ends it.
//
//  Everything runs on the MAIN task, never from MqttClient's event callback,
//  for three reasons: publishConfirmed() polls for an ack that the event task
//  delivers, so calling it from there would deadlock; PayloadCrypto's random
//  generator is shared with the fix sealing and is not thread-safe; and
//  MqttClient keeps a single "last acked" slot that a second publisher could
//  overwrite. So the main loop calls service() at the points where it already
//  wakes up, and a new connection is noticed by its counter changing.
//
//  Best effort throughout: an online message that cannot be delivered is
//  logged and dropped. Nothing here may delay a sleep or a report for long -
//  the publish timeout and EventLog's flush budget bound every call.
// =============================================================================

#include <cstdint>

#include "events/EventLog.h"
#include "mqtt/MqttClient.h"
#include "mqtt/OfflineReason.h"
#include "mqtt/StatusPublisher.h"
#include "power/BatteryData.h"

class PresenceReporter {
 public:
  // Borrows `mqtt`, `publisher` and `log` (all must outlive this object).
  //   resetReason : this boot's reset cause, sent with the first online
  //                 message (a string with static lifetime, or nullptr)
  PresenceReporter(MqttClient& mqtt, StatusPublisher& publisher, EventLog& log,
                   const char* resetReason);

  // Drain the event backlog, then announce "online" if the client has connected
  // since the last announcement. Cheap when there is nothing to do - a few
  // integer compares - so it is safe to call from every place the main loop
  // passes through.
  void service();

  // Remember the latest battery reading, so a goodbye can say how full the pack
  // was. An invalid reading leaves the previous one in place.
  void noteBattery(const BatteryStatus& battery);

  // Record "offline because `reason`" - published now if connected, kept on
  // the card otherwise. A pending online announcement goes first, so a restart
  // reason is never lost behind a sleep.
  //   sleepS : expected time away in seconds, 0 = unknown
  //   detail : short machine code for an Error, or nullptr
  void reportOffline(OfflineReason reason, uint32_t sleepS,
                     const char* detail = nullptr);

 private:
  MqttClient&      mqtt_;
  StatusPublisher& publisher_;
  EventLog&        log_;

  // Sent with online messages until one is confirmed, then cleared - the reset
  // cause belongs to the boot, not to every reconnect within it.
  const char* resetReason_;

  // MqttClient::connectCount() at the last announcement; 0 = none yet.
  uint32_t announcedCount_;

  // Last known battery percent for the goodbye; -1 = unknown.
  int batteryPct_;
};
