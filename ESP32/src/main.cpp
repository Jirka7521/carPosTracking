
#include <cstdio>
#include <functional>

#include "config/Config.h"
#include "crypto/PayloadCrypto.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "gnss/GnssModule.h"
#include "modem/ModemData.h"
#include "modem/Sim7000Modem.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "mqtt/TelemetrySample.h"
#include "power/BatteryMonitor.h"
#include "power/DeepSleepController.h"
#include "sensors/Adxl345.h"
#include "sdcard/FixForwarder.h"
#include "sdcard/FixQueue.h"
#include "sdcard/SdCard.h"
#include "serial/SerialPort.h"
#include "settings/DeviceSettings.h"
#include "settings/RemoteSettings.h"
#include "settings/SettingsStore.h"
#include "wifi/WifiManager.h"

static const char* TAG = "main";

// Pretty-print the onboard sensor readings to the serial console, styled to sit
// directly alongside GnssModule's GNSS/satellite debug blocks. Only called when
// config::kGnssDebug is on. Battery percent 0 is the "charging" sentinel, so it
// is spelled out rather than shown as a misleading flat 0 %. The raw pack
// millivolts are shown next to the percent purely as a calibration aid for the
// Li-ion curve - they are deliberately kept out of the published payload. The
// modem die temperature is shown too: it is the sensor that actually explains a
// hot-car cut-off.
static void debugPrintSensors(const BatteryStatus& battery,
                              const AccelSample& accel,
                              const ModemHealth& modem) {
  printf("---------------- SENSORS ----------------\n");
  if (!battery.valid) {
    printf("  Battery        : n/a (disabled / read failed)\n");
  } else if (battery.charging) {
    printf("  Battery        : charging (sentinel 0)\n");
  } else {
    printf("  Battery        : %u %% (%u mV)\n", (unsigned)battery.percent,
           (unsigned)battery.millivolts);
  }

  if (accel.valid) {
    printf("  Accel X/Y/Z    : %.2f / %.2f / %.2f g\n", accel.xG, accel.yG,
           accel.zG);
  } else {
    printf("  Accel X/Y/Z    : n/a (disabled / read failed)\n");
  }

  if (modem.valid) {
    printf("  Temperature    : %.1f C\n", modem.temperatureC);
  } else {
    printf("  Temperature    : n/a (unavailable)\n");
  }
  printf("-----------------------------------------\n\n");
}

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

  // Optional onboard sensors carried in every report: the ADXL345 accelerometer
  // (I2C) and the battery monitor (charge-sense ADC + modem AT+CBC). Both are
  // optional in the WiFi/SD sense - a failed bring-up logs a warning and tracking
  // continues, just without that field in the payload.
  static Adxl345 accel(config::kI2cSdaPin, config::kI2cSclPin,
                       config::kI2cClockHz, config::kAdxlI2cAddress);
  if (config::kAdxlEnabled) {
    if (accel.begin()) {
      ESP_LOGI(TAG, "ADXL345 accelerometer ready.");
    } else {
      ESP_LOGW(TAG, "ADXL345 unavailable - accel fields will be omitted.");
    }
  } else {
    ESP_LOGI(TAG, "ADXL345 disabled in Config.h.");
  }

  static BatteryMonitor battery(modem, config::kBatteryChargeSensePin,
                                config::kBatteryChargeAdcThreshold,
                                config::kBatteryEmptyMv, config::kBatteryFullMv);
  if (config::kBatteryEnabled) {
    if (battery.begin()) {
      ESP_LOGI(TAG, "Battery monitor ready.");
    } else {
      ESP_LOGW(TAG,
               "Battery monitor unavailable - battery field will be omitted.");
    }
  } else {
    ESP_LOGI(TAG, "Battery monitor disabled in Config.h.");
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

  // Debug-only hook run after every fix poll, so the battery and accelerometer
  // status print beneath each satellite table while we wait for a fix - not just
  // once the wait ends. Left empty in production builds (kGnssDebug == false),
  // so there is no extra per-poll modem traffic between reports.
  std::function<void()> reportSensors;
  if (config::kGnssDebug) {
    reportSensors = [&accel, &battery, &modem]() {
      BatteryStatus batteryStatus;
      AccelSample   accelSample;
      ModemHealth   modemHealth;
      if (config::kBatteryEnabled) {
        battery.read(batteryStatus);
        // Temperature rides along with the battery/health read (same modem, one
        // extra AT round-trip) rather than earning its own feature flag.
        modemHealth.valid = modem.readTemperatureC(modemHealth.temperatureC);
      }
      if (config::kAdxlEnabled) {
        accel.read(accelSample);
      }
      debugPrintSensors(batteryStatus, accelSample, modemHealth);
    };
  }

  while (true) {
    GnssFix    fix;
    const bool haveFix =
        gnss.waitForFix(fix, config::kFixAcquireTimeoutSeconds * 1000,
                        config::kFixPollStepMs, reportSensors);

    // Timestamp the *capture*, not the publish. Anchoring the interval here is
    // what keeps the cadence steady: however long sealing, connecting and
    // waiting for the QoS-2 ack take, the next report still lands one interval
    // after this position was taken rather than drifting later every cycle.
    const int64_t fixCapturedUs = esp_timer_get_time();

    // Assemble the report to forward: the position plus the optional onboard
    // sensors. Each read fills in its own `valid` flag; a disabled or failed
    // sensor simply leaves its slice absent from the published JSON. We read the
    // sensors here only for a fix we will actually publish - the debug callback
    // above is a separate, debug-only read that runs during the wait.
    TelemetrySample sample;
    sample.gnss = fix;

    if (haveFix) {
      if (config::kAdxlEnabled) {
        accel.read(sample.accel);
      }
      if (config::kBatteryEnabled) {
        battery.read(sample.battery);
        // The modem die temperature rides along with the battery read (see the
        // debug lambda above). Published as temp_c when the read succeeds.
        sample.modem.valid = modem.readTemperatureC(sample.modem.temperatureC);
      }

      ESP_LOGI(TAG, "Fix: %.6f, %.6f  %.1f km/h", fix.position.latitudeDeg,
               fix.position.longitudeDeg, fix.speedKmph);

      // Hand the sample to the forwarder: it publishes it (plus any backlog) as
      // an encrypted burst when the broker is reachable, or stores it on the
      // SD card when it is not - so nothing is lost during an outage.
      if (config::kMqttEnabled) {
        forwarder.process(sample);
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
