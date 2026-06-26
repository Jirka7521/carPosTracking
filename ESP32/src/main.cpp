
#include "config/Config.h"
#include "crypto/PayloadCrypto.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "gnss/GnssModule.h"
#include "modem/Sim7000Modem.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "serial/SerialPort.h"
#include "wifi/WifiManager.h"

static const char* TAG = "main";

extern "C" void app_main(void) {
  ESP_LOGI(TAG, "Car position tracker starting...");

  // Bring up WiFi first (if enabled in Config.h). It is an independent
  // subsystem, so a connection failure is logged but does not stop tracking.
  if (config::kWifiEnabled) {
    static WifiManager wifi(config::kWifiSsid, config::kWifiPassword,
                            config::kWifiMaxRetries,
                            config::kWifiReconnectIntervalMs);
    if (wifi.begin() && wifi.connect(config::kWifiConnectTimeoutMs)) {
      ESP_LOGI(TAG, "WiFi connected.");
    } else {
      // Not connected yet - WifiManager keeps retrying in the background, so we
      // start tracking now rather than blocking on the network.
      ESP_LOGW(TAG,
               "WiFi not connected - continuing; retrying in background.");
    }
  } else {
    ESP_LOGI(TAG, "WiFi disabled in Config.h.");
  }

  static SerialPort serial(config::kModemUartPort, config::kModemTxPin,
                           config::kModemRxPin, config::kModemBaudRate);
  static Sim7000Modem modem(serial, config::kModemPwrKeyPin);
  static GnssModule   gnss(modem);

  // Power up the modem, enable the GNSS engine and select the constellations.
  if (!gnss.begin()) {
    ESP_LOGE(TAG, "GNSS init failed - check wiring and power. Halting.");
    return;
  }

  ESP_LOGI(TAG, "GNSS ready. Reading position every %lu ms.",
           (unsigned long)config::kFixPollIntervalMs);

  // Bring up MQTT (if enabled). The client connects and reconnects in the
  // background, so we never block on it. Each fix is end-to-end encrypted by
  // PayloadCrypto before TelemetryPublisher hands it to the broker.
  static MqttClient     mqtt(config::kMqttBrokerUri, config::kMqttUsername,
                             config::kMqttPassword, config::kMqttClientId);
  static PayloadCrypto  crypto(config::kReceiverPublicKeyPem);
  static TelemetryPublisher publisher(mqtt, crypto, config::kTelemetryTopic,
                                      config::kDeviceId);
  if (config::kMqttEnabled) {
    if (mqtt.begin()) {
      ESP_LOGI(TAG, "MQTT enabled; publishing fixes to %s.",
               config::kTelemetryTopic);
    } else {
      ESP_LOGW(TAG, "MQTT failed to start; continuing without publishing.");
    }
  } else {
    ESP_LOGI(TAG, "MQTT disabled in Config.h.");
  }

  // Main loop
  while (true) {
    GnssFix fix;  // Holds the latest position/speed/time and satellite counts
    if (gnss.readFix(fix)) {
      if (fix.hasFix()) {
        // A real position is available
        ESP_LOGI(TAG, "Fix: %.6f, %.6f  %.1f km/h",
                 fix.position.latitudeDeg, fix.position.longitudeDeg,
                 fix.speedKmph);

        // Publish the encrypted position (only when MQTT is up).
        if (config::kMqttEnabled && mqtt.isConnected()) {
          publisher.publishFix(fix);
        }
      } else {
        ESP_LOGI(TAG, "Waiting for satellite fix...");
      }
    } else {
      ESP_LOGW(TAG, "Failed to read GNSS.");
    }

    // To save battery on a long interval you could instead do:
    //     gnss.powerOffModule();
    //     vTaskDelay(...long sleep...);
    //     gnss.powerOnModule();
    // Here we simply keep the engine running between quick polls.
    vTaskDelay(pdMS_TO_TICKS(config::kFixPollIntervalMs));
  }
}
