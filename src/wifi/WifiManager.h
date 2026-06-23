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
//                       config::kWifiMaxRetries);
//      wifi.begin();                               // init the WiFi stack
//      wifi.connect(config::kWifiConnectTimeoutMs);// associate + get an IP
//      if (wifi.isConnected()) { ...use network... }
// =============================================================================

#include <cstdint>

#include "freertos/FreeRTOS.h"
#include "freertos/event_groups.h"

class WifiManager {
 public:
  // Borrows (does not copy) the credential strings - they must outlive this
  // object. With Config.h that is automatic (they are constexpr globals).
  //   ssid       : network name to join
  //   password   : network password ("" for an open network)
  //   maxRetries : association attempts before connect() reports failure
  WifiManager(const char* ssid, const char* password, int maxRetries);

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

  // Disconnect from the access point and stop the WiFi driver (lower power).
  // begin()/connect() can be used again afterwards.
  void disconnect();

 private:
  // ESP-IDF C-style event callback. `arg` is the WifiManager instance so we can
  // route events back into member state.
  static void eventHandler(void* arg, const char* eventBase, int32_t eventId,
                           void* eventData);

  const char* ssid_;
  const char* password_;
  int         maxRetries_;

  bool             initialised_;   // begin() has set up the WiFi stack
  bool             connected_;      // we currently hold an IP
  int              retryCount_;     // association retries so far
  EventGroupHandle_t events_;       // signals connect success / failure
};
