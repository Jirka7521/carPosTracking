#pragma once

// =============================================================================
//  WifiManager  -  Owns the ESP32 WiFi station: bring-up, connect, status.
// -----------------------------------------------------------------------------
//  Responsibility (single!): manage a WiFi station-mode connection to one
//  access point. It hides the ESP-IDF boilerplate (NVS, netif, event loop,
//  esp_wifi, the event group handshake) behind a small, friendly API.
//
//  It knows nothing about the modem or GNSS - it is an independent subsystem so
//  it can be enabled/disabled on its own (see config::kWifiEnabled) and reused.
//
//  Typical lifecycle:
//      WifiManager wifi(config::kWifiSsid, config::kWifiPassword,
//                       config::kWifiMaxRetries,
//                       config::kWifiReconnectIntervalMs);
//      wifi.begin();                               // init the WiFi stack
//      wifi.connect(config::kWifiConnectTimeoutMs);// associate + get an IP
//      if (wifi.isConnected()) { ...use network... }
//
//  If the initial connect() fails, the manager keeps retrying on its own in the
//  background every reconnectIntervalMs until it gets an IP - no extra calls
//  needed. Poll isConnected() whenever you need to know the current state.
// =============================================================================

#include <cstdint>

#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/event_groups.h"

class WifiManager {
 public:
  // What the station is doing right now, for anything that needs to SHOW it.
  //
  // Deliberately coarser than the driver's own event stream: a status indicator
  // only needs to distinguish "not running" from "working on it" from "done",
  // and collapsing the retry burst, the background reconnect timer and the
  // initial association into one Connecting state is what keeps it that way.
  //
  // Note that Connecting persists indefinitely when no access point is
  // reachable - the manager never stops retrying - so a caller must not treat
  // it as a transient.
  enum class LinkState : uint8_t {
    Off        = 0,  // driver stopped, or never started
    Connecting = 1,  // associating, or waiting out a reconnect gap
    Connected  = 2,  // holding an IP
  };

  // Borrows (does not copy) the credential strings - they must outlive this
  // object. With Config.h that is automatic (they are constexpr globals).
  //   ssid                : network name to join
  //   password            : network password ("" for an open network)
  //   maxRetries          : fast association attempts in one burst before the
  //                         burst is considered failed
  //   reconnectIntervalMs : after a failed burst, gap between background
  //                         reconnect bursts (until an IP is obtained)
  WifiManager(const char* ssid, const char* password, int maxRetries,
              uint32_t reconnectIntervalMs);

  // Initialise the WiFi stack in station mode: NVS flash, the default event
  // loop, the network interface and the esp_wifi driver. Call once before
  // connect(). Returns true on success. Safe to call again (no-op if already
  // initialised).
  bool begin();

  // Associate with the configured access point and wait up to `timeoutMs` for
  // an IP address. Returns true once connected, false on timeout / failure.
  bool connect(uint32_t timeoutMs);

  // True while we currently hold an IP address.
  bool isConnected() const;

  // Coarse state for status indicators. Cheap: a single member read, safe to
  // call from another task on a tick.
  LinkState linkState() const;

  // Disconnect from the access point and stop the WiFi driver (lower power).
  // begin()/connect() can be used again afterwards.
  void disconnect();

 private:
  // ESP-IDF C-style event callback. `arg` is the WifiManager instance so we can
  // route events back into member state.
  static void eventHandler(void* arg, const char* eventBase, int32_t eventId,
                           void* eventData);

  // esp_timer callback fired reconnectIntervalMs after a failed burst. `arg` is
  // the WifiManager instance; it starts a fresh association burst.
  static void reconnectTimerCb(void* arg);

  const char* ssid_;
  const char* password_;
  int         maxRetries_;
  uint32_t    reconnectIntervalMs_;

  bool               initialised_;   // begin() has set up the WiFi stack
  // The single source of truth for "what is the link doing". isConnected() is
  // derived from it rather than tracked alongside it, so the two cannot
  // disagree. Written from the WiFi event task, read from the main and
  // indicator tasks; a single aligned enum store needs no lock.
  volatile LinkState linkState_;
  int                retryCount_;    // association retries in the current burst
  EventGroupHandle_t events_;        // signals connect success / failure
  esp_timer_handle_t reconnectTimer_;  // periodic background reconnect
};
