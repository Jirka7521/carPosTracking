#pragma once
#include <cstdint>

#include "driver/uart.h"

// =============================================================================
//  Config.example.h  -  Committed template (NO secrets).
// -----------------------------------------------------------------------------
//  This is the version-controlled template for `Config.h`. The real `Config.h`
//  holds your private WiFi credentials and is git-ignored so it never reaches
//  GitHub.
//
//  First-time setup:
//      cp src/config/Config.example.h src/config/Config.h
//  then open Config.h and fill in kWifiSsid / kWifiPassword.
//
//  Keep this template free of real credentials.
// =============================================================================

namespace config {

// -----------------------------------------------------------------------------
//  UART wiring between the ESP32 and the SIM7000G modem.
//  These are the factory pin assignments for the T-SIM7000G board. UART0 is
//  intentionally avoided because it is used by the USB serial console.
// -----------------------------------------------------------------------------
constexpr uart_port_t kModemUartPort = UART_NUM_1;  // Hardware UART peripheral
constexpr int         kModemTxPin    = 27;          // ESP32 TX  -> modem RX
constexpr int         kModemRxPin    = 26;          // ESP32 RX  <- modem TX
constexpr int         kModemBaudRate = 9600;        // SIM7000G default baud

// -----------------------------------------------------------------------------
//  Power control pin.
//  PWRKEY toggles the whole modem on/off. On the T-SIM7000G it idles HIGH and
//  is pulsed LOW to switch the modem state.
// -----------------------------------------------------------------------------
constexpr int kModemPwrKeyPin = 4;

// -----------------------------------------------------------------------------
//  Which satellite constellations to use.
//  The SIM7000G can fuse several systems at once for a faster, more accurate
//  fix. GPS is always enabled by the modem and cannot be turned off.
// -----------------------------------------------------------------------------
constexpr bool kEnableGps     = true;   // USA  (always on, kept for clarity)
constexpr bool kEnableGlonass = true;   // Russia
constexpr bool kEnableBeidou  = true;   // China
constexpr bool kEnableGalileo = true;   // Europe

// -----------------------------------------------------------------------------
//  Debugging.
//  Set this to `true` to print, on every read, the full decoded fix plus the
//  number of satellites seen per constellation. Set to `false` for silent,
//  production operation (the print code is then removed by the optimiser).
// -----------------------------------------------------------------------------
constexpr bool kGnssDebug = true;

// -----------------------------------------------------------------------------
//  Timing knobs.
// -----------------------------------------------------------------------------
constexpr uint32_t kFixPollIntervalMs = 5000;  // Gap between position reads
constexpr uint32_t kSatelliteScanMs   = 3000;  // How long to listen to NMEA
                                               // when counting satellites

// -----------------------------------------------------------------------------
//  WiFi (station mode).
//
//  ⚠ SECRETS: fill kWifiSsid / kWifiPassword in your local Config.h only. Leave
//  them blank here in the committed template.
//
//  Set kWifiEnabled to `false` to skip WiFi entirely; the connect code is then
//  removed by the optimiser and the SSID/password below are ignored.
// -----------------------------------------------------------------------------
constexpr bool kWifiEnabled = true;

constexpr char kWifiSsid[]     = "";  // <-- your network name (set in Config.h)
constexpr char kWifiPassword[] = "";  // <-- your network password (set in Config.h)

// How long to wait for an IP address before giving up a connection attempt, and
// how many times to retry the association before reporting failure.
constexpr uint32_t kWifiConnectTimeoutMs = 15000;
constexpr int      kWifiMaxRetries       = 5;

// After the burst of kWifiMaxRetries fast retries fails, keep trying to
// reconnect in the background at this interval until an IP is obtained.
constexpr uint32_t kWifiReconnectIntervalMs = 30000;

// -----------------------------------------------------------------------------
//  MQTT broker.
//
//
//  The broker URI scheme decides the transport AND whether the hop is TLS:
//      wss://host:port/path   encrypted MQTT-over-WebSocket  (recommended)
//      ws://host:port/path    plain MQTT-over-WebSocket
//      mqtts://host:port      encrypted MQTT-over-TCP
//      mqtt://host:port       plain MQTT-over-TCP
//
//  ⚠ SECRET: put kMqttPassword in your local Config.h only; leave it blank in
//  this committed template.
// -----------------------------------------------------------------------------
constexpr bool kMqttEnabled = true;

// Full broker URI. Build it from your host, port and (for WebSocket) path.
constexpr char kMqttBrokerUri[] = "url for mqtt broker";  // <-- set in Config.h, leave blank here

// Broker login. Username is not secret; the password is (keep it in Config.h).
constexpr char kMqttUsername[] = "admin";
constexpr char kMqttPassword[] = "";  // <-- set in Config.h, leave blank here

// MQTT client id shown in the broker's logs.
constexpr char kMqttClientId[] = "gnss-tracker";

// -----------------------------------------------------------------------------
//  Telemetry topic + device identity.
//  The single place the publish path is defined (matches the desktop tools).
// -----------------------------------------------------------------------------
constexpr char kDeviceId[]       = "GNSS";
constexpr char kTelemetryTopic[] = "/path/pos";

// -----------------------------------------------------------------------------
//  End-to-end encryption.
//  The GNSS payload is encrypted (RSA-OAEP-SHA256 + AES-256-GCM) for the holder
//  of the receiver PRIVATE key, so the broker only ever sees ciphertext. This
//  device only needs the receiver PUBLIC key.
//
//  Generate the key pair with desktop/create_certificates.py, then paste the
//  contents of desktop/certs/receiver_public.pem below (in your Config.h). The
//  public key is not a secret, but keep this template's copy as a placeholder.
// -----------------------------------------------------------------------------
constexpr char kReceiverPublicKeyPem[] =
    "-----BEGIN PUBLIC KEY-----\n"
    "PASTE receiver_public.pem HERE (one C string line per PEM line, each\n"
    "ending with \\n). See desktop/certs/receiver_public.pem.\n"
    "-----END PUBLIC KEY-----\n";

}  // namespace config
