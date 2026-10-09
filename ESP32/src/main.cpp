
#include <cstdio>
#include <functional>

#include "config/Config.h"
#include "crypto/AckCrypto.h"
#include "crypto/PayloadCrypto.h"
#include "esp_log.h"
#include "esp_sleep.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "gnss/FixAverager.h"
#include "gnss/GnssModule.h"
#include "modem/Sim7000Modem.h"
#include "events/ActivityReporter.h"
#include "events/EventLog.h"
#include "motion/MotionTracker.h"
#include "mqtt/AckWatcher.h"
#include "mqtt/MqttClient.h"
#include "mqtt/PresenceReporter.h"
#include "mqtt/StatusPublisher.h"
#include "mqtt/TelemetryPublisher.h"
#include "mqtt/TelemetrySample.h"
#include "power/AdcSampler.h"
#include "power/BatteryMethods.h"
#include "power/BatteryMonitor.h"
#include "power/BatteryReporter.h"
#include "power/BatteryWindowSampler.h"
#include "power/BootJournal.h"
#include "power/ChargerWatcher.h"
#include "power/DeepSleepController.h"
#include "power/LowBatteryGuard.h"
#include "power/PowerSwitch.h"
#include "sensors/AccelPeakTracker.h"
#include "sensors/Adxl345.h"
#include "status/StatusLed.h"
#include "status/StatusLeds.h"
#include "sdcard/FixForwarder.h"
#include "sdcard/FixQueue.h"
#include "sdcard/RetryQueue.h"
#include "sdcard/SdCard.h"
#include "serial/SerialPort.h"
#include "settings/DeviceSettings.h"
#include "settings/RemoteSchedule.h"
#include "settings/RemoteSettings.h"
#include "settings/ScheduleStore.h"
#include "settings/SettingsApplier.h"
#include "settings/SettingsSelector.h"
#include "settings/SettingsStore.h"
#include "settings/UpdateSignal.h"
#include "util/DeviceClock.h"
#include "wifi/WifiManager.h"

static const char* TAG = "main";

// Pretty-print the onboard sensor readings to the serial console, styled to sit
// directly alongside GnssModule's GNSS/satellite debug blocks. Only called when
// config::kGnssDebug is on. Battery percent 0 is the "charging" sentinel, so it
// is spelled out rather than shown as a misleading flat 0 %. The raw pack
// millivolts are shown next to the percent purely as a calibration aid for the
// Li-ion curve - they are deliberately kept out of the published payload.
static void debugPrintSensors(const BatteryStatus& battery,
                              const AccelSample& accel) {
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
  printf("-----------------------------------------\n\n");
}

