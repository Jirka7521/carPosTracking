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

#include <functional>
#include <string>

#include "mqtt_client.h"

class MqttClient {
 public:
  // Called with the topic and the raw bytes of every message we receive on a
  // subscribed topic. It runs on the *esp-mqtt event task*, so it must return
  // promptly and must not block on slow IO (SD writes, network) - copy the
  // payload and let an application task do the real work. See RemoteSettings.
  using MessageHandler =
      std::function<void(const std::string& topic, const std::string& payload)>;

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

  // Disconnect cleanly and stop the client task. Called before deep sleep so the
  // broker sees a proper DISCONNECT instead of waiting out the keep-alive.
  // Safe to call on a client that never started.
  void stop();

  // True while we currently have a live connection to the broker.
  bool isConnected() const;

  // Install the callback that receives every subscribed message. Set it before
  // begin() so a retained message cannot arrive before there is a handler.
  void setMessageHandler(MessageHandler handler);

  // Subscribe to `topic`. Safe to call before the link is up: the topic is
  // remembered and (re-)subscribed on every MQTT_EVENT_CONNECTED, which matters
  // because a broker with a clean session forgets our subscriptions across the
  // reconnect that follows each deep-sleep wake. One subscription is all this
  // firmware needs, so a later call simply replaces the remembered topic.
  bool subscribe(const char* topic, int qos);

  // Publish `payload` to `topic` at QoS 2. Returns true if the message was
  // accepted by the client for sending (false if not connected / on error).
  // "Accepted" is not "delivered" - use publishConfirmed() when you need to know
  // the broker actually received it (e.g. before deleting it from the SD queue).
  bool publish(const std::string& topic, const std::string& payload);

  // Publish `payload` at QoS 2 and block until the broker's PUBLISHED (PUBCOMP)
  // ack for *this* message arrives, or `timeoutMs` elapses / the link drops.
  // Returns true only on a confirmed delivery. This is the guarantee the
  // store-and-forward queue relies on: a fix is removed from SD only once the
  // broker has taken responsibility for it.
  bool publishConfirmed(const std::string& topic, const std::string& payload,
                        uint32_t timeoutMs);

 private:
  // ESP-IDF C-style event callback. `arg` is the MqttClient instance so we can
  // track the connection state.
  static void eventHandler(void* arg, esp_event_base_t base, int32_t eventId,
                           void* eventData);

  // Accumulate one MQTT_EVENT_DATA. A payload larger than the client's RX buffer
  // is delivered in several events, and only the first carries the topic - so we
  // stitch the pieces together and dispatch once the last one lands.
  void handleData(const esp_mqtt_event_t& event);

  const char* uri_;
  const char* username_;
  const char* password_;
  const char* clientId_;

  esp_mqtt_client_handle_t client_;     // underlying esp-mqtt handle
  volatile bool            connected_;  // updated from the event callback

  // Message id of the most recent broker-acked publish (MQTT_EVENT_PUBLISHED),
  // written from the event callback and polled by publishConfirmed(). -1 means
  // "nothing acked yet".
  volatile int             lastAckedMsgId_;

  // The single remembered subscription, replayed on every reconnect. Empty topic
  // means "nothing subscribed".
  std::string subscribedTopic_;
  int         subscribedQos_;

  MessageHandler messageHandler_;  // empty until setMessageHandler()

  // Reassembly state for a fragmented incoming message (event-task only).
  std::string rxTopic_;
  std::string rxPayload_;
};
