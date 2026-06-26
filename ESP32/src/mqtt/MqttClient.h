#pragma once

// =============================================================================
//  MqttClient  -  Owns the connection to the MQTT broker. Transport only.
// -----------------------------------------------------------------------------
//  Responsibility (single!): connect to a broker and publish bytes. It knows
//  nothing about GNSS or encryption - it just moves an opaque payload to a
//  topic. That keeps it reusable and easy to reason about.
//
//  It wraps the ESP-IDF esp-mqtt client. The broker URI decides the transport:
//      wss://host:port/path   encrypted MQTT-over-WebSocket  (recommended)
//      ws://host:port/path    plain MQTT-over-WebSocket
//      mqtts://host:port      encrypted MQTT-over-TCP
//      mqtt://host:port       plain MQTT-over-TCP
//  For the secure (TLS) schemes the broker's certificate is verified against
//  the built-in CA bundle, so we know we are really talking to the broker.
//
//  Note on security layering: this TLS link protects the hop to the broker.
//  The GNSS payload is *additionally* end-to-end encrypted (see PayloadCrypto)
//  so the broker itself never sees the position data.
// =============================================================================

#include <string>

#include "mqtt_client.h"

class MqttClient {
 public:
  // Borrows all string arguments (does not copy) - they must outlive this
  // object. With Config.h that is automatic (they are constexpr globals).
  //   uri      : broker URI, e.g. "wss://broker.example:443/mqtt"
  //   username : broker login name ("" if the broker needs none)
  //   password : broker login password ("" if none)
  //   clientId : MQTT client id shown in the broker's logs
  MqttClient(const char* uri, const char* username, const char* password,
             const char* clientId);
  ~MqttClient();

  // Initialise and start the client. It then connects (and auto-reconnects) in
  // the background. Returns true if the client started; use isConnected() to
  // learn when the link is actually up.
  bool begin();

  // True while we currently have a live connection to the broker.
  bool isConnected() const;

  // Publish `payload` to `topic` at QoS 1. Returns true if the message was
  // accepted by the client for sending (false if not connected / on error).
  bool publish(const std::string& topic, const std::string& payload);

 private:
  // ESP-IDF C-style event callback. `arg` is the MqttClient instance so we can
  // track the connection state.
  static void eventHandler(void* arg, esp_event_base_t base, int32_t eventId,
                           void* eventData);

  const char* uri_;
  const char* username_;
  const char* password_;
  const char* clientId_;

  esp_mqtt_client_handle_t client_;     // underlying esp-mqtt handle
  volatile bool            connected_;  // updated from the event callback
};