extern "C" void app_main(void) {
  // Undo the pin latches left behind by a previous deep sleep. This must happen
  // before any driver touches those pins: until it does, PWRKEY is frozen and
  // the modem could never be pulsed back on, and the power switch still reads
  // through the RTC pull that ext0 latched onto it. On a cold boot there is
  // nothing latched and all three are no-ops.
  DeepSleepController::releasePinHolds(config::kModemPwrKeyPin,
                                       config::kWakeGpioPin,
                                       config::kMotionWakePin);

  ESP_LOGI(TAG, "Car position tracker starting (wake cause: %s).",
           DeepSleepController::wakeCauseName());

  // ---- Checkpoint 0: is the operator asking for this at all? ---------------
  //
  // Before anything expensive - before the radio, the card, the modem or a cold
  // GNSS acquire - find out whether the power switch is even on. Every wake from
  // the switch is a full reboot, so without this check a single bouncing contact
  // would turn one flick into a burst of complete boot cycles.
  static PowerSwitch powerSwitch(
      config::kPowerSwitchEnabled ? config::kPowerSwitchPin : -1,
      config::kPowerSwitchRunLevel, config::kPowerSwitchDebounceMs);
  powerSwitch.begin();

  if (!powerSwitch.isRunRequested()) {
    // Switched off. The only thing that still needs dealing with is the modem,
    // which keeps its own power state across an ESP32 reset - so a brown-out or
    // a re-flash can land us here with it still running and drawing far more
    // than everything else put together.
    //
    // It is probed before it is touched. A blind PWRKEY pulse would switch an
    // already-off modem back ON, which is the exact opposite of what this path
    // is for, so isResponsive() decides. These two are locals, not statics: this
    // branch never returns, so there is nothing to outlive.
    ESP_LOGI(TAG, "Power switch is off - shutting down without starting up.");
    SerialPort    offSerial(config::kModemUartPort, config::kModemTxPin,
                            config::kModemRxPin, config::kModemBaudRate);
    Sim7000Modem  offModem(offSerial, config::kModemPwrKeyPin);
    if (offModem.begin() && offModem.isResponsive()) {
      ESP_LOGI(TAG, "Modem still running - powering it down first.");
      offModem.powerOff();
    }
    DeepSleepController::sleepUntilExternalWakeBare(config::kModemPwrKeyPin,
                                                    config::kWakeGpioPin,
                                                    config::kWakeGpioLevel);
  }

  // ---- The pack measurement, and the low-battery boot check ----------------
  //
  // Brought up here, ahead of WiFi and the modem, for one reason: the
  // low-battery guard has to decide whether this boot may run at all BEFORE
  // anything loads the rail. A device latched off for a flat pack that wakes on
  // its re-check timer and finds the pack still low goes straight back to sleep
  // from here, having spent a few tens of milliseconds and no radio time. The
  // sampling task itself is still started further down, where it always was.
  //
  // The single owner of the ESP32's ADC1 unit: the IDF refuses a second handle
  // on a unit that is already claimed, and two subsystems below need pins on it
  // (the monitor's charge sense, the measurement's pack sense). See
  // AdcSampler.h.
  static AdcSampler adcSampler;
  if (config::kBatteryEnabled || config::kBatteryReportFromMethods ||
      config::kLowBatteryCutoffEnabled) {
    if (!adcSampler.begin()) {
      ESP_LOGW(TAG, "ADC unavailable - battery readings will be omitted.");
    }
  }

  // The conversions behind the published percent, taken on their own task every
  // kBatteryWindowSampleMs. Started further down, as early as the rest of the
  // device exists, because the window is meant to cover the whole awake stretch
  // - the modem coming up, WiFi settling, the MQTT connect, the config fetch and
  // the entire fix hunt - rather than a couple of seconds guessed at just before
  // the publish. Every one of those is a moment the rail moves, and a median
  // wants to see all of them. See BatteryWindowSampler.h.
  static BatteryWindowSampler batteryWindow(adcSampler,
                                            config::kBatteryVbatSensePin,
                                            config::kBatteryWindowSampleMs);

  // The pack measurement itself: the window, outlier-trimmed and taken down to
  // its median, scored with the Li-ion curve. It measures only - it decides
  // nothing and stores nothing itself.
  static BatteryMethods batteryMethods(
      adcSampler, batteryWindow, config::kBatteryDividerRatio,
      config::kBatteryOutlierMadFactor, config::kBatteryNoReadingMv);

  // Claim the pack sense pin now, so the guard's spot reading below can use it.
  const bool windowReady =
      (config::kBatteryReportFromMethods || config::kLowBatteryCutoffEnabled) &&
      batteryWindow.begin();

  // Stops the tracker before the pack runs flat - see LowBatteryGuard.h. With
  // no usable pack reading it stays out of the way (fail open).
  static LowBatteryGuard lowBattery(
      batteryMethods, config::kLowBatteryCutoffEnabled && windowReady,
      config::kLowBatteryCutoffMv, config::kLowBatteryResumeMv);
  constexpr uint32_t kLowBatteryRecheckMs =
      config::kLowBatteryRecheckMinutes * 60UL * 1000UL;

  if (lowBattery.checkAtBoot() == LowBatteryGuard::BootVerdict::Hold) {
    // Latched off and not recovered: back to sleep without starting anything.
    // On our own re-check timer the modem is known to be off - the sleep that
    // latched us powered it down. Any other way in (a reset, a re-flash) gets
    // the same probe-before-touch treatment as checkpoint 0 above, because the
    // modem keeps its power state across an ESP32 reset and a blind PWRKEY pulse
    // would switch an off modem ON.
    if (esp_sleep_get_wakeup_cause() != ESP_SLEEP_WAKEUP_TIMER) {
      SerialPort   holdSerial(config::kModemUartPort, config::kModemTxPin,
                              config::kModemRxPin, config::kModemBaudRate);
      Sim7000Modem holdModem(holdSerial, config::kModemPwrKeyPin);
      if (holdModem.begin() && holdModem.isResponsive()) {
        ESP_LOGI(TAG, "Modem still running - powering it down first.");
        holdModem.powerOff();
      }
    }
    DeepSleepController::sleepForBare(kLowBatteryRecheckMs,
                                      config::kModemPwrKeyPin);
  }

  // WiFi. Constructed unconditionally - even a WiFi-disabled build hands it to
  // DeepSleepController, whose shutdown sequence must be able to stop the radio.
  // Only the bring-up below is gated on the feature flag; disconnect() on an
  // un-begun manager is a no-op.
  static WifiManager wifi(config::kWifiSsid, config::kWifiPassword,
                          config::kDeviceId, config::kWifiMaxRetries,
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

  // Status indicators. Constructed after WiFi because the green LED is driven by
  // polling WifiManager::linkState() - see StatusLeds.h for why that dependency
  // points this way round rather than WifiManager pushing events at a LED it
  // should know nothing about.
  static StatusLeds statusLeds(
      &wifi, config::kStatusLedsEnabled ? config::kGnssLedPin : -1,
      config::kStatusLedsEnabled ? config::kWifiLedPin : -1,
      config::kStatusLedActiveHigh, config::kStatusLedTickMs,
      config::kStatusLedBlinkMs);
  if (config::kStatusLedsEnabled) {
    statusLeds.begin();
    statusLeds.start();
  } else {
    ESP_LOGI(TAG, "Status LEDs disabled in Config.h.");
  }

  // microSD store-and-forward. When the broker cannot be reached, each fix is
  // sealed (same encrypted envelope as transmit) and appended to a queue file
  // on the card; once the link is back the backlog is drained in encrypted
  // bursts. The FixForwarder owns this decide-publish-or-store logic so the
  // loop below stays trivial. All optional (kSdEnabled): if the card is absent
  // the forwarder still publishes live fixes, it just cannot store missed ones.
  //
  // The card comes up before MQTT because it holds the cached runtime settings,
  // and those decide how the rest of this function behaves. It comes up before
  // the MODEM for a second reason: gnss.begin() below can fail and end the run
  // outright, and a boot that dies there is exactly the one worth recording - so
  // the journal has to be writable before we try.
  static SdCard sdCard(config::kSdSpiHost, config::kSdPinMiso,
                       config::kSdPinMosi, config::kSdPinSclk, config::kSdPinCs,
                       config::kSdMountPoint);
  static FixQueue fixQueue(sdCard, config::kSdQueueFilePath,
                           config::kSdMaxQueuedFixes);
  // Fixes the API rejected outright live in their own file, on a slow retry
  // schedule, so one permanently unacceptable fix cannot block the live queue.
  static RetryQueue retryQueue(sdCard, config::kSdRetryFilePath,
                               config::kSdMaxRetryEntries,
                               config::kRetryIntervalHours,
                               config::kRetryMaxAgeHours);
  // Status events (offline / wake / motion) said while the broker was out of
  // reach. The same queue class as the fixes, in a file of its own - see
  // EventLog. Unbegun, it simply refuses every append.
  static FixQueue eventQueue(sdCard, config::kSdEventQueueFilePath,
                             config::kSdMaxQueuedEvents);
  if (config::kSdEnabled) {
    if (sdCard.begin() && fixQueue.begin()) {
      retryQueue.begin();
      if (config::kStatusReportsEnabled) {
        eventQueue.begin();
      }
      ESP_LOGI(TAG, "SD store-and-forward ready (%u fix(es) recovered).",
               (unsigned)fixQueue.size());
    } else {
      ESP_LOGW(TAG,
               "SD card unavailable - undeliverable fixes will be dropped.");
    }
  } else {
    ESP_LOGI(TAG, "SD store-and-forward disabled in Config.h.");
  }

  // Why this device restarted, on the console and on the card. Placed as early
  // as the card allows: everything below can fail, and the failures that end the
  // run in seconds are the ones a boot log exists to catch.
  static BootJournal bootJournal(sdCard, config::kSdBootLogPath,
                                 config::kSdMaxBootLogLines,
                                 config::kBootLogPrintLines);
  if (config::kBootLogEnabled) {
    bootJournal.begin();  // prints the recent history, then persists this boot
  }

  // Runtime settings: the last configuration the broker gave us, cached in the
  // clear on the card. Falls back to the Config.h defaults on a fresh device or
  // an unreadable card, so `settings` is always usable from here on.
  static SettingsStore settingsStore(sdCard, config::kSdSettingsFilePath);
  DeviceSettings       settings = settingsStore.load(DeviceSettings());

  // The schedule this device switches itself on, cached beside the settings.
  // Loaded here rather than waiting for the broker for the same reason as the
  // settings, only more so: the schedule is what the device needs precisely when
  // it CANNOT reach the broker, so a bundle that only ever lived in RAM would be
  // gone by the first deep-sleep reboot.
  static ScheduleStore  scheduleStore(sdCard, config::kSdSchedulePath);
  static ScheduleBundle cachedSchedule = scheduleStore.load();

  // The wall clock. Restored from RTC memory if this is a wake rather than a
  // cold boot; otherwise it stays unseeded until the first GNSS fix below.
  static DeviceClock deviceClock(config::kClockTrustSeconds);
  deviceClock.begin();

  // Parked, checking or driving? Decided here, from the wake cause and RTC
  // memory, because it picks which of the two reporting sets the rest of this
  // boot runs - and with motion wake off it is inert and the standby set is the
  // only one. See MotionTracker.h for the state machine.
  static MotionTracker motion;
  motion.begin(settings);

  // Pushes the storage-related settings into the two queues. Applied here for
  // the cached document, and again every time a new one arrives, so the queues
  // are never running limits the server has already superseded.
  static SettingsApplier settingsApplier(fixQueue, retryQueue);
  settingsApplier.apply(settings);

  static SerialPort serial(config::kModemUartPort, config::kModemTxPin,
                           config::kModemRxPin, config::kModemBaudRate);
  static Sim7000Modem modem(serial, config::kModemPwrKeyPin);
  static GnssModule   gnss(modem);

  // Every acquire below goes through this rather than calling gnss.waitForFix()
  // directly: it acquires exactly as before, then throws that first (least
  // settled) fix away and hands back the average of the next few readings. See
  // FixAverager - with kFixAverageEnabled off it is literally waitForFix().
  static FixAverager averager(gnss);

  // Power up the modem, enable the GNSS engine and select the constellations.
  //
  // A failure no longer halts the firmware where it stands - that used to leave
  // the device awake, silent and draining the pack. Instead the rest of the
  // bring-up carries on (MQTT does not need the modem: it runs over WiFi), and
  // once the sleeper exists below the device reports an "error" status and
  // sleeps for kGnssInitRetrySleepMinutes before trying the whole boot again.
  const bool gnssReady = gnss.begin();
  if (!gnssReady) {
    ESP_LOGE(TAG, "GNSS init failed - check wiring and power. Will report the "
                  "error and retry after a sleep.");
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

  // Optional peak tracking: samples the accelerometer on its own small task and
  // keeps the per-axis maximum, so each report carries the strongest reading of
  // the interval rather than one arbitrary instant. Constructed unconditionally
  // (it is four words and a mutex); only the task is gated.
  static AccelPeakTracker accelPeak(accel, config::kAccelSampleIntervalMs);
  if (config::kAccelPeakEnabled) {
    if (!config::kAdxlEnabled) {
      ESP_LOGW(TAG,
               "kAccelPeakEnabled needs kAdxlEnabled - peak tracking not "
               "started.");
    } else if (!accelPeak.start()) {
      ESP_LOGW(TAG, "accelerometer peak tracking failed to start.");
    }
  }

  // The ADC unit, the pack window and its measurement were brought up right
  // after checkpoint 0, for the low-battery boot check - see there.
  static BatteryMonitor battery(adcSampler, modem,
                                config::kBatteryChargeSensePin,
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

  // Tested once here rather than re-derived every cycle. A device whose ADC
  // never came up still tracks and still publishes positions; it just leaves
  // battery_pct out (or falls back to the modem's own figure, below). The pin
  // was claimed early (windowReady); the sampling task starts here.
  bool methodsReady = false;
  if (config::kBatteryReportFromMethods) {
    methodsReady = windowReady && batteryWindow.start();
    if (!methodsReady) {
      ESP_LOGW(TAG,
               "Battery measurement unavailable - battery_pct will be "
               "omitted.");
    }
  }

  // Turns that measurement into the ONE percent the payload carries.
  // Constructed unconditionally - it holds no state - so the loop can call it
  // without re-testing the flag around every use.
  static BatteryReporter reporter;
  if (config::kBatteryReportFromMethods) {
    ESP_LOGI(TAG, "Publishing battery_pct from %s.", reporter.methodName());
  } else {
    ESP_LOGI(TAG, "Publishing battery_pct from the modem's AT+CBC curve.");
  }

  // Remembers the charger across cycles (and across deep-sleep reboots) so the
  // moment it comes off can be told from every cycle where it is simply absent.
  static ChargerWatcher chargerWatcher;

  // Bring up MQTT (if enabled). The client connects and reconnects in the
  // background, so we never block on it. Each fix is end-to-end encrypted by
  // PayloadCrypto before TelemetryPublisher hands it to the broker.
  static MqttClient     mqtt(config::kMqttBrokerUri, config::kMqttUsername,
                             config::kMqttPassword, config::kMqttClientId,
                             config::kMqttKeepaliveSeconds);
  static PayloadCrypto  crypto(config::kReceiverPublicKeyPem);
  static TelemetryPublisher publisher(mqtt, crypto, config::kTelemetryTopic,
                                      config::kDeviceId);
  // Opens the API's delivery acks. Constructed unconditionally so the forwarder
  // always has a collaborator to talk to; with kAckEnabled false it is simply
  // never subscribed, so waitForAck() always reports Unknown straight away.
  static AckCrypto  ackCrypto(config::kDeviceAckPrivateKeyPem);
  static AckWatcher ackWatcher(mqtt, ackCrypto, config::kAckTopic,
                               config::kDeviceId);
  static FixForwarder forwarder(publisher, mqtt, ackWatcher, fixQueue,
                                retryQueue, config::kTelemetryTopic,
                                config::kMqttPublishAckTimeoutMs,
                                config::kAckEnabled ? config::kAckTimeoutMs : 0,
                                config::kSdMaxBurstFixes,
                                config::kBacklogFlushRetryMs,
                                config::kBacklogFlushBudgetMs);
  // One event group shared by both retained-topic watchers, so the interval wait
  // below can block on a single object and still be woken by either. A task can
  // only wait on one event group, and a schedule bundle that had to sit unnoticed
  // for a whole reporting interval would defeat the point of an on-device
  // schedule.
  static UpdateSignal   updateSignal;
  static RemoteSettings remoteSettings(mqtt, settingsStore, updateSignal,
                                       config::kConfigTopic);
  static RemoteSchedule remoteSchedule(mqtt, scheduleStore, updateSignal,
                                       config::kScheduleTopic);

  // The device's own status messages - "online", "offline because ..." before
  // every sleep, each wake and each motion step - plus the Last Will the broker
  // publishes for us when we cannot. Sealed with the same crypto as the fixes;
  // everything but "online" goes through the event log, so out of range it
  // waits on the card. Constructed unconditionally (they hold a few pointers);
  // with kStatusReportsEnabled off nothing below ever calls them and the
  // sleeper is handed nullptr.
  static StatusPublisher statusPublisher(mqtt, crypto, deviceClock,
                                         config::kStatusTopic,
                                         config::kDeviceId,
                                         config::kStatusPublishTimeoutMs);
  static EventLog eventLog(mqtt, eventQueue, config::kStatusTopic,
                           config::kStatusPublishTimeoutMs,
                           config::kSdMaxBurstFixes,
                           config::kBacklogFlushRetryMs,
                           config::kBacklogFlushBudgetMs);
  static PresenceReporter presence(mqtt, statusPublisher, eventLog,
                                   BootJournal::resetCauseName());
  static ActivityReporter activity(statusPublisher, eventLog);

  // The one place that knows which of the three opinions about this device's
  // settings wins - see the banner on SettingsSelector.
  static SettingsSelector selector(remoteSettings, remoteSchedule, deviceClock,
                                   updateSignal);

  // Seeded here rather than inside the MQTT block below, because begin() does
  // two things: it arms the subscription AND it hands the watcher the document
  // loaded from the card. The selector reads the second of those on every
  // resolve, so with kMqttEnabled off it would otherwise see empty watchers and
  // hand back factory defaults instead of the cached configuration. Arming a
  // subscription on a client that is never started is free - MqttClient just
  // remembers it (see its subscribe()).
  //
  // Both must run BEFORE MqttClient::begin(): the broker replays the retained
  // documents the instant we connect, and that must not race the handlers being
  // installed. The ack subscription is armed for the same reason.
  remoteSettings.begin(settings);
  if (config::kScheduleEnabled) {
    remoteSchedule.begin(cachedSchedule);
  }

  if (config::kMqttEnabled) {
    if (config::kAckEnabled) {
      if (ackWatcher.begin()) {
        ESP_LOGI(TAG, "Delivery acks enabled; listening on %s.",
                 config::kAckTopic);
      } else {
        ESP_LOGW(TAG, "Delivery acks could not start - falling back to "
                      "broker-only confirmation.");
      }
    } else {
      ESP_LOGW(TAG,
               "Delivery acks disabled in Config.h - a fix is dropped once the "
               "BROKER acks it, even if the API never stored it.");
    }

    // The Last Will travels in CONNECT, so it is sealed and registered BEFORE
    // begin(). Sealed once per boot and replayed by esp-mqtt on every automatic
    // reconnect. A failure costs only the dead-man message, never the link.
    if (config::kStatusReportsEnabled) {
      std::string lastWill;
      if (statusPublisher.sealLastWill(lastWill) &&
          mqtt.setLastWill(config::kStatusTopic, lastWill)) {
        ESP_LOGI(TAG, "Last will armed on %s.", config::kStatusTopic);
      } else {
        ESP_LOGW(TAG, "Last will could not be armed - an unexpected drop will "
                      "not be reported.");
      }
    }

    if (mqtt.begin()) {
      ESP_LOGI(TAG, "MQTT enabled; publishing fixes to %s.",
               config::kTelemetryTopic);
      // Give the broker a moment to connect and replay the retained config
      // before we commit to an interval for this cycle. A timeout here is
      // routine, not an error - we simply keep the cached settings.
      if (!remoteSettings.waitForUpdate(config::kConfigFetchTimeoutMs)) {
        ESP_LOGI(TAG,
                 "no config from the broker within %ums - continuing with the "
                 "cached settings (is it published retained?)",
                 (unsigned)config::kConfigFetchTimeoutMs);
      }
      // Say we are here - and, on the first connection of this boot, why we
      // restarted. The retained config above arriving means the link is up.
      if (config::kStatusReportsEnabled) {
        presence.service();
      }
    } else {
      ESP_LOGW(TAG, "MQTT failed to start; continuing without publishing.");
    }
  } else {
    ESP_LOGI(TAG, "MQTT disabled in Config.h.");
  }

  // From here on every step of the motion state machine goes into the event
  // history. Two things happened before anyone could listen, and are recorded
  // by hand: the wake that started this boot, and the check MotionTracker began
  // with it. Recorded after "online" so a restart reason still leads the boot;
  // out of range they wait on the card like the rest.
  if (config::kMqttEnabled && config::kStatusReportsEnabled) {
    activity.recordWake();
    if (motion.isChecking()) {
      activity.recordMotion(MotionChange::Checking);
    }
    motion.setChangeHandler(
        [](MotionChange change) { activity.recordMotion(change); });
  }

  // First resolve of the run. Done outside the MQTT block so it also covers the
  // kMqttEnabled-off case, where it simply returns the documents loaded from the
  // card. The clock is almost certainly unseeded at this point on a cold boot,
  // so this usually resolves to the config document and the first real schedule
  // evaluation happens after the first fix below.
  settings = selector.resolve();
  motion.update(settings);
  settingsApplier.apply(settings);

  // Owns the ordered shutdown for the sleep_between path. Only ever used when
  // that setting is on, but wiring it here keeps the loop below free of the
  // details. The accelerometer is handed over for the motion wake, which it
  // arms as the last thing before a standby sleep.
  static DeepSleepController sleeper(
      mqtt, wifi, gnss, sdCard, config::kModemPwrKeyPin, config::kWakeGpioPin,
      config::kWakeGpioLevel, config::kAdxlEnabled ? &accel : nullptr,
      config::kMotionWakePin, config::kMotionWakeLowPowerSensor,
      (config::kMqttEnabled && config::kStatusReportsEnabled) ? &presence
                                                              : nullptr);

  // Shut down for a flat pack once the guard has called it - at boot (the
  // spot check found the pack already below the cut-off) or after a cycle's
  // reading. Every caller is a safe point: nothing is half-published. Does
  // not return when it acts. Everything else it touches is static, so the
  // re-check period is the only capture.
  std::function<void()> cutOffIfBatteryLow = [kLowBatteryRecheckMs]() {
    if (!lowBattery.cutoffPending()) {
      return;
    }
    statusLeds.allOff();
    lowBattery.latch();  // the next wake re-checks before starting anything
    sleeper.sleepForLowBattery(kLowBatteryRecheckMs);
  };

  // GNSS never came up (see above). Everything else did, so the device can
  // still say so before it sleeps and retries - unless the pack is the reason,
  // in which case "battery low" is the truer report and the longer sleep.
  if (!gnssReady) {
    if (config::kStatusReportsEnabled) {
      presence.service();
    }
    cutOffIfBatteryLow();
    statusLeds.allOff();
    sleeper.sleepAfterError(
        "gnss_init", config::kGnssInitRetrySleepMinutes * 60UL * 1000UL);
  }

  ESP_LOGI(TAG,
           "GNSS ready. Reporting every %us (sleep between: %s, settings v%u, "
           "config re-check every %us, motion wake: %s).",
           (unsigned)settings.intervalSeconds(),
           settings.sleepBetweenSends() ? "yes" : "no",
           (unsigned)settings.version(),
           (unsigned)settings.configCheckSeconds(),
           motion.enabled() ? MotionTracker::stateName(motion.state()) : "off");

  // The fix currently being polled. Hoisted out of the loop so the per-poll hook
  // below can read the UTC time of the poll that has just happened, and so the
  // flush at the top of each cycle still has the last known clock to schedule
  // retries with. Carrying it across iterations cannot leak a stale position
  // into a report: CgnsinfParser resets the struct on every successful read, and
  // nothing is published unless waitForFix() reported a fix from such a read.
  GnssFix fix;

  // THRESH_ACT the accelerometer's activity interrupt is currently armed with
  // while the device is AWAKE in standby, or 0 when it is not armed. Tracked so
  // the interval wait arms it once rather than every second, and re-arms it only
  // when the threshold changes or a trip has intervened.
  uint8_t activityArmedSteps = 0;

  // Hook run after every fix poll - fix or no fix, every kFixPollStepMs. It does
  // three things:
  //   1. Offers whatever is on the SD card to the broker. This is what frees the
  //      backlog from the position lock: a device that never gets one - parked
  //      in a garage, antenna unplugged - still empties its card within seconds
  //      of the link coming back, instead of sitting on it through a 3-minute
  //      acquire it will lose anyway. The call is a cheap no-op when there is
  //      nothing queued or the broker is unreachable.
  //   1b. Adopts a config that has arrived, FULLY: it takes the document, moves
  //      the loop's `settings` copy onto it and pushes it through the applier.
  //      Doing only the first of those three would leave the report built at the
  //      end of this cycle stamped with the previous revision - the device would
  //      be running the new settings while the dashboard still said "pending",
  //      until the report after this one. An acquire can last minutes, and the
  //      CPU is fully awake here driving the modem, so polling costs nothing and
  //      means a setting saved during the wait is in force - and *reported as
  //      in force* - by the time the report is built, rather than a whole cycle
  //      later. Runs on this same task, so current() still needs no locking.
  //
  //      The acquire already in flight keeps the fix budget it was started with:
  //      settings.fixTimeoutSeconds() was read by value when acquire() was
  //      called. That is deliberate - a shortened timeout should not truncate a
  //      wait that is already half spent and about to produce a lock; it takes
  //      effect from the next cycle.
  //   2. In debug builds only, prints the battery and accelerometer status
  //      beneath each satellite table while we wait - not just once the wait
  //      ends. Compiled out entirely when kGnssDebug is false, so production
  //      builds add no extra per-poll modem traffic.
  // Only `fix` and `settings` need capturing - every collaborator touched here
  // has static storage duration and is reachable without one. Both are locals of
  // app_main(), which never returns, so the references cannot dangle.
  //   3. Checks the power switch, and returns false to ABANDON the acquire when
  //      it has been turned off. That return is the whole reason this hook has a
  //      result: a fix budget can be up to an hour at its clamp, and without it
  //      a device switched off mid-hunt would sit there hunting anyway.
  std::function<bool()> onEachPoll = [&fix, &settings]() -> bool {
    // Asked first, and answered by abandoning everything else: if the operator
    // has switched the unit off there is no point seeding a clock, flushing a
    // backlog or re-resolving settings for a cycle that is about to end.
    if (config::kPowerSwitchEnabled && !powerSwitch.isRunRequested()) {
      return false;
    }

    // A reconnect during a long hunt is a new session the dashboard has not
    // heard about yet. Cheap when there is nothing new.
    if (config::kStatusReportsEnabled) {
      presence.service();
    }

    // Seed the clock from the poll that has just happened. Doing it here rather
    // than only after the acquire succeeds means the schedule starts being
    // evaluated the moment the receiver has a time, which on a cold boot can be
    // a minute or two before it has a position worth publishing.
    if (config::kScheduleEnabled) {
      deviceClock.seedFromGnss(fix.time);
    }

    if (config::kMqttEnabled) {
      forwarder.flushBacklog(fix);
    }

    // Re-resolved on every poll, not just when a document arrives: the clock
    // being seeded a moment ago can change the answer all by itself, with
    // nothing having been delivered.
    settings = selector.resolve();
    motion.update(settings);
    settingsApplier.apply(settings);

    if (config::kGnssDebug) {
      BatteryStatus batteryStatus;
      AccelSample   accelSample;
      if (config::kBatteryEnabled) {
        battery.read(batteryStatus);
      }
      if (config::kAdxlEnabled) {
        accel.read(accelSample);
      }
      debugPrintSensors(batteryStatus, accelSample);
    }

    return true;  // keep waiting for the fix
  };

  // How long one cycle lasts: the short check poll while looking for movement,
  // otherwise the report interval of whichever set is in force - standby or
  // moving. With motion wake off that is always the standby interval, i.e.
  // exactly what this loop used before motion wake existed.
  std::function<int64_t()> cycleIntervalUs = [&settings]() -> int64_t {
    if (motion.isChecking()) {
      return static_cast<int64_t>(config::kMotionCheckPollMs) * 1000LL;
    }
    return static_cast<int64_t>(
               motion.activeMode(settings).intervalSeconds()) *
           1000000LL;
  };

  // Whether this cycle ends in a deep sleep: the set in force says so - except
  // while checking, which never sleeps. The whole point of a check is to watch
  // the receiver, and sleeping would throw away the lock it has just acquired.
  std::function<bool()> sleepsBetween = [&settings]() -> bool {
    return !motion.isChecking() &&
           motion.activeMode(settings).sleepBetweenSends();
  };

  while (true) {
    // ---- Checkpoint 1: still switched on? -----------------------------------
    // Cheap, and it covers the case checkpoint 0 cannot: the switch flicked off
    // during the previous cycle's publish or interval wait. Everything is up by
    // now, so this takes the full shutdown path rather than the bare one.
    if (config::kPowerSwitchEnabled && !powerSwitch.isRunRequested()) {
      statusLeds.allOff();
      sleeper.sleepUntilExternalWake();
    }

    // Announce a connection made since the last pass, then act on a flat pack
    // found at boot or by the previous cycle - before spending an acquire.
    if (config::kStatusReportsEnabled) {
      presence.service();
    }
    cutOffIfBatteryLow();

    statusLeds.setGnss(StatusLed::Mode::Blink);

    // Out of awake standby - a check or a trip has begun, or motion wake has been
    // switched off: stand the activity interrupt down, so the next standby
    // re-arms it against a fresh at-rest reference rather than one taken before
    // the car moved.
    const bool inStandbyWatch =
        motion.enabled() && motion.state() == MotionTracker::State::Standby;
    if (activityArmedSteps != 0 && !inStandbyWatch) {
      accel.disarmActivity();
      activityArmedSteps = 0;
    }

    // Before committing to an acquire that may burn kFixAcquireTimeoutSeconds
    // and come back empty-handed, give anything already on the card its chance:
    // this is the first thing that runs after a cold boot or a deep-sleep wake,
    // when WiFi and MQTT have just been brought up above.
    if (config::kMqttEnabled) {
      forwarder.flushBacklog(fix);
    }

    // The acquire budget is a runtime setting: on a device that reports rarely
    // it is worth chasing a lock for minutes, while one reporting every 30 s
    // must give up quickly or it would never get to the wait at all. It comes
    // from whichever set is in force - standby or moving.
    //
    // While checking for movement it is also capped at what is left of the wake
    // window: a lock that arrives after the window has closed could not change
    // the decision any more, it would only keep the device awake. Never below
    // one poll step, so even the last moments of a window get a real look.
    //
    // `fix` comes back AVERAGED: the averager discards the fix the acquisition
    // produced and returns the mean of the readings that follow it. Everything
    // downstream - the payload, the copy stored on the card - is therefore
    // working from the same averaged position.
    uint32_t fixTimeoutMs = motion.activeMode(settings).fixTimeoutSeconds() * 1000;
    if (motion.isChecking()) {
      const int64_t windowMs = motion.msUntilDeadline();
      const int64_t capMs    = windowMs > static_cast<int64_t>(config::kFixPollStepMs)
                                   ? windowMs
                                   : static_cast<int64_t>(config::kFixPollStepMs);
      if (capMs < static_cast<int64_t>(fixTimeoutMs)) {
        fixTimeoutMs = static_cast<uint32_t>(capMs);
      }
    }
    bool haveFix = averager.acquire(fix, fixTimeoutMs, config::kFixPollStepMs,
                                    onEachPoll);

    // ---- Checkpoint 2: did the acquire end because we were switched off? ----
    // acquire() reports a caller-requested abort exactly as it reports a
    // timeout, so ask the switch rather than trying to tell the two apart from
    // its return value. Nothing is published: the fix, if there even is one, is
    // from a cycle the operator has already ended.
    if (config::kPowerSwitchEnabled && !powerSwitch.isRunRequested()) {
      statusLeds.allOff();
      sleeper.sleepUntilExternalWake();
    }

    // Solid for a lock, dark for a hunt that ran out of budget - the receiver is
    // no longer searching either way, so leaving it blinking would be a lie.
    statusLeds.setGnss(haveFix ? StatusLed::Mode::On : StatusLed::Mode::Off);

    // Timestamp the *capture*, not the publish. Anchoring the interval here is
    // what keeps the cadence steady: however long sealing, connecting and
    // waiting for the QoS-2 ack take, the next report still lands one interval
    // after this position was taken rather than drifting later every cycle. Not
    // const: an unplug retry below can supersede this capture, and the interval
    // has to be measured from whichever one we actually publish.
    int64_t fixCapturedUs = esp_timer_get_time();

    // ---- Sensors: once per cycle now, fix or no fix -------------------------
    // These used to run only for a fix we were about to publish. They run on
    // every cycle now, because the charger edge below can only be spotted by a
    // detector that actually ran each time. The debug callback above is a
    // separate, debug-only read that runs during the wait.

    // Score the pack. The conversions were taken by the sampling task while all
    // of the above was happening, so this neither blocks nor touches the ADC -
    // it drains the window and does the arithmetic.
    //
    // What still fixes where the call sits is that taking the window RESETS it.
    // It has to happen before forwarder.process() publishes, so the ~2 A
    // transmit droop of this cycle's own publish lands in the NEXT window rather
    // than in the one being scored. Everything else that used to pin this call
    // down - keeping it ahead of the monitor's AT+CBC, pausing first to let the
    // rail come back up after a backlog flush - was about a two-second burst
    // having to find a quiet moment. A window spanning the whole cycle does not
    // need one: the droops are in it, in the minority, where the outlier trim
    // can delete them.
    BatteryMethodsSample methods;
    if (methodsReady) {
      batteryMethods.sample(methods);
    }

    // fwBattery is BatteryMonitor's own verdict, and it is kept deliberately
    // SEPARATE from sample.battery further down, which is the published figure.
    // Two things still need it: it carries the charging flag the rest of this
    // cycle acts on - which is why it has to run before the charger edge below -
    // and it is the percent published when kBatteryReportFromMethods is off.
    BatteryStatus fwBattery;
    if (config::kBatteryEnabled) {
      battery.read(fwBattery);
    }

    // Has the charger just come off? Only the edge counts - see ChargerWatcher.
    const bool justUnplugged = chargerWatcher.update(fwBattery.charging);

    // The charger has just come off and this cycle has no position to attach the
    // news to. That first post-unplug reading is worth chasing: while the
    // charger was connected the pack was invisible to the ADC (the sense pin is
    // cut off from the cell on USB power), so this is the first moment the real
    // level exists at all - and a cycle without a fix publishes nothing, which
    // on a car parked indoors could mean never. So spend one more acquire on it.
    // If that also comes back empty, nothing is published: the alternative -
    // re-sending the last known position - would be dropped anyway, because the
    // API dedupes on (device, fix time) and keeps the row it already has.
    if (!haveFix && justUnplugged && config::kUnplugFixTimeoutSeconds > 0) {
      ESP_LOGI(TAG, "Charger off with no fix - one more acquire (%us).",
               (unsigned)config::kUnplugFixTimeoutSeconds);
      haveFix = averager.acquire(fix, config::kUnplugFixTimeoutSeconds * 1000,
                                 config::kFixPollStepMs, onEachPoll);
      if (haveFix) {
        // Re-anchor: the interval is measured from the capture, and the capture
        // is now this one rather than the empty acquire that preceded it.
        fixCapturedUs = esp_timer_get_time();
      } else {
        ESP_LOGI(TAG, "Still no fix - nothing published for the unplug.");
      }
    }

    // Assemble the report to forward: the position plus the optional onboard
    // sensors. Each read fills in its own `valid` flag; a disabled or failed
    // sensor simply leaves its slice absent from the published JSON.
    TelemetrySample sample;
    sample.gnss = fix;
    // Stamped from the settings in force at capture time, not at publish time -
    // see the note on TelemetrySample::settingsVersion. `settings` is current as
    // of the end of the acquire: the per-poll hook above adopts anything that
    // landed during it, so a config saved while we were chasing a lock is
    // reported by THIS cycle's report rather than the next one.
    sample.settingsVersion = settings.version();

    // Where the device's own schedule placed it at capture time, stamped from
    // the same instant and for the same reason. Both are left at their "not
    // scheduled" defaults when the selector is not running the schedule, and the
    // publisher then omits the pair entirely - which is exactly how the API
    // recognises a device that does not switch itself.
    sample.profileSlot     = selector.activeSlot();
    sample.scheduleVersion = selector.scheduleVersion();

    // The one battery figure that goes on the wire. BatteryReporter turns the
    // measurement above into it - or reports nothing at all rather than
    // guessing; see its banner for the three rules, and for why none of them
    // may emit -1.
    if (config::kBatteryReportFromMethods) {
      reporter.toStatus(methods, fwBattery.charging, sample.battery);
    } else {
      sample.battery = fwBattery;  // the pre-P4 behaviour, one flag away
    }

    // Say where this cycle's percent came from. Worth a line of its own: the
    // number has two possible sources and a third state (absent), and this line
    // is the only place that distinction is visible.
    if (!sample.battery.valid) {
      ESP_LOGI(TAG, "Battery: n/a (no reading this cycle).");
    } else if (sample.battery.charging) {
      ESP_LOGI(TAG, "Battery: charging (sentinel 0).");
    } else {
      ESP_LOGI(TAG, "Battery: %u %% (%s, %u mV).",
               (unsigned)sample.battery.percent,
               config::kBatteryReportFromMethods ? reporter.methodName()
                                                 : "AT+CBC",
               (unsigned)sample.battery.millivolts);
    }

    // The same reading, two more consumers: the low-battery guard decides on
    // the measured voltage (falling back to AT+CBC), and the presence reporter
    // keeps the published percent for the next goodbye. The guard only marks
    // the cut-off here; it is acted on after this cycle's report has gone out,
    // so the last position before a shutdown is not lost.
    lowBattery.evaluateCycle(methods, fwBattery);
    presence.noteBattery(sample.battery);

    // Does this fix go out? With motion wake off: whenever there is one, as
    // always. With it on, MotionTracker also uses the fix to move its state on -
    // a fast one starts or extends a trip - and holds back every fix of a check
    // but the first, so a parked car is reported once per wake, not every few
    // seconds while it is being watched. `publish` implies `haveFix`.
    const bool publish = motion.onFix(settings, haveFix, fix.speedKmph);
    if (haveFix && !publish) {
      ESP_LOGI(TAG, "Fix while checking for movement: %.1f km/h - not published.",
               fix.speedKmph);
    }

    if (publish) {
      if (config::kAdxlEnabled) {
        // With peak tracking on, report the strongest reading of the interval
        // that has just ended and start a fresh window. takePeak() returns false
        // on the very first cycle - nothing has been sampled yet - so fall
        // through to a live reading rather than publish an empty accel field.
        if (!config::kAccelPeakEnabled || !accelPeak.takePeak(sample.accel)) {
          accel.read(sample.accel);
        }
      }

      // Checkpoint this run in RTC memory so the NEXT boot's journal line can
      // say where it got to. Costs a couple of stores - no card write. The
      // charging path deliberately leaves millivolts unset (percent 0 is the
      // charging sentinel), so stamping the uptime alone is the honest answer
      // there rather than recording a confident 0 mV.
      //
      // Reads fwBattery, not sample.battery: the journal records the AT+CBC pack
      // VOLTAGE, which only the monitor produces, and which the published figure
      // no longer necessarily derives from.
      if (config::kBootLogEnabled) {
        if (fwBattery.valid && !fwBattery.charging) {
          bootJournal.noteBattery(fwBattery.millivolts);
        } else {
          bootJournal.noteUptime();
        }
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

    // This cycle's report is out (or queued on the card); if the reading above
    // put the pack below the cut-off, this is where the device stops.
    cutOffIfBatteryLow();

    // Catches the one window the per-poll hook cannot: a config that arrived
    // while we were sealing and publishing the sample. That one is genuinely
    // reported next cycle - this sample was captured under the older settings,
    // and back-dating it would be a lie - but the device must still start
    // honouring it now rather than after another whole interval.
    //
    // resolve() is a cheap no-op when the hook already took the document, and
    // apply() is a no-op when nothing changed, so this costs nothing on the
    // common path and is simpler than tracking who took the message.
    settings = selector.resolve();
    motion.update(settings);
    settingsApplier.apply(settings);

    // Close a motion window whose deadline has passed - a check that found no
    // movement, or a trip that has stood still for the whole stop window. Asked
    // only now, after this cycle's fix has had its say, so a fast fix landing
    // right at the deadline still counts. Nothing to re-apply when it closes:
    // the settings SettingsApplier pushes are shared by both modes.
    motion.evaluate();

    // Where this interval is measured from. With no report there is nothing to
    // measure from, so we start a fresh interval here instead - otherwise a
    // device that has just burned its whole acquire budget finding no satellites
    // would be already "late" and would retry (or reboot) in a tight,
    // battery-eating loop. A fix held back during a check is not a report, so it
    // does not anchor anything either.
    int64_t anchorUs = publish ? fixCapturedUs : esp_timer_get_time();

    // ...except right after a check has ended in standby: then the standby
    // interval runs from the moment the check BEGAN, i.e. from the wake, so a
    // parked car keeps its standby cadence however long each check took.
    motion.takeCheckAnchor(anchorUs);

    // Staying awake: wait out the rest of the interval, but *interruptibly*.
    //
    // The task is blocked the whole time, exactly as the plain delay this
    // replaces was - same tickless idle, same current draw - but the MQTT event
    // task can now wake it the instant a config lands. That is the difference
    // between a setting taking effect within a second and taking effect up to a
    // whole reporting interval later, and it costs nothing.
    //
    // The loop re-derives the remaining time from `anchorUs` on every pass, so a
    // config that changes interval_s re-times the cadence immediately: shorten
    // it past what has already elapsed and the next report goes out at once;
    // lengthen it and the wait simply extends. No extra acquire, no extra
    // airtime - the change is adopted, the rhythm is not disturbed.
    //
    // With motion wake the same holds for the set in force: a trip ending
    // mid-wait swaps the moving interval for the standby one, and the loop
    // re-times against it exactly as it would for a config change.
    while (config::kMqttEnabled) {
      // A reconnect while waiting is noticed here: it re-subscribes, the broker
      // replays the retained config, and that wakes the wait below at once - so
      // the new session is announced within a pass, not an interval later.
      if (config::kStatusReportsEnabled) {
        presence.service();
      }

      // A motion window may have run out while we waited. Closing it here, not
      // only after the next acquire, is what makes a trip end on time.
      if (motion.evaluate()) {
        motion.takeCheckAnchor(anchorUs);
      }

      const int64_t intervalUs  = cycleIntervalUs();
      const int64_t remainingUs = intervalUs - (esp_timer_get_time() - anchorUs);

      // Interval elapsed, or we have just been told to sleep - either way this
      // wait is over and the deep-sleep decision below is the freshest one.
      //
      // The threshold is a millisecond rather than zero because the wait below is
      // expressed in ticks: a sub-millisecond remainder rounds to "no wait at
      // all", and looping on it would spin the CPU until the clock caught up.
      if (remainingUs < 1000 || sleepsBetween()) {
        break;
      }

      // Never wait longer than the re-check period in one go, so that backstop
      // still fires on a device whose reporting interval is hours long. Each
      // chunk boundary is one wake-up and one SUBSCRIBE - the entire ongoing
      // cost of the periodic check.
      const int64_t checkUs =
          static_cast<int64_t>(settings.configCheckSeconds()) * 1000000LL;
      int64_t chunkUs = (checkUs > 0 && checkUs < remainingUs) ? checkUs
                                                              : remainingUs;

      // ...nor past the next scheduled profile switch. Without this a device
      // whose "Night" profile starts at 22:00 would carry on with its daytime
      // settings until whatever it was already waiting for expired - up to a
      // whole reporting interval late, and the dashboard's timeline would be
      // saying something that is not true of the device.
      int64_t untilSwitchS = 0;
      if (selector.secondsUntilNextChange(untilSwitchS)) {
        const int64_t untilSwitchUs = untilSwitchS * 1000000LL;
        if (untilSwitchUs < chunkUs) {
          chunkUs = untilSwitchUs;
        }
      }

      // ...nor past the next power-switch poll. Without this cap a device on an
      // hourly interval would keep running for up to an hour after being
      // switched off, because nothing else in this loop wakes to look. One extra
      // task wake-up a second is what makes the switch feel immediate, and it is
      // nothing against a radio that is still up.
      if (config::kPowerSwitchEnabled) {
        const int64_t pollUs =
            static_cast<int64_t>(config::kPowerSwitchPollMs) * 1000LL;
        if (pollUs < chunkUs) {
          chunkUs = pollUs;
        }
      }

      // ...nor past the end of a motion window, so the evaluate() at the top of
      // the next pass closes a check or a trip on time.
      const int64_t windowMs = motion.msUntilDeadline();
      if (windowMs >= 0 && windowMs * 1000LL < chunkUs) {
        chunkUs = windowMs * 1000LL;
      }

      // ...nor past the next look at the accelerometer, while AWAKE in standby
      // with motion wake on. Asleep, INT1 wakes the chip by itself; awake, nothing
      // listens to that pin, so the latch is polled instead. Armed once, at rest -
      // and re-armed only when the threshold changes or a trip has intervened.
      const bool watchActivity = config::kAdxlEnabled && motion.enabled() &&
                                 motion.state() == MotionTracker::State::Standby;
      if (watchActivity) {
        const uint8_t steps = settings.motion().thresholdSteps();
        if (activityArmedSteps != steps) {
          activityArmedSteps = accel.armActivity(steps, false) ? steps : 0;
        }
        const int64_t activityUs =
            static_cast<int64_t>(config::kMotionActivityPollMs) * 1000LL;
        if (activityArmedSteps != 0 && activityUs < chunkUs) {
          chunkUs = activityUs;
        }
      }

      // ---- Checkpoint 3: switched off while waiting out the interval? -------
      if (config::kPowerSwitchEnabled && !powerSwitch.isRunRequested()) {
        statusLeds.allOff();
        sleeper.sleepUntilExternalWake();
      }

      if (selector.waitForChange(static_cast<uint32_t>(chunkUs / 1000))) {
        settings = selector.current();
        motion.update(settings);
        settingsApplier.apply(settings);
        continue;  // re-time against the settings we have just adopted
      }

      // The car may have started moving while we waited. If so, the wait is
      // over: go straight into an acquire, which is where the check begins.
      if (watchActivity && activityArmedSteps != 0 && accel.takeActivity()) {
        motion.onActivity(settings);
        break;
      }

      // Nothing was delivered within the chunk - but the chunk may have ended
      // because a switch fell due, so re-resolve before deciding anything. This
      // is what actually performs the switch on a device that stays awake.
      const DeviceSettings previous = settings;
      settings                      = selector.resolve();
      motion.update(settings);
      settingsApplier.apply(settings);
      if (settings != previous) {
        continue;  // the schedule just moved us; re-time against the new interval
      }

      // Ask the broker to re-send the retained documents if the re-check is due;
      // it self-paces, so this is a no-op on a chunk that ended for any other
      // reason.
      selector.resyncIfDue(settings.configCheckSeconds());
    }

    // Deep sleep narrows what peak tracking can see: the chip is powered down
    // between reports, so the sampling task only runs during the awake window
    // (the acquire plus the publish), not across the whole interval. Worth
    // saying once - it is a surprising result, not a fault - but not worth
    // overriding the server's setting for.
    if (config::kAccelPeakEnabled && sleepsBetween()) {
      static bool warnedSleepingPeak = false;
      if (!warnedSleepingPeak) {
        ESP_LOGW(TAG,
                 "sleep_between is on: accel peaks only cover the awake part of "
                 "each cycle, not the full interval.");
        warnedSleepingPeak = true;
      }
    }

    if (sleepsBetween()) {
      // Power everything down and deep-sleep the rest of the interval. This does
      // not return: the chip reboots on wake and app_main() runs again from the
      // top, which is why every cycle re-reads the settings and re-subscribes.
      const int64_t intervalUs = cycleIntervalUs();
      const int64_t minSleepUs =
          static_cast<int64_t>(config::kMinDeepSleepMs) * 1000LL;
      int64_t remainingUs = intervalUs - (esp_timer_get_time() - anchorUs);

      // Cut the sleep short at the next profile switch, so a schedule boundary
      // is honoured to the minute instead of whenever the device next happened
      // to wake. On a device reporting hourly that is the difference between a
      // low-power profile starting at 22:00 and starting at 22:59.
      //
      // Be clear about the cost: waking here runs a WHOLE cycle - modem, GNSS
      // acquire, publish - not just a re-resolve, because app_main() restarts
      // from the top on every wake and has no notion of a partial one. So a
      // switch costs one extra report, not merely one extra wake.
      //
      // That is accepted rather than merely tolerated. The server verifies the
      // schedule from the profile slot inside each report, so the report this
      // produces is exactly the one that confirms the switch happened; without
      // it the switch would go unconfirmed until the next scheduled report. A
      // couple of switches a day is a couple of extra reports a day.
      int64_t untilSwitchS = 0;
      if (selector.secondsUntilNextChange(untilSwitchS)) {
        const int64_t untilSwitchUs = untilSwitchS * 1000000LL;
        if (untilSwitchUs < remainingUs) {
          remainingUs = untilSwitchUs;
        }
      }

      if (remainingUs < minSleepUs) {
        remainingUs = minSleepUs;
      }
      const uint32_t sleepMs = static_cast<uint32_t>(remainingUs / 1000);
      statusLeds.allOff();

      // Tell the next wake what it needs to know (the rest of a trip's stop
      // window), then sleep. In standby the accelerometer is armed as a second
      // wake source; on a trip it is not - a moving car would trip it at once,
      // turning every timed sleep into an instant reboot.
      motion.prepareForSleep(sleepMs);
      sleeper.sleepFor(sleepMs, motion.sleepWakeSteps(settings));
    }

    // MQTT disabled at compile time: there is no config to wait for, so fall
    // back to a plain delay for whatever is left of the interval.
    if (!config::kMqttEnabled) {
      const int64_t intervalUs  = cycleIntervalUs();
      const int64_t remainingUs = intervalUs - (esp_timer_get_time() - anchorUs);
      if (remainingUs > 0) {
        vTaskDelay(pdMS_TO_TICKS(static_cast<uint32_t>(remainingUs / 1000)));
      }
    }
  }
}
