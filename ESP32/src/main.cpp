
#include <cstdio>
#include <functional>

#include "config/Config.h"
#include "crypto/AckCrypto.h"
#include "crypto/PayloadCrypto.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "gnss/FixAverager.h"
#include "gnss/GnssModule.h"
#include "modem/ModemData.h"
#include "modem/Sim7000Modem.h"
#include "mqtt/AckWatcher.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "mqtt/TelemetrySample.h"
#include "power/AdcSampler.h"
#include "power/BatteryCsvLogger.h"
#include "power/BatteryMethods.h"
#include "power/BatteryMonitor.h"
#include "power/BatteryReporter.h"
#include "power/BootJournal.h"
#include "power/ChargerWatcher.h"
#include "power/DeepSleepController.h"
#include "sensors/AccelPeakTracker.h"
#include "sensors/Adxl345.h"
#include "sdcard/FixForwarder.h"
#include "sdcard/FixQueue.h"
#include "sdcard/RetryQueue.h"
#include "sdcard/SdCard.h"
#include "serial/SerialPort.h"
#include "settings/DeviceSettings.h"
#include "settings/RemoteSettings.h"
#include "settings/SettingsApplier.h"
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
  if (config::kSdEnabled) {
    if (sdCard.begin() && fixQueue.begin()) {
      retryQueue.begin();
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

  // The single owner of the ESP32's ADC1 unit: the IDF refuses a second handle
  // on a unit that is already claimed, and two subsystems below need pins on it
  // (the monitor's charge sense, the method log's pack + charge-input sense).
  // See AdcSampler.h.
  static AdcSampler adcSampler;
  if (config::kBatteryEnabled || config::kBatteryLogEnabled ||
      config::kBatteryReportFromMethods) {
    if (!adcSampler.begin()) {
      ESP_LOGW(TAG, "ADC unavailable - battery readings will be omitted.");
    }
  }

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

  // Every way this board can measure the pack, in one sweep. It has TWO
  // consumers now, which is why its bring-up is no longer tied to the CSV alone:
  // the diagnostic log below writes all of it to the card, and BatteryReporter
  // picks one of its columns to publish. It still measures only - it decides
  // nothing and stores nothing itself.
  static BatteryMethods batteryMethods(
      adcSampler, modem, config::kBatteryVbatSensePin,
      config::kBatterySolarSensePin, config::kBatteryDividerRatio,
      config::kSolarDividerRatio, config::kBatteryAdcSamples,
      config::kBatteryEmptyMv, config::kBatteryFullMv,
      config::kSolarInputThresholdMv, config::kBatteryNoReadingMv);
  static BatteryCsvLogger batteryCsv(sdCard, config::kSdBatteryLogPath,
                                     config::kSdMaxBatteryLogRows);

  // Two flags, because the two consumers can fail independently: a device with
  // no SD card still publishes a percent, and a device that publishes the older
  // AT+CBC figure still writes a full capture. The loop tests these rather than
  // re-deriving them every cycle.
  bool methodsReady = false;
  bool csvReady     = false;
  if (config::kBatteryLogEnabled || config::kBatteryReportFromMethods) {
    methodsReady = batteryMethods.begin();
    if (!methodsReady) {
      ESP_LOGW(TAG,
               "Battery methods unavailable - no CSV rows, and battery_pct "
               "will be omitted.");
    }
  }
  if (config::kBatteryLogEnabled) {
    csvReady = methodsReady && batteryCsv.begin();
    if (!csvReady) {
      ESP_LOGW(TAG,
               "Battery method log unavailable - no CSV rows will be written.");
    }
  } else {
    ESP_LOGI(TAG, "Battery method log disabled in Config.h.");
  }

  // Turns that sweep into the ONE percent the payload carries. Constructed
  // unconditionally - it is two enums and a name - so the loop can call it
  // without re-testing the flag around every use.
  static BatteryReporter reporter(
      static_cast<BatterySource>(config::kBatteryReportSourceIndex),
      static_cast<BatteryModel>(config::kBatteryReportModelIndex));
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
                             config::kMqttPassword, config::kMqttClientId);
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
  static RemoteSettings remoteSettings(mqtt, settingsStore,
                                       config::kConfigTopic);

  if (config::kMqttEnabled) {
    // Subscribe before starting the client: the broker replays the retained
    // config the instant we connect, and that must not race the handler being
    // installed. The ack subscription is armed here for the same reason.
    remoteSettings.begin(settings);
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
      settings = remoteSettings.current();
      settingsApplier.apply(settings);
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

  ESP_LOGI(TAG,
           "GNSS ready. Reporting every %us (sleep between: %s, settings v%u, "
           "config re-check every %us).",
           (unsigned)settings.intervalSeconds(),
           settings.sleepBetweenSends() ? "yes" : "no",
           (unsigned)settings.version(),
           (unsigned)settings.configCheckSeconds());

  // The fix currently being polled. Hoisted out of the loop so the per-poll hook
  // below can read the UTC time of the poll that has just happened, and so the
  // flush at the top of each cycle still has the last known clock to schedule
  // retries with. Carrying it across iterations cannot leak a stale position
  // into a report: CgnsinfParser resets the struct on every successful read, and
  // nothing is published unless waitForFix() reported a fix from such a read.
  GnssFix fix;

  // Hook run after every fix poll - fix or no fix, every kFixPollStepMs. It does
  // three things:
  //   1. Offers whatever is on the SD card to the broker. This is what frees the
  //      backlog from the position lock: a device that never gets one - parked
  //      in a garage, antenna unplugged - still empties its card within seconds
  //      of the link coming back, instead of sitting on it through a 3-minute
  //      acquire it will lose anyway. The call is a cheap no-op when there is
  //      nothing queued or the broker is unreachable.
  //   1b. Adopts a config that has arrived. An acquire can last minutes, and the
  //      CPU is fully awake here driving the modem, so polling costs nothing and
  //      means a setting saved during the wait is in force by the time the
  //      report is built rather than a whole cycle later. Runs on this same
  //      task, so current() still needs no locking.
  //   2. In debug builds only, prints the battery and accelerometer status
  //      beneath each satellite table while we wait - not just once the wait
  //      ends. Compiled out entirely when kGnssDebug is false, so production
  //      builds add no extra per-poll modem traffic.
  // Only `fix` needs capturing - every collaborator it touches has static
  // storage duration and is reachable without one.
  std::function<void()> onEachPoll = [&fix]() {
    if (config::kMqttEnabled) {
      forwarder.flushBacklog(fix);
      remoteSettings.poll();
    }

    if (config::kGnssDebug) {
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
    }
  };

  while (true) {
    // Before committing to an acquire that may burn kFixAcquireTimeoutSeconds
    // and come back empty-handed, give anything already on the card its chance:
    // this is the first thing that runs after a cold boot or a deep-sleep wake,
    // when WiFi and MQTT have just been brought up above.
    if (config::kMqttEnabled) {
      forwarder.flushBacklog(fix);
    }

    // The acquire budget is a runtime setting: on a device that reports rarely
    // it is worth chasing a lock for minutes, while one reporting every 30 s
    // must give up quickly or it would never get to the wait at all.
    //
    // `fix` comes back AVERAGED: the averager discards the fix the acquisition
    // produced and returns the mean of the readings that follow it. Everything
    // downstream - the CSV row, the payload, the copy stored on the card - is
    // therefore working from the same averaged position.
    bool haveFix = averager.acquire(fix, settings.fixTimeoutSeconds() * 1000,
                                    config::kFixPollStepMs, onEachPoll);

    // Timestamp the *capture*, not the publish. Anchoring the interval here is
    // what keeps the cadence steady: however long sealing, connecting and
    // waiting for the QoS-2 ack take, the next report still lands one interval
    // after this position was taken rather than drifting later every cycle. Not
    // const: an unplug retry below can supersede this capture, and the interval
    // has to be measured from whichever one we actually publish.
    int64_t fixCapturedUs = esp_timer_get_time();

    // ---- Sensors: once per cycle now, fix or no fix -------------------------
    // These used to run only for a fix we were about to publish. Two things need
    // them unconditionally now: the CSV wants a row whether or not there was a
    // lock, and the charger edge below can only be spotted by a detector that
    // actually ran on every cycle. The debug callback above is a separate,
    // debug-only read that runs during the wait.
    //
    // fwBattery is BatteryMonitor's own verdict, and it is kept deliberately
    // SEPARATE from sample.battery further down, which is the published figure.
    // The CSV's fw_* columns exist precisely to be compared against the other
    // methods, so feeding them anything but the monitor's own answer would empty
    // them of meaning.
    BatteryStatus fwBattery;
    if (config::kBatteryEnabled) {
      battery.read(fwBattery);
    }

    // Every way this board can measure the pack. Taken BEFORE the publish
    // because one of its columns is now what gets published, and called exactly
    // ONCE per cycle, because its trend detector's window is five *calls* - a
    // second call would quietly halve the span those columns cover.
    BatteryMethodsSample methods;
    if (methodsReady) {
      batteryMethods.sample(methods);
    }

    // Has the charger just come off? Only the edge counts - see ChargerWatcher.
    const bool justUnplugged = chargerWatcher.update(fwBattery.charging);

    // Diagnostic capture: one CSV row per cycle, fix or no fix, so a device
    // parked without a lock still leaves a discharge curve behind. Written right
    // next to the sweep it records, so a row's voltages and its fix come from
    // the same instant. Costs one line on the card and touches nothing that gets
    // published - see BatteryCsvLogger.
    if (csvReady) {
      batteryCsv.append(static_cast<uint32_t>(esp_timer_get_time() / 1000), fix,
                        methods, fwBattery);
    }

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
    // see the note on TelemetrySample::settingsVersion.
    sample.settingsVersion = settings.version();

    // The one battery figure that goes on the wire. BatteryReporter turns the
    // sweep above into it - or reports nothing at all rather than guessing; see
    // its banner for the three rules, and for why none of them may emit -1.
    if (config::kBatteryReportFromMethods) {
      reporter.toStatus(methods, fwBattery.charging, sample.battery);
    } else {
      sample.battery = fwBattery;  // the pre-P4 behaviour, one flag away
    }

    // Say where this cycle's percent came from. Worth a line of its own: the
    // number now has two possible sources and a third state (absent), and the
    // capture on the card is the only place to check it against.
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

    if (haveFix) {
      if (config::kAdxlEnabled) {
        // With peak tracking on, report the strongest reading of the interval
        // that has just ended and start a fresh window. takePeak() returns false
        // on the very first cycle - nothing has been sampled yet - so fall
        // through to a live reading rather than publish an empty accel field.
        if (!config::kAccelPeakEnabled || !accelPeak.takePeak(sample.accel)) {
          accel.read(sample.accel);
        }
      }
      if (config::kBatteryEnabled) {
        // The modem die temperature used to ride along with the battery read
        // (see the debug lambda above); that read has moved above this block, so
        // only the temperature is left here. Published as temp_c when it
        // succeeds, and only ever for a fix we are about to send.
        sample.modem.valid = modem.readTemperatureC(sample.modem.temperatureC);
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

    // A config may have arrived while we were publishing (the per-poll hook
    // above covers the acquire). Adopt whatever is current either way - apply()
    // is a no-op when nothing changed, so this is cheaper than tracking whether
    // it was the hook or us that took the message.
    if (config::kMqttEnabled) {
      remoteSettings.poll();
      settings = remoteSettings.current();
      settingsApplier.apply(settings);
    }

    // Where this interval is measured from. With no fix there is nothing to
    // measure from, so we start a fresh interval here instead - otherwise a
    // device that has just burned its whole acquire budget finding no satellites
    // would be already "late" and would retry (or reboot) in a tight,
    // battery-eating loop.
    const int64_t anchorUs = haveFix ? fixCapturedUs : esp_timer_get_time();

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
    while (config::kMqttEnabled) {
      const int64_t intervalUs =
          static_cast<int64_t>(settings.intervalSeconds()) * 1000000LL;
      const int64_t remainingUs = intervalUs - (esp_timer_get_time() - anchorUs);

      // Interval elapsed, or we have just been told to sleep - either way this
      // wait is over and the deep-sleep decision below is the freshest one.
      //
      // The threshold is a millisecond rather than zero because the wait below is
      // expressed in ticks: a sub-millisecond remainder rounds to "no wait at
      // all", and looping on it would spin the CPU until the clock caught up.
      if (remainingUs < 1000 || settings.sleepBetweenSends()) {
        break;
      }

      // Never wait longer than the re-check period in one go, so that backstop
      // still fires on a device whose reporting interval is hours long. Each
      // chunk boundary is one wake-up and one SUBSCRIBE - the entire ongoing
      // cost of the periodic check.
      const int64_t checkUs =
          static_cast<int64_t>(settings.configCheckSeconds()) * 1000000LL;
      const int64_t chunkUs =
          (checkUs > 0 && checkUs < remainingUs) ? checkUs : remainingUs;

      if (remoteSettings.waitForUpdate(static_cast<uint32_t>(chunkUs / 1000))) {
        settings = remoteSettings.current();
        settingsApplier.apply(settings);
        continue;  // re-time against the settings we have just adopted
      }

      // Nothing arrived within the chunk. Ask the broker to re-send the retained
      // document if the re-check is due; it self-paces, so this is a no-op on a
      // chunk that ended because the interval did.
      remoteSettings.resyncIfDue(settings.configCheckSeconds());
    }

    // Deep sleep narrows what peak tracking can see: the chip is powered down
    // between reports, so the sampling task only runs during the awake window
    // (the acquire plus the publish), not across the whole interval. Worth
    // saying once - it is a surprising result, not a fault - but not worth
    // overriding the server's setting for.
    if (config::kAccelPeakEnabled && settings.sleepBetweenSends()) {
      static bool warnedSleepingPeak = false;
      if (!warnedSleepingPeak) {
        ESP_LOGW(TAG,
                 "sleep_between is on: accel peaks only cover the awake part of "
                 "each cycle, not the full interval.");
        warnedSleepingPeak = true;
      }
    }

    if (settings.sleepBetweenSends()) {
      // Power everything down and deep-sleep the rest of the interval. This does
      // not return: the chip reboots on wake and app_main() runs again from the
      // top, which is why every cycle re-reads the settings and re-subscribes.
      const int64_t intervalUs =
          static_cast<int64_t>(settings.intervalSeconds()) * 1000000LL;
      const int64_t minSleepUs =
          static_cast<int64_t>(config::kMinDeepSleepMs) * 1000LL;
      int64_t remainingUs = intervalUs - (esp_timer_get_time() - anchorUs);
      if (remainingUs < minSleepUs) {
        remainingUs = minSleepUs;
      }
      sleeper.sleepFor(static_cast<uint32_t>(remainingUs / 1000));
    }

    // MQTT disabled at compile time: there is no config to wait for, so fall
    // back to a plain delay for whatever is left of the interval.
    if (!config::kMqttEnabled) {
      const int64_t intervalUs =
          static_cast<int64_t>(settings.intervalSeconds()) * 1000000LL;
      const int64_t remainingUs = intervalUs - (esp_timer_get_time() - anchorUs);
      if (remainingUs > 0) {
        vTaskDelay(pdMS_TO_TICKS(static_cast<uint32_t>(remainingUs / 1000)));
      }
    }
  }
}
