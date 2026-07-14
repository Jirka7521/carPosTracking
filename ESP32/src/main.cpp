
#include "config/Config.h"
#include "crypto/PayloadCrypto.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "gnss/GnssModule.h"
#include "modem/Sim7000Modem.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "power/DeepSleepController.h"
#include "sdcard/FixForwarder.h"
#include "sdcard/FixQueue.h"
#include "sdcard/SdCard.h"
#include "serial/SerialPort.h"
#include "settings/DeviceSettings.h"
#include "settings/RemoteSettings.h"
#include "settings/SettingsStore.h"
#include "wifi/WifiManager.h"

static const char* TAG = "main";

extern "C" void app_main(void) {
  // Undo the PWRKEY latch left behind by a previous deep sleep. This must happen
  // before any driver touches that pin, or the modem could never be pulsed back
  // on. On a cold boot there is nothing latched and this is a no-op.
  DeepSleepController::releasePinHolds(config::kModemPwrKeyPin);

  ESP_LOGI(TAG, "Car position tracker starting (wake cause: %s).",
           DeepSleepController::wakeCauseName());

  // WiFi. Constructed unconditionally - even a WiFi-disabled build hands it to
  // DeepSleepController, whose shutdown sequence must be able to stop the radio.
  // Only the bring-up below is gated on the feature flag; disconnect() on an
  // un-begun manager is a no-op.
  static WifiManager wifi(config::kWifiSsid, config::kWifiPassword,
                          config::kWifiMaxRetries,
                          config::kWifiReconnectIntervalMs);
  if (config::kWifiEnabled) {
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

  // microSD store-and-forward. When the broker cannot be reached, each fix is
  // sealed (same encrypted envelope as transmit) and appended to a queue file
  // on the card; once the link is back the backlog is drained in encrypted
  // bursts. The FixForwarder owns this decide-publish-or-store logic so the
  // loop below stays trivial. All optional (kSdEnabled): if the card is absent
  // the forwarder still publishes live fixes, it just cannot store missed ones.
  //
  // The card is brought up before MQTT because it also holds the cached runtime
  // settings, and those decide how the rest of this function behaves.
  static SdCard sdCard(config::kSdSpiHost, config::kSdPinMiso,
                       config::kSdPinMosi, config::kSdPinSclk, config::kSdPinCs,
                       config::kSdMountPoint);
  static FixQueue fixQueue(sdCard, config::kSdQueueFilePath,
                           config::kSdMaxQueuedFixes);
  if (config::kSdEnabled) {
    if (sdCard.begin() && fixQueue.begin()) {
      ESP_LOGI(TAG, "SD store-and-forward ready (%u fix(es) recovered).",
               (unsigned)fixQueue.size());
    } else {
      ESP_LOGW(TAG,
               "SD card unavailable - undeliverable fixes will be dropped.");
    }
  } else {
    ESP_LOGI(TAG, "SD store-and-forward disabled in Config.h.");
  }

  // Runtime settings: the last configuration the broker gave us, cached in the
  // clear on the card. Falls back to the Config.h defaults on a fresh device or
  // an unreadable card, so `settings` is always usable from here on.
  static SettingsStore settingsStore(sdCard, config::kSdSettingsFilePath);
  DeviceSettings       settings = settingsStore.load(DeviceSettings());

  // Bring up MQTT (if enabled). The client connects and reconnects in the
  // background, so we never block on it. Each fix is end-to-end encrypted by
  // PayloadCrypto before TelemetryPublisher hands it to the broker.
  static MqttClient     mqtt(config::kMqttBrokerUri, config::kMqttUsername,
                             config::kMqttPassword, config::kMqttClientId);
  static PayloadCrypto  crypto(config::kReceiverPublicKeyPem);
  static TelemetryPublisher publisher(mqtt, crypto, config::kTelemetryTopic,
                                      config::kDeviceId);
  static FixForwarder forwarder(publisher, mqtt, fixQueue,
                                config::kTelemetryTopic,
                                config::kMqttPublishAckTimeoutMs,
                                config::kSdMaxBurstFixes);
  static RemoteSettings remoteSettings(mqtt, settingsStore,
                                       config::kConfigTopic);

  if (config::kMqttEnabled) {
    // Subscribe before starting the client: the broker replays the retained
    // config the instant we connect, and that must not race the handler being
    // installed.
    remoteSettings.begin(settings);

    if (mqtt.begin()) {
      ESP_LOGI(TAG, "MQTT enabled; publishing fixes to %s.",
               config::kTelemetryTopic);
      // Give the broker a moment to connect and replay the retained config
      // before we commit to an interval for this cycle. A timeout here is
      // routine, not an error - we simply keep the cached settings.
      remoteSettings.waitForUpdate(config::kConfigFetchTimeoutMs);
      settings = remoteSettings.current();
    } else {
      ESP_LOGW(TAG, "MQTT failed to start; continuing without publishing.");
    }
  } else {
    ESP_LOGI(TAG, "MQTT disabled in Config.h.");
  }

  // Owns the ordered shutdown for the sleep_between path. Only ever used when
  // that setting is on, but wiring it here keeps the loop below free of the
  // details.
  static DeepSleepController sleeper(mqtt, wifi, gnss, sdCard,
                                     config::kModemPwrKeyPin,
                                     config::kWakeGpioPin,
                                     config::kWakeGpioLevel);

  ESP_LOGI(TAG, "GNSS ready. Reporting every %us (sleep between: %s).",
           (unsigned)settings.intervalSeconds(),
           settings.sleepBetweenSends() ? "yes" : "no");

  while (true) {
    GnssFix    fix;
    const bool haveFix =
        gnss.waitForFix(fix, config::kFixAcquireTimeoutSeconds * 1000,
                        config::kFixPollStepMs);

    // Timestamp the *capture*, not the publish. Anchoring the interval here is
    // what keeps the cadence steady: however long sealing, connecting and
    // waiting for the QoS-2 ack take, the next report still lands one interval
    // after this position was taken rather than drifting later every cycle.
    const int64_t fixCapturedUs = esp_timer_get_time();

    if (haveFix) {
      ESP_LOGI(TAG, "Fix: %.6f, %.6f  %.1f km/h", fix.position.latitudeDeg,
               fix.position.longitudeDeg, fix.speedKmph);

      // Hand the fix to the forwarder: it publishes it (plus any backlog) as
      // an encrypted burst when the broker is reachable, or stores it on the
      // SD card when it is not - so nothing is lost during an outage.
      if (config::kMqttEnabled) {
        forwarder.process(fix);
      }
    }

    // A config may have arrived while we were acquiring or publishing. Adopt it
    // now, so the interval and sleep decision just below already honour it.
    if (config::kMqttEnabled && remoteSettings.poll()) {
      settings = remoteSettings.current();
    }

    // Time left in this interval. With no fix there is nothing to measure from,
    // so we start a fresh interval here instead - otherwise a device that has
    // just burned kFixAcquireTimeoutSeconds finding no satellites would be
    // already "late" and would retry (or reboot) in a tight, battery-eating loop.
    const int64_t nowUs      = esp_timer_get_time();
    const int64_t anchorUs   = haveFix ? fixCapturedUs : nowUs;
    const int64_t intervalUs =
        static_cast<int64_t>(settings.intervalSeconds()) * 1000000LL;
    int64_t remainingUs = intervalUs - (nowUs - anchorUs);

    if (settings.sleepBetweenSends()) {
      // Power everything down and deep-sleep the rest of the interval. This does
      // not return: the chip reboots on wake and app_main() runs again from the
      // top, which is why every cycle re-reads the settings and re-subscribes.
      const int64_t minSleepUs =
          static_cast<int64_t>(config::kMinDeepSleepMs) * 1000LL;
      if (remainingUs < minSleepUs) {
        remainingUs = minSleepUs;
      }
      sleeper.sleepFor(static_cast<uint32_t>(remainingUs / 1000));
    }

    // Staying awake: keep the modem and the link up and just wait out the rest
    // of the interval.
    if (remainingUs > 0) {
      vTaskDelay(pdMS_TO_TICKS(static_cast<uint32_t>(remainingUs / 1000)));
    }
  }
}
