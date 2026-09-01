# Car Position Tracking — GNSS subsystem (ESP32 + SIM7000G)

Firmware for the **LilyGO TTGO T-SIM7000G** (ESP32-WROVER-B + SIMCom SIM7000G)
that reads the GNSS position from the modem's *integrated* GNSS receiver using
**all available constellations** (GPS, GLONASS, BeiDou, Galileo), returns
**position, speed and time**, and **publishes each fix — end-to-end encrypted —
to an MQTT broker** over a secure WebSocket (`wss://`).

The [desktop companion](../DESKTOP/README.md) subscribes to the broker and
decrypts the stream; the broker itself only ever sees ciphertext.

It is written in **C++** on top of **ESP-IDF** (built with **PlatformIO**) and
is split into small, single-purpose classes so it is easy to read, extend and
reuse.

---

## Features

- 📍 Read latitude, longitude, altitude, **speed** and **UTC time**.
- 🛰️ Uses GPS + GLONASS + BeiDou + Galileo together for a faster, better fix.
- 🎯 **Averaged positions**: every report is built from **four** readings — the
  noisy first fix after a lock is discarded and the next three are averaged — so
  a parked car stops wandering. See
  [Averaged position reports](#averaged-position-reports).
- 🧭 **ADXL345 accelerometer** (GY-291) over I2C: the raw X/Y/Z acceleration
  (in g) rides along with every position report — optionally as the **strongest
  per-axis reading of the whole interval** rather than one instantaneous sample,
  so braking and potholes between two reports are not missed.
- 🔋 **Battery monitor**: pack state of charge measured on the sense pin
  (GPIO35) and mapped through a **Li-ion discharge curve** (not a straight line),
  plus charge detection on that same pin — a value of `0` is the agreed
  "charging" sentinel. Also reports the **modem die temperature**
  (`AT+CPMUTEMP`, published as `temp_c`).
- 📊 **Battery method log** *(SD card)*: one CSV row per report comparing
  **every** way this board can measure the pack — 5 voltage sources × 3
  state-of-charge models, the modem's own percentage, the charge-input pin and
  three charging detectors — each row stamped with the **uptime in milliseconds**
  and the **current GNSS UTC**. **One** of those columns is the number that gets
  published (`p4_curve` by default); the rest exist so that choice can be checked
  against real captures. See [Battery method log](#battery-method-log).
- 🔌 **Charger-disconnect report**: while the charger is connected the pack is
  invisible to the ADC, so the first true reading of a trip only exists once it
  comes off. On that edge — and only when the cycle found no position — the
  firmware spends one more acquire chasing one, so the news does not wait for the
  next lock. See [Charger-disconnect report](#charger-disconnect-report).
- 🔋 **Power the whole modem off** between reads to minimise battery drain
  (plus a lighter "GNSS engine only" off switch).
- 📶 **Optional WiFi** (station mode): connect to a network with one flag, or
  disable it entirely. Credentials are kept out of Git.
- 📡 **Optional MQTT publishing**: each fix is pushed to a broker over secure
  WebSocket (`wss://`), with automatic background (re)connection.
- 🔒 **End-to-end encryption** of every payload (RSA-OAEP-SHA256 + AES-256-GCM):
  only the holder of the receiver's private key can read a position — the broker
  cannot.
- 💾 **microSD store-and-forward**: a fix the broker doesn't acknowledge is
  saved — *already encrypted* — to a queue file on the SD card and re-sent in an
  encrypted **burst** once the link returns. Nothing is lost during an outage,
  and only ciphertext ever touches the card. The backlog goes out **as soon as
  the link is back, with or without a current position lock** — a car parked in
  a garage still empties its card.
- ⚙️ **Remote settings over MQTT**: the reporting interval, the
  power-down-between-reports flag, the GNSS lock timeout, the undelivered-fix cap,
  the retry policy and the settings re-check interval are all pushed from the
  broker on a retained config topic, validated, clamped, and cached on the SD card
  so they survive a reboot with no network. An **awake device applies a change
  within a second** — it blocks on an event group that the arriving message itself
  signals, so the responsiveness costs no extra power. Each document carries a
  revision number the device echoes back in every report, so the dashboard can
  tell a published change from an applied one.
- 😴 **Deep sleep between reports**: when told to, the firmware powers the modem
  right down (GNSS engine, antenna amplifier and LTE PA all go with it) and puts
  the ESP32 into deep sleep for the rest of the interval.
- 🧾 **Boot log**: one line per boot on the SD card — reset reason, boot counter,
  free heap — and the recent history printed to serial at start-up, so an
  unexplained restart can be diagnosed after the fact. Because RTC memory
  survives a reset but not a lost rail, it separates **"it crashed"** from
  **"it lost power"**, which the serial console alone never could.
- 🐛 **GNSS debug mode** (one flag in the config file): prints every value read
  from the module *and* how many satellites of each constellation are in view.
- 🧱 Clean class-per-file structure, heavily commented.

---

## Project layout

```
src/
├── main.cpp                 ← Example app: wires the classes together & loops
│
├── config/
│   ├── Config.example.h     ← Committed template (NO secrets) — copy to Config.h
│   └── Config.h             ← ★ EDIT ME (git-ignored): pins, WiFi creds, flags
│
├── serial/
│   ├── SerialPort.h/.cpp    ← Thin wrapper over the ESP-IDF UART driver
│
├── modem/
│   ├── Sim7000Modem.h/.cpp  ← Modem power (PWRKEY) + AT command transport
│
├── wifi/
│   ├── WifiManager.h/.cpp   ← Optional WiFi station: begin / connect / status
│
├── gnss/
│   ├── GnssData.h           ← Plain data structs (GnssFix, GnssSatelliteCounts…)
│   ├── CgnsinfParser.h/.cpp ← Decodes the AT+CGNSINF position reply
│   ├── NmeaParser.h/.cpp    ← Counts satellites per constellation (NMEA GSV)
│   ├── GnssModule.h/.cpp    ← ★ The high-level API you call from your code
│   └── FixAverager.h/.cpp   ← Drop the first fix, publish the mean of the next 3
│
├── crypto/
│   ├── PayloadCrypto.h/.cpp ← Hybrid RSA-OAEP + AES-256-GCM payload encryption
│   └── AckCrypto.h/.cpp     ← The mirror image: opens the API's encrypted acks
│
├── sensors/
│   ├── AccelData.h             ← Plain AccelSample struct (X/Y/Z in g)
│   └── Adxl345.h/.cpp          ← I2C driver for the ADXL345 accelerometer
│
├── mqtt/
│   ├── MqttClient.h/.cpp        ← Broker transport (esp-mqtt over wss/TLS)
│   ├── TelemetrySample.h        ← Aggregate: position + battery + accel
│   ├── TelemetryPublisher.h/.cpp← Sample → JSON → encrypt (seal/publish)
│   └── AckWatcher.h/.cpp       ← Did the API actually store it? (per-envelope)
│
├── sdcard/
│   ├── SdCard.h/.cpp        ← Mount/format the card + line-oriented file IO
│   ├── FixQueue.h/.cpp      ← Persistent FIFO of encrypted envelopes on the card
│   ├── RetryQueue.h/.cpp    ← Rejected fixes, re-offered on a slow schedule
│   └── FixForwarder.h/.cpp  ← Publish-now-or-store; flush the backlog, lock or not
│
├── settings/
│   ├── DeviceSettings.h/.cpp ← The two runtime knobs, validated & clamped
│   ├── SettingsCodec.h/.cpp  ← DeviceSettings ⇄ the config JSON (one format)
│   ├── SettingsStore.h/.cpp  ← Cache them, in the clear, on the SD card
│   └── RemoteSettings.h/.cpp ← Subscribe to the config topic; apply & persist
│
└── power/
    ├── AdcSampler.h/.cpp          ← The one owner of ADC1: raw counts + calibrated mV
    ├── BatteryData.h              ← Plain BatteryStatus struct (percent + charging)
    ├── BatteryMonitor.h/.cpp      ← Charge-sense on GPIO35 + the AT+CBC fallback %
    ├── BatteryMethodsData.h       ← Plain struct: one multi-method measurement
    ├── BatteryMethods.h/.cpp      ← Measure the pack every way at once (diagnostic)
    ├── BatteryCsvLogger.h/.cpp    ← One CSV row per report, on the card
    ├── BatteryReporter.h/.cpp     ← Picks the ONE percent that goes on the wire
    ├── ChargerWatcher.h/.cpp      ← Spots the charger-off edge (RTC-backed)
    ├── BootJournal.h/.cpp         ← Why this device restarted: one line per boot
    └── DeepSleepController.h/.cpp ← Ordered shutdown + wake sources + deep sleep
```

### How the layers fit together

```
        ┌─────────────────────────────┐
        │          main.cpp           │   your application
        └───────┬─────────────────┬───┘
          uses  │                 │ uses
   ┌────────────▼────────┐        │
   │     FixAverager     │        │  average away the noisy
   │ acquire → mean of 3 │        │  first fix after a lock
   └────────────┬────────┘        │
          uses  │                 │
   ┌────────────▼────────┐  ┌─────▼──────────────┐
   │      GnssModule     │  │    FixForwarder    │  publish now, or store
   │ begin/readFix/power…│  │ process / flush    │  & flush without a lock
   └───────┬─────────┬───┘  └──┬──────┬───────┬──┘
    uses   │         │ uses    │ uses │ uses  │ uses
   ┌───────▼──────┐ ┌▼─────────▼──┐ ┌─▼──────────────┐ ┌──────────────┐
   │ Sim7000Modem │ │CgnsinfParser│ │TelemetryPublish│ │   FixQueue   │
   │ power + AT   │ │NmeaParser   │ │ er (seal fix)  │ │ SD FIFO      │
   └───────┬──────┘ └─────────────┘ └──┬──────────┬──┘ └──────┬───────┘
    uses   │                    uses   │          │ uses      │ uses
   ┌───────▼──────┐          ┌─────────▼──────┐ ┌─▼──────────┐ │
   │  SerialPort  │          │ PayloadCrypto  │ │ MqttClient │ │
   │  UART bytes  │          │ RSA-OAEP+GCM   │ │ esp-mqtt   │ │
   └──────────────┘          └────────────────┘ │ QoS-2 ack  │ │
                                                 └────────────┘ │
                                              ┌─────────────────▼┐
                                              │      SdCard       │  FAT on µSD
                                              │  mount + file IO  │
                                              └───────────────────┘
```

Each class has exactly one job, which makes the system easy to follow and to
test:

| Class | Responsibility |
|-------|----------------|
| `SerialPort` | Send/receive bytes over a UART; read a line. Nothing else. |
| `Sim7000Modem` | Turn the modem on/off (PWRKEY) and run AT commands. |
| `WifiManager` | Optional WiFi station: init the stack, connect, report status. |
| `CgnsinfParser` | Turn one `+CGNSINF:` line into a `GnssFix`. |
| `NmeaParser` | Count satellites per constellation from `GSV` sentences. |
| `GnssModule` | The friendly API: configure, read a fix, manage power, debug. |
| `FixAverager` | Discard the first fix after a lock; publish the mean of the next three readings. |
| `Adxl345` | I2C driver: configure the ADXL345 and return one X/Y/Z sample (g). |
| `AdcSampler` | The single owner of ADC1: claims pins, serves raw counts and calibrated millivolts. |
| `BatteryMonitor` | Charging detection (GPIO35, via `AdcSampler`) — the single source of that verdict — plus the fallback pack % (Li-ion curve over the modem's `AT+CBC`). |
| `BatteryMethods` | Measure the pack five ways, score each with three models, and report the spread. |
| `BatteryCsvLogger` | *Diagnostic:* write one of those measurements per report as a CSV row on the card. |
| `BatteryReporter` | Turn one of those measurements into the single `battery_pct` the payload carries — sentinel, floor, or absent. |
| `ChargerWatcher` | Remember the charger across cycles (and deep sleeps) and report the moment it comes off. |
| `BootJournal` | Record *why* the device restarted — reset reason, boot counter, whether RTC memory survived. |
| `PayloadCrypto` | Seal a plaintext string into the encrypted JSON envelope (and stamp its `id`). |
| `AckCrypto` | The inverse: open an ack sealed to this device's own private key. |
| `MqttClient` | Connect to the broker (esp-mqtt/TLS); publish, subscribe, confirm QoS-2 delivery. |
| `TelemetryPublisher` | Format a `TelemetrySample` as JSON and encrypt it (`sealSample`); publish one. |
| `AckWatcher` | Collect the API's per-envelope verdicts; answer "was this fix actually stored?". |
| `SdCard` | Mount/format the microSD (FAT) and read/append/trim/filter files — every helper streams, one line at a time. |
| `FixQueue` | Persistent FIFO of encrypted envelopes on the card (with a size cap). |
| `RetryQueue` | Fixes the API rejected, with a next-attempt time and a give-up age. |
| `FixForwarder` | Publish a fix (plus any backlog) or store it; flush the card as a burst whenever the link is up — no position lock needed. |
| `DeviceSettings` | Hold a *valid* interval + sleep flag; clamp anything out of range. |
| `SettingsCodec` | The one definition of the config JSON, for both the wire and the card. |
| `SettingsStore` | Cache the settings on the card; fall back to defaults when unreadable. |
| `RemoteSettings` | Subscribe to the config topic; validate, apply and persist what arrives. |
| `DeepSleepController` | Quiesce MQTT/WiFi/modem/card in order, arm the wake sources, sleep. |

---

## Configuration

> ⚠️ **First-time setup — create your config file.**
> [`Config.h`](src/config/Config.h) holds your private WiFi and MQTT credentials
> and is **git-ignored** so it never reaches GitHub. The repository only ships
> the credential-free template [`Config.example.h`](src/config/Config.example.h).
> Before your first build, copy it and fill it in:
>
> ```bash
> cp src/config/Config.example.h src/config/Config.h
> ```
>
> Then edit `Config.h` and set at least `kWifiSsid` / `kWifiPassword`,
> `kMqttPassword`, and `kReceiverPublicKeyPem` (see
> [MQTT & end-to-end encryption](#mqtt--end-to-end-encryption) below). **Never
> commit `Config.h`** — keep real secrets only in the untracked copy.

> 💡 **Or let the dashboard write it for you.** A device's *Settings → Firmware
> configuration* tab renders a **complete `Config.h`** for that tracker — its id,
> topics, broker URI, receiver public key and current remote settings already
> filled in — with fields for your WiFi/MQTT secrets and a button that mints the
> delivery-ack key pair in your browser. Download it straight to
> `src/config/Config.h` and run `pio run`; nothing needs merging by hand. The
> secrets and the ack private key are spliced in locally and never reach the
> server. The same tab also lists every constant below, read-only.
>
> **Add, remove or rename a constant here and everything downstream follows by
> itself.** The API embeds this exact file — an MSBuild target stages a verbatim
> copy into
> [`API/CarPosAPI/Services/Provisioning/ConfigTemplate.h.txt`](../API/CarPosAPI/Services/Provisioning/ConfigTemplate.h.txt)
> on every build, because the API image is built with `../../API` as its Docker
> context and cannot read `ESP32/` — and rewrites the per-device constants in it
> *by name*, so one it does not name simply passes through with the value you set
> here. The dashboard's reference table is parsed out of that same rendered file.
> The one thing to remember: that staged copy is **committed**, so build the API
> (`dotnet build` in `API/`) in the same change and commit the refreshed file with
> it — warning `CARPOS001` says so if you forget, and the Docker build cannot
> regenerate it.

Everything tunable lives in [`src/config/Config.h`](src/config/Config.h):

| Setting | Default | Meaning |
|---------|---------|---------|
| `kModemUartPort` | `UART_NUM_1` | UART used for the modem (UART0 is the console) |
| `kModemTxPin` / `kModemRxPin` | `27` / `26` | T-SIM7000G factory pins |
| `kModemBaudRate` | `9600` | SIM7000G default; tried first at start-up |
| `kModemBaudCandidates` | `115200, 9600, 57600, 38400, 19200` | Fallback rates probed if the modem stays silent |
| `kModemPwrKeyPin` | `4` | PWRKEY (power on/off) |
| `kModemPwrKeyActiveLow` | `true` | PWRKEY polarity: `true` = idles HIGH, pulses LOW (most boards) |
| `kEnableGps/Glonass/Beidou/Galileo` | all `true` | Constellations to use |
| **`kGnssDebug`** | `true` | **Turn the serial debug report on/off** |
| `kSatelliteScanMs` | `3000` | How long to listen to NMEA when counting sats |
| `kFixAcquireTimeoutSeconds` | `180` | **Default** for `fix_timeout_s` — give up waiting for a fix after this long |
| `kFixPollStepMs` | `2000` | Gap between `AT+CGNSINF` polls while acquiring |
| **`kFixAverageEnabled`** | `true` | **Publish the average of several readings instead of one raw fix** (see below) |
| `kFixAverageSampleCount` | `3` | Readings averaged after the discarded first one — also the exact number of reads attempted |
| `kFixAverageStepMs` | `1000` | Gap between those readings; do not go below the receiver’s 1 Hz solve rate |
| **`kAdxlEnabled`** | `true` | **Enable/disable the ADXL345 accelerometer** |
| `kI2cSdaPin` / `kI2cSclPin` | `21` / `22` | I2C data / clock GPIOs |
| `kI2cClockHz` | `400000` | I2C bus speed (fast mode) |
| `kAdxlI2cAddress` | `0x53` | ADXL345 address (CS→3V3, SDO→GND) |
| `kAdxlInt1Pin` / `kAdxlInt2Pin` | `32` / `33` | INT pins — reserved, interrupts not used yet |
| **`kAccelPeakEnabled`** | `false` | **Report the strongest per-axis reading of the interval instead of one instantaneous sample** (see below) |
| `kAccelSampleIntervalMs` | `1000` | How often the sensor is sampled while peak tracking is on |
| **`kBatteryEnabled`** | `true` | **Enable/disable the battery monitor** |
| `kBatteryChargeSensePin` | `35` | Charge-sense ADC pin; reads ~0 while charging |
| `kBatteryChargeAdcThreshold` | `200` | Raw ADC counts below which = charging (report `0`) |
| `kBatteryEmptyMv` / `kBatteryFullMv` | `3300` / `4200` | Clamp ends of the Li-ion SoC curve (≤empty→1 %, ≥full→100 %) |
| **`kBatteryLogEnabled`** | `true` | **Enable/disable the battery method log** (see [Battery method log](#battery-method-log)) |
| `kSdBatteryLogPath` | `/sdcard/battery.csv` | The CSV (**plaintext**); a header mismatch rotates to `battery2.csv` … `battery9.csv` |
| `kSdMaxBatteryLogRows` | `20000` | Cap on data rows (header excluded); oldest are dropped past this. `0` = no cap |
| `kBatteryVbatSensePin` | `35` | Pack voltage sense — the **same pin** as `kBatteryChargeSensePin`, read as a voltage here |
| `kBatterySolarSensePin` | `36` | Charge-input (solar/VIN) sense |
| `kBatteryDividerRatio` / `kSolarDividerRatio` | `2.0f` / `2.0f` | On-board divider ratios; the solar one is an assumption that varies by board revision |
| `kBatteryAdcSamples` | `16` | ADC conversions per measurement (averaged **and** medianed) |
| `kSolarInputThresholdMv` | `1000` | Above this on GPIO36, a charge source is present |
| `kBatteryNoReadingMv` | `2000` | Below this the ADC path is logged as absent, not as a flat pack |
| **`kBatteryReportFromMethods`** | `true` | **Publish one of the method-log columns as `battery_pct`.** `false` goes back to `BatteryMonitor`'s own `AT+CBC` figure |
| `kBatteryReportSourceIndex` | `3` | Which voltage source to publish — a `BatterySource` index; `3` = `kSourceCalMedian`, the CSV's `p4_*` |
| `kBatteryReportModelIndex` | `1` | Which model to score it with — a `BatteryModel` index; `1` = `kModelCurve`, the CSV's `*_curve` |
| `kUnplugFixTimeoutSeconds` | `60` | Extra acquire budget when the charger comes off and the cycle found no position. `0` disables it |
| **`kWifiEnabled`** | `true` | **Enable/disable WiFi entirely** |
| `kWifiSsid` / `kWifiPassword` | — | **Your WiFi credentials (secret)** |
| `kWifiConnectTimeoutMs` | `15000` | Max wait for an IP before giving up |
| `kWifiMaxRetries` | `5` | Fast retries in a burst before it's deemed failed |
| `kWifiReconnectIntervalMs` | `30000` | Background reconnect interval after a failed burst |
| **`kMqttEnabled`** | `true` | **Enable/disable MQTT publishing entirely** |
| `kMqttBrokerUri` | — | Full broker URI; scheme sets transport + TLS (`wss://…`) |
| `kMqttUsername` | `admin` | Broker login name (not secret) |
| `kMqttPassword` | — | **Broker login password (secret)** |
| `kMqttClientId` | `GNSSXX` | Client id shown in the broker's logs |
| `kDeviceId` | `GNSSXX` | Device id placed inside each payload |
| `kTelemetryTopic` | `devices/GNSSXX` | Topic each fix is published to |
| `kMqttPublishAckTimeoutMs` | `8000` | How long to wait for the broker's QoS-2 delivery ack |
| `kReceiverPublicKeyPem` | — | **Receiver's RSA public key** (encrypts the payload) |
| `kConfigTopic` | `devices/GNSSXX/config` | Topic the **retained** settings message is read from |
| `kConfigFetchTimeoutMs` | `8000` | Wait for the retained config (covers connect + TLS) |
| `kDefaultConfigCheckSeconds` | `3600` | **Default** for `config_check_s` — awake-mode re-check interval |
| `kMinConfigCheckSeconds` / `kMaxConfigCheckSeconds` | `60` / `86400` | Clamps on `config_check_s` |
| `kAckEnabled` | `false` | Wait for the API to confirm a fix was stored before dropping it |
| `kAckTopic` | `devices/GNSSXX/ack` | Topic the API publishes its delivery verdicts to |
| `kAckTimeoutMs` | `10000` | Wait for the API's verdict (covers decrypt + validate + DB write) |
| `kDeviceAckPrivateKeyPem` | — | **This device's RSA private key (secret)** — decrypts the acks |
| `kDefaultSendIntervalSeconds` | `60` | Interval used until the broker says otherwise |
| `kDefaultSleepBetweenSends` | `false` | Sleep flag used until the broker says otherwise |
| `kMinSendIntervalSeconds` / `kMaxSendIntervalSeconds` | `5` / `86400` | Clamps on a broker-supplied `interval_s` |
| `kMinFixTimeoutSeconds` / `kMaxFixTimeoutSeconds` | `15` / `3600` | Clamps on `fix_timeout_s` |
| `kMinQueueMaxFixes` / `kMaxQueueMaxFixes` | `100` / `100000` | Clamps on `queue_max_fixes` (~100 MB of envelopes at the ceiling) |
| `kMinRetryIntervalHours` / `kMaxRetryIntervalHours` | `1` / `720` | Clamps on `retry_interval_h` |
| `kMaxRetryMaxAgeHours` | `8760` | Upper clamp on `retry_max_age_h` (no floor — `0` means "never") |
| **`kSdEnabled`** | `true` | **Enable/disable the microSD store-and-forward queue** |
| `kSdSpiHost` | `SPI2_HOST` | SPI peripheral the card is wired to (HSPI) |
| `kSdPinMiso/Mosi/Sclk/Cs` | `2/15/14/13` | T-SIM7000G microSD SPI pins |
| `kSdMountPoint` | `/sdcard` | FAT mount point |
| `kSdQueueFilePath` | `/sdcard/queue.jsonl` | Line-delimited queue file (one envelope per line); its head offset lives in the sibling `.idx` |
| `kSdSettingsFilePath` | `/sdcard/settings.json` | Cached runtime settings (**plaintext**) |
| `kSdMaxQueuedFixes` | `20000` | **Default** for `queue_max_fixes` — cap on stored fixes; oldest are dropped past this |
| `kSdMaxBurstFixes` | `10` | Max envelopes per burst message (RAM/MQTT safety bound — see below) |
| `kBacklogFlushRetryMs` | `600000` | Pause after a backlog flush that achieved **nothing** (an MQTT reconnect cancels it) |
| `kBacklogFlushBudgetMs` | `30000` | How long one flush may keep draining before yielding to the poll loop; it resumes on the next poll |
| `kSdRetryFilePath` | `/sdcard/retry.jsonl` | Fixes the API **rejected**, awaiting a scheduled retry |
| `kRetryIntervalHours` | `24` | **Default** for `retry_interval_h` — wait between attempts on a rejected fix |
| `kRetryMaxAgeHours` | `168` | **Default** for `retry_max_age_h` — give up on a fix still refused after this long (`0` = never) |
| `kSdMaxRetryEntries` | `2000` | Cap on the retry file; oldest are dropped past this |
| **`kBootLogEnabled`** | `true` | **Enable/disable the boot log** (see [Boot log](#boot-log)) |
| `kSdBootLogPath` | `/sdcard/boot.log` | One line per boot (**plaintext**) |
| `kSdMaxBootLogLines` | `200` | Cap on the boot log; oldest lines are dropped past this |
| `kBootLogPrintLines` | `10` | How many previous boots are printed to serial at start-up |
| `kWakeGpioPin` | `-1` | Extra ext0 wake pin; `-1` = timer-only |
| `kWakeGpioLevel` | `1` | Pin level that wakes the chip (`1` = HIGH) |
| `kMinDeepSleepMs` | `1000` | Floor on a deep-sleep duration |

> All settings are `constexpr`, so when `kGnssDebug` is `false` the debug code
> is removed by the compiler — zero runtime cost in production. Likewise, when
> `kWifiEnabled` is `false` the WiFi connect code is dropped entirely, when
> `kMqttEnabled` is `false` the publish path is skipped, and when `kSdEnabled`
> is `false` the card is never mounted.
>
> The reporting interval and the sleep flag are the exception: they are **not**
> compile-time constants. The values above are only the defaults a device falls
> back to — see [Remote settings](#remote-settings-broker--device) below.

---

## WiFi

WiFi runs in **station mode** and is fully optional, handled by the standalone
[`WifiManager`](src/wifi/WifiManager.h) class.

- **To enable:** set `kWifiEnabled = true` in `Config.h` and provide
  `kWifiSsid` / `kWifiPassword`. On boot, the app connects before starting GNSS.
  A failed connection is logged as a warning and tracking continues regardless.
- **To disable:** set `kWifiEnabled = false`. The WiFi stack is never
  initialised and the connect code is compiled out.

Using it directly:

```cpp
#include "config/Config.h"
#include "wifi/WifiManager.h"

static WifiManager wifi(config::kWifiSsid, config::kWifiPassword,
                        config::kWifiMaxRetries);

wifi.begin();                                  // init NVS + WiFi stack (once)
if (wifi.connect(config::kWifiConnectTimeoutMs)) {
    // wifi.isConnected() == true, we hold an IP
}

wifi.disconnect();                             // drop the link + stop the driver
```

> **Keeping secrets safe:** credentials live only in the git-ignored `Config.h`.
> The committed `Config.example.h` carries empty placeholders, so cloning the
> repo never exposes a network. Don't paste credentials anywhere else.

---

## MQTT & end-to-end encryption

Once a fix is read, it is published to an MQTT broker — but only *after* being
encrypted so that **the broker never sees a position**. Two independent classes
handle this, and [`TelemetryPublisher`](src/mqtt/TelemetryPublisher.h) glues
them together:

```
TelemetrySample ──(TelemetryPublisher formats)──► plaintext JSON
                ──(PayloadCrypto seals)─────────► encrypted envelope
                ──(MqttClient publishes)────────► broker topic  (devices/GNSSxx)
```

The plaintext JSON (before sealing) carries the position plus the optional
sensor fields; the field names match the API's `PositionPayloadDto` exactly:

```json
{"device":"GNSS01","latitude_deg":50.08,"longitude_deg":14.42,
 "speed_kmph":0.0,"altitude_m":210.0,"time_utc":"2026-07-23T10:00:00Z",
 "battery_pct":87,"accel_x_g":0.01,"accel_y_g":-0.02,"accel_z_g":0.99,
 "temp_c":31.0}
```

`battery_pct` (`0` = charging), `accel_x/y/z_g` and `temp_c` (modem die
temperature in °C) are **omitted** when their sensor is disabled or a read
failed, so an older decoder still parses the six location fields it knows. The
raw pack millivolts are **not** on the wire — they stay on the serial console as
a curve-calibration aid only.

`battery_pct` is produced by [`BatteryReporter`](src/power/BatteryReporter.h)
from one column of the method log — `p4_curve` by default, i.e. the calibrated
**median** of the ADC burst scored with the piecewise Li-ion curve
(`kBatteryReportSourceIndex` / `kBatteryReportModelIndex`). Three rules, and each
protects something downstream:

| Situation | On the wire | Why |
|-----------|-------------|-----|
| Charger connected | `0` | The agreed sentinel; the API accepts it and the front end renders "charging". It is also the only honest answer — the sense pin is cut off from the cell on USB power, so the ADC has nothing to say. |
| A reading | that percent, floored at **1** | So a genuinely empty pack can never be read as the charging sentinel. |
| Neither | the field is **absent** | Never `-1`: the API validates `battery_pct` to `0–100` and rejects the **whole fix** — position included — on anything outside it. |

### Transport — `MqttClient`

[`MqttClient`](src/mqtt/MqttClient.h) wraps the ESP-IDF **esp-mqtt** client. The
scheme in `kMqttBrokerUri` selects the transport *and* whether the hop is TLS:

| URI scheme | Transport | Encrypted hop? |
|------------|-----------|----------------|
| `wss://host:port/path` | MQTT over WebSocket | ✅ (recommended) |
| `ws://host:port/path`  | MQTT over WebSocket | ❌ |
| `mqtts://host:port`    | MQTT over TCP | ✅ |
| `mqtt://host:port`     | MQTT over TCP | ❌ |

For the secure schemes the broker's certificate is verified against the built-in
CA bundle. The client connects and **auto-reconnects in the background**, so the
main loop never blocks on the network; it publishes each fix at **QoS 1** only
while `mqtt.isConnected()`.

### Payload encryption — `PayloadCrypto`

[`PayloadCrypto`](src/crypto/PayloadCrypto.h) applies hybrid **KEM-DEM**
encryption that mirrors the desktop
[`crypto_box.py`](../DESKTOP/crypto_box.py) byte-for-byte:

1. A fresh random **AES-256** key is generated for every message.
2. The JSON payload is sealed with **AES-256-GCM** (12-byte nonce, 16-byte tag).
3. That one-time AES key is encrypted with **RSA-OAEP (SHA-256)** using the
   **receiver's public key** (`kReceiverPublicKeyPem`).

The published message is a compact JSON envelope:

```json
{"alg":"RSA-OAEP-SHA256+AES-256-GCM",
 "k":"<RSA-OAEP encrypted AES key>",
 "iv":"<12-byte GCM nonce>",
 "ct":"<AES-GCM ciphertext>",
 "tag":"<16-byte GCM tag>"}
```

Only the holder of the matching **private** key — the desktop companion — can
recover the AES key and read the position. The device only ever needs the
**public** key, which is not a secret.

> ℹ️ **The RNG and the public key are set up once**, on the first `encrypt()`
> call, and then reused for every message: seeding CTR_DRBG from the hardware
> entropy source and parsing the PEM are expensive in both time and *stack*, and
> doing them per fix was enough to overflow the main task's stack (see
> [Main task stack](#main-task-stack) below). CTR_DRBG reseeds itself as it is
> consumed, so each message still gets a fresh, unpredictable AES key and nonce.

### Getting the receiver's public key

The keypair is created and held by the [desktop companion](../DESKTOP/README.md).
Each device has its own key, so compromising one never exposes the others:

```bash
cd ../DESKTOP
python app.py generate-certs GNSS01     # creates certs/GNSS01/{private,public}.pem
```

Copy the printed **public** key into this device's `Config.h`
(`kReceiverPublicKeyPem`, one C-string line per PEM line, each ending with `\n`)
and set `kDeviceId` / `kTelemetryTopic` to match (e.g. `GNSS01` /
`devices/GNSS01`). The desktop `read` mode then decrypts that device's traffic
automatically. Keep the matching `private.pem` safe — losing it means the
device's messages can no longer be read.

> **Two layers, on purpose.** TLS (`wss://`) protects the hop to the broker;
> `PayloadCrypto` *additionally* encrypts the payload end-to-end so the broker
> operator, and anyone on the path, only ever see ciphertext.

---

## microSD store-and-forward

A fix must never be lost just because the broker was briefly unreachable. Three
classes give the device a **persistent, encrypted outbox** on its microSD card,
coordinated by [`FixForwarder`](src/sdcard/FixForwarder.h):

```
GnssFix ──(TelemetryPublisher.sealFix)──► encrypted envelope
   link up, nothing queued  ──► publish [envelope] ──(QoS-2)─► ──(API ack)─► done
   link up, backlog present ──► queue it, then drain the queue in bursts
   link down                ──► FixQueue.enqueue → /sdcard/queue.jsonl
   API rejected it          ──► RetryQueue.add    → /sdcard/retry.jsonl

no fix at all, link up      ──► FixForwarder.flushBacklog() drains the card anyway
```

- **Same encryption as transmit.** What is stored is the *exact* envelope that
  would have been sent (`RSA-OAEP-SHA256 + AES-256-GCM`), one per line in
  `queue.jsonl`. A lost/stolen card therefore leaks nothing — the device holds
  only the public key and cannot even read back its own stored positions.
- **Append-only, with a head offset.** `queue.jsonl` is never rewritten to remove
  delivered entries. A sidecar, `queue.jsonl.idx`, records where the live region
  starts:

  ```
  queue.jsonl      [xxxx delivered xxxx][ live entries ................. ]
                                        ▲ head
  queue.jsonl.idx  head=12800000 count=421337
  ```

  Popping a burst moves `head` forward and rewrites nothing, so a pop costs
  *O(burst)* rather than *O(file)*. The dead prefix is reclaimed when the queue
  drains completely (the file is simply deleted — the normal end of an outage) or,
  failing that, by a compaction that only runs once the prefix is both over 32 MB
  **and** more than half the file. That threshold is what keeps the total copying
  across a whole drain linear overall instead of quadratic.

  The previous design rewrote the entire file on every pop, so the cost of
  draining a backlog grew with the square of its depth — which is what put a
  practical ceiling on `queue_max_fixes`. See [`FixQueue`](src/sdcard/FixQueue.h)
  and [`QueueIndex`](src/sdcard/QueueIndex.h).
- **A missing or damaged index is safe.** The queue falls back to reading from
  the top of the file and re-counting. That re-offers entries the API has already
  stored, which costs airtime but never a duplicate row — the API dedupes on
  `(device, fix time)`. The same property makes the crash window harmless: the
  sidecar is written *after* the in-memory head moves, so a power cut re-delivers
  rather than skips.
- **Format only if needed.** The card is mounted with `format_if_mount_failed`,
  so a blank/corrupt card is formatted once, but an existing queue **survives
  reboots** (fixes saved before a power cut are recovered and sent on boot).
- **Delivered means *stored*, not merely sent.** A fix leaves the queue only once
  **two** acks have arrived: the broker's **QoS-2** ack (`publishConfirmed`) and
  then the **API's verdict** (see [Delivery acknowledgements](#delivery-acknowledgements)).
  The broker ack alone proves nothing about the database — with `kAckEnabled`
  off, a fix the API rejects or never receives is still deleted. If either ack
  never comes the data stays on the card for the next attempt, and the healthy
  online path still never writes to the card at all.
- **Always a JSON array.** Every message — a single live fix or a drained
  backlog — is published as a JSON **array of envelopes** (`[env]` or
  `[env1,env2,…]`), so the subscriber always parses one shape. A long backlog is
  drained in several back-to-back bursts of up to `kSdMaxBurstFixes` so the
  array and MQTT buffer stay within this board's (PSRAM-less) RAM.
- **The burst size is a memory budget, not a throughput knob.** One envelope is
  ~1 KB — over half of it the base64 RSA-3072-wrapped AES key, which every
  envelope carries separately — and a burst of *N* is held **three times over**
  at the moment of publish: the batch read off the card (kept alive so rejected
  fixes can be parked for retry), the joined JSON array (one *contiguous*
  allocation), and esp-mqtt's outbox copy (also contiguous, because QoS 2 must be
  re-sendable until PUBCOMP). Add the mbedTLS session buffers (~20 KB) and the
  WebSocket buffer, all in the same internal DRAM. At `40` that reached ~117 KB
  per burst and the outbox's ~39 KB contiguous request began failing outright
  (`outbox_enqueue: Memory exhausted`) once the heap was fragmented; `10` keeps
  the peak near 30 KB. Raising it costs no throughput worth measuring in return —
  bursts already fire back-to-back within `kBacklogFlushBudgetMs`, and each one
  is dominated by the broker and API ack waits, not by its size.
- **A refused publish reports why.** `MqttClient` bounds esp-mqtt's outbox
  (`outbox.limit`) so an oversized publish is refused cleanly instead of failing
  a raw allocation deep inside the client, and logs the payload size, the free
  heap **and the largest free block** — the pair that tells memory exhaustion
  apart from fragmentation.
- **A failed drain does not drag the retry queue down with it.** When a drain
  stops on a transport or card error, the retry drain is skipped: it would fail
  identically, and `takeDue()` lifts entries *off* the card before publishing and
  rewrites every one of them back on failure — a full rewrite of the retry file
  bought for nothing.
- **No card file is ever held in RAM.** Every `SdCard` helper streams — including
  `rewriteLines()`/`forEachLine()`, which `RetryQueue` walks its schedule with —
  so memory scales with one **line** and one **burst**, never with the backlog.
  This is load-bearing, not tidiness: the retry queue used to read and rebuild
  its whole file as a single `std::string`, and since `CONFIG_COMPILER_CXX_EXCEPTIONS`
  is off, the failed allocation aborted the device rather than throwing. It fired
  right after a long backlog drain, which is precisely when the heap is most
  fragmented *and* the retry file most likely to be busy. See
  [`RetryQueue.h`](src/sdcard/RetryQueue.h).
- **Sending does not need a position lock.** `FixForwarder::flushBacklog()` is
  called at the top of every cycle *and* after every GNSS poll (each
  `kFixPollStepMs`) while waiting for a fix, so a queued backlog leaves within
  seconds of MQTT reconnecting — even on a device that never gets a fix at all
  (parked in a garage, antenna unplugged). Without this a fixless cycle could
  never touch the card, and a car left indoors would sit on its backlog
  indefinitely. The call costs two in-memory comparisons when there is nothing
  queued or the broker is unreachable, so polling it is free on the healthy path.
- **A fixless flush drains the *whole* backlog, not one burst.** Flushes are
  paced on what an attempt **achieved**, never on whether it finished. An attempt
  that moved data off the card — or that simply spent its `kBacklogFlushBudgetMs`
  budget mid-drain — is repeated on the very next poll, so however deep the queue
  is it empties in back-to-back bursts a couple of seconds apart. Only an attempt
  that achieved *nothing* waits out `kBacklogFlushRetryMs`: a dead link, an
  unreadable card, or an API that has stopped answering (and that last one gets a
  few prompt retries first, because a verdict which merely arrived late is
  already held by the `AckWatcher` and clears the burst on the next try). An MQTT
  reconnect cancels the wait either way, since a reconnect is exactly the event
  worth retrying on. The reporting-cycle path (`process()`) is paced by the same
  rule and the same budget — previously it drained unbounded and ignored the
  pause entirely, which meant a link that could not take a burst was re-offered
  the identical burst once per cycle regardless.
- **The budget is why one flush cannot hog the loop.** A full 20 000-fix queue is
  500 bursts, i.e. minutes of publishing and ack-waiting. `kBacklogFlushBudgetMs`
  caps how long one call keeps going before it hands the CPU back to the GNSS
  poll and the remote-settings poll; the next call resumes exactly where it
  stopped, since everything confirmed has already left the card.
- **The clock caveat.** The GNSS UTC time is the device's only wall clock, and
  the `RetryQueue` schedule is measured in it. The modem reports that time as
  soon as it decodes any satellite — well before it can compute a position — so
  a fixless flush usually still has one. When it does not, the live queue still
  drains in full; only the retry file sits the cycle out, and a rejected fix that
  cannot be scheduled stays in the live queue instead of being dropped.

> ⚠️ **Desktop change required (not included here).** Because the wire format is
> now a JSON *array* of envelopes, the [desktop companion](../DESKTOP/README.md)
> must be updated to iterate the array and decrypt each element (a single fix is
> just an array of one). That change was intentionally left out of this commit —
> update the subscriber before relying on live decoding.

To disable the card entirely, set `kSdEnabled = false`: the forwarder still
publishes live fixes, it simply cannot store the ones it misses.

---

## Delivery acknowledgements

**A QoS-2 ack from the broker only proves Mosquitto took the message.** It says
nothing about whether the API decrypted it, accepted it, and wrote a row. So on
its own it let a fix that was rejected — or that arrived while the API was down —
be deleted from the card and silently lost.

With `kAckEnabled = true` the API publishes an **encrypted verdict per envelope**
to `kAckTopic`, and a fix leaves the card only when that verdict says *stored*.

### How an envelope is matched to its verdict

`PayloadCrypto` adds a cleartext **`id`** (16 lowercase hex chars) to every
envelope, alongside `alg`/`k`/`iv`/`ct`/`tag`. It sits *outside* the ciphertext
deliberately: `FixQueue` stores the envelope verbatim, so the id survives deep
sleep and reboots and can still be matched against an ack days later. It names a
message, never its contents, so the broker learns nothing from it.

The ack itself is one envelope of the same shape, wrapping:

```json
{"device":"GNSS01",
 "stored":["9f2a7c41b8e05d36"],
 "rejected":[{"id":"77aa01ffbe24c185","reason":"TimestampOutOfWindow"}]}
```

`stored` merges *inserted* and *already present* — the device does the same thing
either way. Re-sending is safe: the API dedupes on `(device, fix time)`, so a
lost ack costs one duplicate delivery, never a duplicate row.

### The ack key runs the *opposite* way to the telemetry key

| | Telemetry (device → API) | Ack (API → device) |
|---|---|---|
| Device holds | `kReceiverPublicKeyPem` (**public**) | `kDeviceAckPrivateKeyPem` (**private**) |
| Server holds | the receiver private key | the device's ack public key |

That inversion is the whole security point: only firmware holding the ack private
key can read a verdict, so a compromised broker cannot forge one and make the
device delete undelivered fixes. It also means **the ack private key must never
reach the server** — generate the pair yourself and import only the public half:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out GNSS01_ack_private.pem
openssl rsa -in GNSS01_ack_private.pem -pubout -out GNSS01_ack_public.pem
# in API/CarPosAPI:
dotnet run -- import-device-key --device GNSS01 --ack-public-pem GNSS01_ack_public.pem
```

Paste the **private** PEM into `kDeviceAckPrivateKeyPem` in your `Config.h`
(git-ignored), then delete the loose `.pem`.

### ⚠️ The broker ACL must grant the read

Mosquitto needs an explicit `topic read` rule before it will *deliver* to a
subscriber. Without it the SUBSCRIBE is still ACKed and every ack is silently
dropped — which looks exactly like a broken API, and makes every fix wait out
`kAckTimeoutMs`. The rules live in
[`Container/MQTTBroker/mosquitto/acl`](../Container/MQTTBroker/mosquitto/acl):

```
user GNSS01
topic read devices/GNSS01/ack

user carpos-api
topic write devices/+/ack
```

### Rejected fixes are retried, not dropped

A rejection goes to `kSdRetryFilePath` (`retry.jsonl`) with its first-seen time
and attempt count, and is re-offered every `kRetryIntervalHours` until it is
accepted or `kRetryMaxAgeHours` passes. This is worth doing because several
reject reasons are **server-side and self-clearing**: an unknown or deactivated
device starts working the moment its row is provisioned, and a decrypt failure
clears when the key is fixed. Only the age cap ever discards data, and it logs at
error level when it does.

The schedule is measured in **GNSS UTC time** — the only trustworthy wall clock
here, since `esp_timer` restarts across the deep-sleep reboot and there is no RTC
battery. A cycle with no valid GNSS time simply treats nothing as due.

A line that cannot be parsed is **kept**, counted and logged — never discarded.
cJSON returns the same `nullptr` for "malformed" as for "could not allocate", so
a line that fails to parse while memory is tight is most likely a perfectly good
fix; deleting it would be silent data loss, while keeping it costs a few hundred
bytes on a card with gigabytes. The `kSdMaxRetryEntries` cap is what eventually
clears them. Walking the file therefore takes two streaming passes — one to
decide, one to rewrite — and the second is skipped entirely when nothing came
due, which is the common cycle.

Set `kAckEnabled = false` to restore the old behaviour exactly: the broker ack
becomes the only confirmation, and nothing is written to `retry.jsonl`.

---

## Remote settings (broker → device)

Seven things about the device are tunable at runtime, from the broker, without a
reflash: **how often it reports**, **whether it sleeps in between**, **how long it
chases a GNSS lock**, **how many undelivered fixes it keeps**, the two knobs of its
**rejected-fix retry policy**, and **how often it re-checks for its own settings**.
The device subscribes to `kConfigTopic` (`devices/GNSS01/config`) and expects a
small **plaintext** JSON document:

```json
{
  "version": 7,
  "interval_s": 60,
  "sleep_between": true,
  "fix_timeout_s": 180,
  "queue_max_fixes": 20000,
  "retry_interval_h": 24,
  "retry_max_age_h": 168,
  "config_check_s": 3600
}
```

| Field | Type | Meaning | Clamped to |
|-------|------|---------|------------|
| `version` | number | Server-assigned revision of this document. Not a setting — the device echoes it back in every report as `settings_version`, which is how the dashboard tells "published" from "actually running". | — |
| `interval_s` | number | Seconds between position reports. | `[kMinSendIntervalSeconds, kMaxSendIntervalSeconds]` |
| `sleep_between` | boolean | Power the modem down and deep-sleep the ESP32 between reports. | — |
| `fix_timeout_s` | number | How long to keep asking for a position before giving up on the cycle. | `[kMinFixTimeoutSeconds, kMaxFixTimeoutSeconds]` |
| `queue_max_fixes` | number | How many undelivered fixes the SD queue may hold before the oldest are dropped. | `[kMinQueueMaxFixes, kMaxQueueMaxFixes]` |
| `retry_interval_h` | number | Hours between attempts on a fix the API rejected. | `[kMinRetryIntervalHours, kMaxRetryIntervalHours]` |
| `retry_max_age_h` | number | Give up on a still-rejected fix after this long; `0` = never. | `[0, kMaxRetryMaxAgeHours]` |
| `config_check_s` | number | How often an **awake** device asks the broker to re-send this document. A backstop only — see [Staying in step](#staying-in-step). Ignored while `sleep_between` is on. | `[kMinConfigCheckSeconds, kMaxConfigCheckSeconds]` |

Any field may be omitted; what is absent is simply left as it was. That merge
behaviour is what keeps this firmware compatible with an older publisher that
only knows `interval_s` and `sleep_between`.

**Out-of-range values are clamped, never rejected** — a tracker in a field has
nobody to ask, and must keep reporting whatever nonsense it is handed. The API
enforces the same numbers by answering **400** instead, because a person at a
dashboard *can* be told to fix their input. If you change a bound in `Config.h`,
change it in the API's `DeviceConfigRules` too.

`queue_max_fixes` is a **count rather than a duration** on purpose: a queued line
is a bare encrypted envelope with no plaintext timestamp to age it by, and adding
one would write fix times in the clear onto a card that today leaks nothing. One
fix is queued per reporting cycle, so the dashboard converts the count into an
approximate span ("≈ 13.9 days at a 60 s interval") for the reader.

### Normally you do not publish this by hand

The dashboard owns these settings: the API stores every revision, publishes the
document retained, and shows whether the device has picked it up. See
[`../API/CarPosAPI/README.md`](../API/CarPosAPI/README.md). The command below is
for bench work and debugging.

### ⚠️ Publish it **retained**

```bash
mosquitto_pub -h jimajer.cz -t 'devices/GNSS01/config' -r \
              -m '{"version":7,"interval_s":60,"sleep_between":true}'
```

The `-r` (retain) flag is **not optional in practice**. With `sleep_between` on,
the device is only connected for a few seconds per cycle, so it will essentially
never be online at the moment you publish a live message. Retaining it makes the
broker replay the current config the instant the device subscribes — which is
exactly what it waits `kConfigFetchTimeoutMs` for on every boot.

### Why it is not encrypted

The fix payload is end-to-end encrypted because a position is private. This
document holds a cadence and a boolean — nothing a broker operator or a thief
with the SD card should not see. It travels inside the `wss://` TLS hop like
everything else, and is written to `/sdcard/settings.json` in the clear.

### Precedence

```
Config.h defaults  ←  /sdcard/settings.json  ←  retained MQTT config
   (weakest)              (survives reboot)         (strongest, wins)
```

On boot the device loads the cached file (falling back to the `Config.h` defaults
if the card is missing, the file absent, or its contents corrupt), then waits
briefly for the broker. Anything that arrives is validated, clamped, adopted, and
— only if it actually differs from what was already in force — written back to
the card. That cache is what lets a device that boots in a tunnel still know it
is meant to be sleeping.

### Staying in step

There is **one subscription** and **three ways** a document arrives through it.
All three end in `RemoteSettings::poll()` on the application task, so there is
exactly one parse → clamp → adopt → write-to-card path however the bytes got here.

| Path | When it fires | Latency |
|------|---------------|---------|
| **Push** — the broker delivers a live publish on the open subscription | the device is awake and connected when someone saves | **under a second** |
| **Retained replay on connect** — the broker re-sends the retained document on every SUBSCRIBE | cold boot, deep-sleep wake, reconnect after an outage | immediate on connect |
| **Periodic re-check** (`config_check_s`) — the device re-subscribes on purpose | every `config_check_s` while awake | ≤ `config_check_s` |

Push is the normal case and needs nothing from the device — it is already
subscribed. The retained replay is what covers a device that was **offline when
the change was saved**: nothing is lost, because the API publishes with the retain
flag and the broker holds the document indefinitely. `MqttClient` re-issues every
remembered subscription on each `MQTT_EVENT_CONNECTED`, which is required anyway
because the session is clean and the broker forgets us on every disconnect.

`RemoteSettings::resyncIfDue` is the third path, and it exists for one failure
mode: a live publish only reaches us over a connection that is genuinely alive,
and a half-open socket looks connected while delivering nothing. It is a **plain
re-SUBSCRIBE**, not an unsubscribe/subscribe pair — MQTT 3.1.1 §3.8.4 requires a
repeat SUBSCRIBE on an identical filter to re-send matching retained messages
*without* interrupting the flow of publications (`[MQTT-3.8.4-3]`), so it costs one
packet and has no window in which a live config could slip past.

**How the wait is structured, and why it is free.** Between reports the
application task blocks in `waitForUpdate`, which waits on a FreeRTOS event group
that `onMessage` signals from the esp-mqtt event task. A blocked task is a blocked
task — the same tickless idle and the same current draw as the plain delay it
replaces — but this one is also woken the instant a config lands. The wait is cut
into chunks of at most `config_check_s` purely so the re-check still happens on a
device whose reporting interval is hours long; each chunk boundary is one wake-up
and one SUBSCRIBE, and that is the whole ongoing cost.

Adopting a config mid-wait **re-times the cadence without disturbing it**: the
remaining time is recomputed from the fix that anchored the interval, so shortening
`interval_s` past what has already elapsed sends the next report at once, and
lengthening it simply extends the wait. No extra GNSS acquire, no extra airtime.
Switching `sleep_between` on mid-wait ends the wait immediately, so the device
sleeps promptly instead of staying awake for what could be another 24 hours.

**The acquire is the other place a config gets adopted.** Chasing a lock can take
minutes, and the per-fix-poll hook in the main loop polls `RemoteSettings` on
every step of it. It adopts the document *completely* — it takes it, moves the
loop's working settings onto it and pushes them through `SettingsApplier` — so a
change saved while the device is hunting for satellites is in force by the time
that cycle's report is assembled, and is reported as such. Doing only the first
of those three used to leave the report stamped with the previous revision, which
showed up in the dashboard as a change staying "pending" until the report *after*
the one that adopted it. The acquire already running keeps the `fix_timeout_s` it
was started with: a shortened timeout takes effect from the next cycle rather
than truncating a wait that is about to produce a lock.

**With `sleep_between` on none of this runs** — the radio and the CPU are off, so
there is nothing to push to and nothing to poll. Such a device gets its
configuration through the retained replay on every wake, which is why
`config_check_s` is documented as awake-mode-only. If that replay is slower than
the few seconds the wake allows for it, the document lands during the acquire
instead and the hook above still gets it into that wake's report.

In the other direction, every position report carries `settings_version`. The API
records it against the device row, so the dashboard can show whether a change it
published has actually been adopted — and, because the API keeps every revision,
*which* values the tracker is running while a newer one waits. The version is
stamped when the fix is **captured**, not when it is published, so a backlog
drained days later honestly reports the settings it was taken under.

Two gaps are inherent to echoing the revision inside telemetry rather than
acking it separately. A config that arrives *after* the sample is sealed but
before the publish finishes is reported next cycle — that sample really was
captured under the older settings. And a cycle that gets no fix publishes
nothing, so a device that cannot see the sky adopts its new configuration
silently and confirms it only once it gets a lock.

---

## Using it in your own code

```cpp
#include "config/Config.h"
#include "crypto/PayloadCrypto.h"
#include "gnss/GnssModule.h"
#include "modem/Sim7000Modem.h"
#include "mqtt/MqttClient.h"
#include "mqtt/TelemetryPublisher.h"
#include "serial/SerialPort.h"

static SerialPort   serial(config::kModemUartPort, config::kModemTxPin,
                           config::kModemRxPin, config::kModemBaudRate);
static Sim7000Modem modem(serial, config::kModemPwrKeyPin);
static GnssModule   gnss(modem);

// Transport + encryption + the glue that publishes a fix.
static MqttClient         mqtt(config::kMqttBrokerUri, config::kMqttUsername,
                               config::kMqttPassword, config::kMqttClientId);
static PayloadCrypto      crypto(config::kReceiverPublicKeyPem);
static TelemetryPublisher publisher(mqtt, crypto, config::kTelemetryTopic,
                                    config::kDeviceId);

gnss.begin();                 // power on + enable engine + select constellations
mqtt.begin();                 // connects + auto-reconnects in the background

GnssFix fix;
if (gnss.readFix(fix) && fix.hasFix()) {
    double lat   = fix.position.latitudeDeg;
    double lon   = fix.position.longitudeDeg;
    double speed = fix.speedKmph;
    // fix.time.{year,month,day,hour,minute,second}

    if (mqtt.isConnected()) {
        publisher.publishFix(fix);   // format → encrypt → publish the envelope
    }
}

gnss.powerOffModule();        // lowest power until the next powerOnModule()
```

> `readFix()` gives you the raw reading, exactly as above. The firmware's own
> loop instead goes through
> [`FixAverager::acquire()`](src/gnss/FixAverager.h), which acquires a lock,
> discards that first fix and returns the mean of the next three — see
> [Averaged position reports](#averaged-position-reports).

### Power saving

The manual switches, if you are driving `GnssModule` yourself:

- `powerOffModule()` / `powerOnModule()` — switch the **entire modem** off/on
  via PWRKEY. Lowest current draw; re-enabling takes a few seconds (modem boot).
  Powering the modem off also cuts the active GNSS antenna's amplifier (the modem
  drives it from its own GPIO4) and the LTE PA.
- `disableGnss()` / `enableGnss()` — switch only the **GNSS engine**. Faster to
  toggle, but the modem itself keeps running (higher idle current).

---

## Deep sleep between reports

When the broker sets `"sleep_between": true`,
[`DeepSleepController`](src/power/DeepSleepController.h) takes over the end of
every cycle. The order matters, and it owns it:

1. **MQTT** — `stop()`, so the broker sees a clean DISCONNECT rather than waiting
   out the keep-alive on a session that is already gone.
2. **WiFi** — stop the driver. ESP-IDF wants the radio *stopped* before deep
   sleep, not merely idle.
3. **Modem** — `powerOffModule()`. This is the big one: the GNSS engine, the
   antenna amplifier and the LTE PA all go down with it.
4. **microSD** — unmount, leaving a clean FAT and releasing the SPI pins.
5. **PWRKEY** — latch the pin at its idle level with `gpio_hold_en()` for the
   duration (the level comes from `Sim7000Modem::pwrKeyIdleLevel()`, so it
   follows `kModemPwrKeyActiveLow`). During deep sleep the digital IO matrix is
   powered down and pins float; a floating PWRKEY reads as a pulse and would
   switch the modem straight back on — undoing everything step 3 just achieved.

`releasePinHolds()` undoes step 5 at the top of `app_main()`, before any driver
touches those pins again.

### It reboots, it does not resume

Deep sleep is not a pause. The chip **restarts**: `app_main()` runs from the top,
the card is remounted, WiFi re-associates, TLS re-handshakes, the subscription is
re-issued and the GNSS engine acquires from cold. Nothing on the stack or the
heap survives.

Budget for that. A cold GNSS acquisition alone can take 30 s or more (bounded by
`kFixAcquireTimeoutSeconds`), so at `interval_s = 60` the device spends most of
its cycle awake anyway and saves little. **Deep sleep starts paying off at
intervals of several minutes.** This is inherent to a full modem power-down, not
something the firmware can work around.

### Wake sources

The **RTC timer** is always armed, for `interval_s` minus the time already spent
since the position was captured — so the cadence stays steady no matter how long
publishing took, rather than drifting later every cycle. If no fix was obtained
this cycle, a fresh full interval is started instead (otherwise a device that
just spent `kFixAcquireTimeoutSeconds` finding no satellites would be "late" the
moment it gave up, and would reboot-retry in a tight, battery-eating loop).

An **external GPIO** (ext0) can wake the device early — an ignition sense or a
motion line, say. It is off by default:

```cpp
constexpr int kWakeGpioPin   = 33;  // -1 disables it; 32/33 are free & RTC-capable
constexpr int kWakeGpioLevel = 1;   // wake when the pin reads HIGH
```

Set the pin and it arms itself; the matching internal pull (down for level `1`,
up for level `0`) is held through the sleep so a floating input cannot wake the
device at random. Pins 2, 4, 13, 14, 15, 26 and 27 are already taken by the modem
and the card, and 34–39 are input-only with no internal pulls (they need an
external resistor). `DeepSleepController::wakeCauseName()` reports which source
fired, so the serial log tells you whether a wake was scheduled or external.

---

## Debug output

With `kGnssDebug = true`, every read prints to the serial console, e.g.:

```
================ GNSS FIX ================
  Engine running : yes
  Fix status     : FIX
  UTC time       : 2026-06-23 12:00:00.000
  Latitude       : 48.123456 deg
  Longitude      : 17.123456 deg
  Altitude       : 210.5 m
  Speed          : 0.42 km/h
  Course         : 0.0 deg
  Sats in view   : 12
  Sats used      : 9
  HDOP/PDOP/VDOP : 1.1 / 1.4 / 0.9
=========================================
--------------- SATELLITES --------------
                     in view   tracked
  GPS     (USA)    :     8         6
  GLONASS (Russia) :     5         4
  BeiDou  (China)  :     6         3
  Galileo (Europe) :     4         2
  TOTAL            :    23        15
  Strongest signal : 44 dB-Hz
-----------------------------------------
---------------- SENSORS ----------------
  Battery        : 87 % (3892 mV)
  Accel X/Y/Z    : 0.01 / -0.02 / 0.99 g
  Temperature    : 31.0 C
-----------------------------------------
```

The **SENSORS** block is printed beneath the satellite table after **every fix
poll** while the device waits for a lock, so battery/accel/temperature are
visible even before a fix arrives. `Battery` shows the percent with the raw pack
millivolts in parentheses — a calibration aid for the Li-ion curve, and
deliberately **not** published — or `charging (sentinel 0)` while the charger is
connected, or `n/a` when the monitor is disabled or a read failed; `Accel X/Y/Z`
shows the raw ADXL345 sample in g, or `n/a`; `Temperature` is the modem die
temperature (published as `temp_c`), or `n/a` when unavailable.

Once a lock arrives, three more **GNSS FIX** blocks follow about a second apart
with **no satellite table between them** — that is the averaging burst, which
suppresses the 3 s NMEA scan so its samples stay 1 s apart — and then one summary
line:

```
I (128412) FixAverager: Averaged 3 readings: 48.123457, 17.123454
I (128414) main: Fix: 48.123457, 17.123454  0.4 km/h
```

A partial burst says so instead (`Averaged 2 of 3 readings`), and each skipped
reading logs its own reason. See
[Averaged position reports](#averaged-position-reports).

**Read the two satellite columns carefully — they mean different things:**

| Column | Source | What it proves |
|--------|--------|----------------|
| **in view** | the receiver's stored **almanac** — where satellites *ought* to be given the rough time and last position | **nothing about signal.** A board with the antenna unplugged still reports 20+ in view |
| **tracked** | GSV entries carrying a **non-zero SNR** | real RF is arriving and being demodulated. This is the number that must reach 4 for a fix |

`Strongest signal` is the most diagnostic value in the whole report:

| dB-Hz | Meaning |
|-------|---------|
| `0` | no RF at all — antenna, u.FL socket, or antenna power |
| `< 25` | too weak to decode the ephemeris; **a fix will never arrive**, however long you wait |
| `25–35` | marginal — a fix is possible but slow |
| `35+` | healthy open-sky signal |

When the numbers indicate a problem the report appends the concrete next step,
so the log diagnoses itself rather than leaving you to interpret dB-Hz.

---

## Boot log

Every boot appends one plaintext line to `kSdBootLogPath` (`/sdcard/boot.log`)
and prints the recent history to the serial console, so plugging in USB after an
unexplained restart shows immediately what has been happening:

```
---------------- BOOT LOG ----------------
  #0039 reset=POWERON   wake=power-on / reset prev_up=?        heap=151204 bat=?
  #0040 reset=DEEPSLEEP wake=timer            prev_up=47s      heap=148012 bat=4130mV
  #0041 reset=BROWNOUT  wake=power-on / reset prev_up=52s      heap=147880 bat=4128mV
> #0042 reset=BROWNOUT  wake=power-on / reset prev_up=51s      heap=147904 bat=4119mV
------------------------------------------
```

`>` marks the boot now starting; the lines above it are read back off the card.

| Field | Meaning |
|-------|---------|
| `#NNNN` | Boot counter. Restarts at `#0001` whenever RTC memory is cleared. |
| `reset=` | `esp_reset_reason()` — `POWERON`, `BROWNOUT`, `PANIC`, `INT_WDT`, `TASK_WDT`, `DEEPSLEEP`, `SW`, `EXT`. |
| `wake=` | The deep-sleep wake cause, as before (`timer`, `ext0 GPIO`, `power-on / reset`). |
| `prev_up=` | How far the **previous** run got, in seconds. `?` when RTC memory did not survive it. |
| `heap=` | Free heap at boot. A number that falls across boots is a leak. |
| `bat=` | Pack millivolts at the previous run's last report. `?` when unknown. |

### Reading it

The `reset=` column is the one that matters, and `(RTC CLEARED)` is the tell:

| Line looks like | What happened |
|---|---|
| `reset=DEEPSLEEP wake=timer` | Normal `sleep_between` cycling. Nothing to see. |
| `reset=PANIC` / `TASK_WDT` repeating | A firmware crash loop. The panic backtrace precedes it on the console. |
| `reset=BROWNOUT` | The supply sagged past the detector. On battery this is the cell failing to deliver a current peak — see the pack note under [Troubleshooting](#troubleshooting-modem-did-not-respond-after-power-on). |
| `reset=POWERON` **+ `(RTC CLEARED)`** | The rail actually went away: a flat pack, a tripped protection FET, a pulled connector. **Not a crash.** |

That last distinction is the reason this exists. `RTC_DATA_ATTR` memory survives
deep sleep *and* CPU resets (panic, watchdog, software reset) but **not** a loss
of the rail — so whether the magic word is still there answers "did it reboot, or
did it lose power?", which the reset reason alone cannot.

### Cost and caveats

- **No flash wear and no extra power in the loop.** The running device only
  updates two words in RTC memory per report; the card is touched once per boot.
- The log is capped at `kSdMaxBootLogLines` (200 ≈ 14 KB) and the oldest lines
  are dropped past that, exactly like the fix queue.
- **`bat=` reads `?` for the run that lost power** — RTC memory went with it.
  That is not a gap in practice: the battery at the moment of death arrives
  through the encrypted position backlog on the card, which survives a flat pack.
  The boot log's unique contribution is the reset reason.
- With no card the block still prints this boot's line; only the history and the
  persistence are lost. The device carries on regardless, like every other
  SD-backed subsystem.

---

## Battery method log

The published `battery_pct` is one number produced by one method, and that number
is only as good as the method behind it — which cannot be judged without knowing
how the alternatives behave on the same pack, at the same instant.

So the firmware measures the pack **every** way this board allows, once per
reporting cycle, and appends the lot to a plaintext CSV on the card
(`kSdBatteryLogPath`, default `/sdcard/battery.csv`, written when
`kBatteryLogEnabled`). Nothing on the card is encrypted or queued — it changes
neither the envelope nor the shape of the payload.

**Exactly one of these columns leaves the device.** `kBatteryReportFromMethods`
selects it — `p4_curve` by default, the calibrated **median** of the ADC burst
scored with the piecewise Li-ion curve — and
[`BatteryReporter`](src/power/BatteryReporter.h) turns it into the payload's
`battery_pct`. Every other column is there to keep that choice honest: change
`kBatteryReportSourceIndex` / `kBatteryReportModelIndex` and a different column
is published, with no other code touched.

> The sweep is taken **before** the publish and exactly **once** per cycle. Once,
> because the trend detector's window is five *calls* — a second sweep would
> quietly halve the span `trend_charging` covers.

```
uptime_ms,gps_utc,gps_time_valid,has_fix,sats_used,raw_mean,raw_median,...
41230,2026-08-24T09:14:07Z,1,1,9,2043,2044,3291,3288,3288,3290,3872,1,1,...
```

### What one row contains

| Column(s) | Meaning |
|-----------|---------|
| `uptime_ms` | Milliseconds since boot (`esp_timer`). Always present, always monotonic — and the only usable x-axis before the receiver has a fix. |
| `gps_utc`, `gps_time_valid` | The current GNSS UTC as `YYYY-MM-DDThh:mm:ssZ`, **empty** until the receiver decodes one. The flag keeps "unknown" from being read as 1970. |
| `has_fix`, `sats_used` | Whether this cycle got a lock, and how many satellites went into it. |
| `raw_mean`, `raw_median` | The ADC burst behind the first four sources. A count near 0 is the fingerprint of the USB cut-off below. |
| `v1_naive_mv` | Raw counts × nominal full scale — no calibration at all. |
| `v2_calper_mv` | Each sample calibrated, then averaged. |
| `v3_calmean_mv` | Calibration applied to the mean count. |
| `v4_calmed_mv` | Calibration applied to the median count. |
| `v5_modem_mv` | The modem's own VBAT measurement (`AT+CBC`). |
| `adc_valid`, `v5_valid` | Whether the ADC path and the modem actually produced a reading. Sources 1–4 live or die together. |
| `p1_*` … `p5_*` | Each source scored by three models: `lin` (straight line), `curve` (piecewise Li-ion), `sig` (LiPo sigmoid). `-1` = that source had no reading. |
| `modem_pct`, `modem_bcs` | The modem's own percentage and charge status (`0` not charging, `1` charging, `2` complete), `-1` when unavailable. |
| `solar_raw`, `solar_mv`, `input_present` | The charge-input pin (GPIO36) and whether it says a source is connected. |
| `trend_charging`, `trend_usable` | Charging inferred from a rising pack voltage. **The window is five *cycles*, not five seconds** — it reacts in minutes and is a corroborating signal, not the primary one. |
| `fw_pct`, `fw_charging`, `fw_valid` | What the shipped `BatteryMonitor` concluded for the same moment — the thing every other column exists to be compared against. `fw_pct` is `-1` when that read failed, so it is never confused with the monitor's `0 = charging` sentinel. |
| `v_spread_mv`, `p_spread` | How far apart the methods landed. This is the deliverable. |

### Three caveats before you trust a capture

- **On USB power, `v1`–`v4` read ~0.** On the T-SIM7000G the sense pin is cut off
  from the cell whenever USB is connected ([LilyGO issue #128][lilygo128]) — a
  hardware fact, not a bug here. `adc_valid` goes to `0` and only `v5` keeps
  answering, and then it reports the **charger rail**, not the cell.
- **The modem's TX bursts sag VBAT.** A row captured during a publish reads low
  across every source at once. That is why `uptime_ms` and `gps_utc` are on the
  row: they let a sagging sample be lined up with what the device was doing.
- **`solar_mv` assumes a 2:1 divider**, which varies across board revisions —
  check it against `solar_raw` before trusting the millivolts.

[lilygo128]: https://github.com/Xinyuan-LilyGO/LilyGO-T-SIM7000G/issues/128

### Cost and caveats

- One ADC burst plus **one** extra `AT+CBC` per reporting cycle, and one appended
  line — negligible next to an acquire, and nothing at all in deep sleep.
- The file is capped at `kSdMaxBatteryLogRows` (20 000 ≈ 2.4 MB). The cap is
  checked once every 256 rows, not every row: enforcing it rewrites the file to
  keep the header, so it has to stay rare. The file can therefore overshoot the
  cap by up to 256 rows.
- A file whose first line is not the current header is **left alone** and the
  logger steps to `battery2.csv` … `battery9.csv`, so changing the columns never
  corrupts an older capture.
- With no card there are no rows and a warning; tracking carries on, like every
  other SD-backed subsystem.
- The origin of all this is the Arduino comparison rig in `../../BatteryTest/`
  (a standalone sketch outside this repo), which prints the same measurements as
  a live table over serial. Two deliberate differences here: one burst of samples
  feeds sources 1–4 (so `v2`/`v3` differ by *maths*, not by *samples*), and the
  trend window counts cycles rather than seconds.

---

## Charger-disconnect report

On the T-SIM7000G the pack sense pin is **cut off from the cell whenever USB
power is connected** ([LilyGO issue #128][lilygo128]). That is why `v1`–`v4` read
~0 on charge, and it has a consequence beyond the capture: while the charger is
in, the device genuinely cannot know the battery level. It reports the `0`
sentinel, the front end says "charging", and the first true reading of a trip
only comes into existence the moment the charger comes off.

A cycle that gets a position publishes that reading by itself. A cycle that gets
**no** position publishes nothing at all — and a car parked in a garage may never
get one. So the firmware watches for the edge:

- [`ChargerWatcher`](src/power/ChargerWatcher.h) remembers the charge-sense
  verdict from one cycle to the next and reports the **present → absent**
  transition. Only the edge counts; a device that is simply running on battery
  fires nothing.
- The state lives in **RTC memory**, the same trick `BootJournal` uses, because
  with `sleep_between` on the chip reboots between reports and an ordinary static
  would be wiped every cycle — the edge could then never be seen at all.
- A state that did *not* survive — a real power cut, or a device's first ever
  boot — is treated as **unknown** and fires nothing. A tracker switched on
  already unplugged has not just been unplugged.
- On the edge, **and only when this cycle found no position**, one more acquire
  is spent chasing one (`kUnplugFixTimeoutSeconds`, default 60 s; `0` disables
  it). A fix means a normal report goes out carrying the fresh level.

If that acquire also comes back empty, **nothing is published**. Re-sending the
last known position instead would be pointless: the API dedupes on
`(device, fix_time)` and keeps the row it already has, so the new battery value
would be silently discarded — and the device would still be told the fix was
stored.

Detection is once per reporting cycle, so an unplug is noticed up to one interval
(60 s by default) after it happens.

---

## Averaged position reports

A single GNSS solution carries several metres of noise, and the **first**
solution after a lock is the least settled of all — the receiver is still
converging when it first declares a fix. Publishing that one reading is what
makes a parked car's track wander.

So every report is built from **four positions instead of one**.
[`FixAverager`](src/gnss/FixAverager.h) acquires exactly as before, throws that
first fix away, and averages the next `kFixAverageSampleCount` readings:

```
   acquire ──▶ fix #1        DISCARDED  (first solution after the lock)
      1 s  ──▶ fix #2   ┐
      1 s  ──▶ fix #3   ├─▶  mean  ──▶  published, and stored on the card
      1 s  ──▶ fix #4   ┘
```

The discarded reading costs nothing extra: it is the fix the acquisition
produced anyway. **What it costs is ~3 s of awake time per cycle**
(`kFixAverageSampleCount × kFixAverageStepMs`), and never more — see the
partial-burst rule below.

**What is averaged, and what is not.** Latitude, longitude, altitude and speed
are meaned. Everything else — the **UTC timestamp**, course, the DOP figures,
satellite counts — is taken whole from the **last** accepted reading, so the
fields that are not averaged all come from one consistent solution rather than
being stitched together. The timestamp choice is load-bearing: the API dedupes
on `(device, fix time)` at second precision, so a report has to carry a real,
advancing UTC, and the freshest sample's can never collide with the previous
cycle's. Course is deliberately *not* averaged — it is a circular quantity, and
the naive mean of 359° and 1° is 180°, the exact opposite of the truth.

Longitudes are averaged as **differences from the first sample**, not as
absolutes, which is what keeps the result correct on the ±180° meridian (a plain
mean of −179.9 and +179.9 lands in the Gulf of Guinea).

**A short burst is normal, not an error.** A reading that comes back without a
fix is *skipped, never retried* — retrying would let a bad sky stretch the cycle
indefinitely — and so is one whose UTC repeats the previous sample, because the
modem solves at 1 Hz and re-reading inside the same second returns the identical
solution, which would silently weight it twice.

| Readings accepted | What gets published |
|---|---|
| 3 | the mean of the three |
| 2 | the mean of the two |
| 1 | that one reading |
| 0 | the acquisition fix, unaveraged — **a cycle never goes silent because the burst was unlucky** |

**The card holds the average too.** Averaging happens in place before anything
downstream sees the fix, so the SD queue, the retry queue and the diagnostic
battery CSV all carry the same averaged position — no raw sample is stored
anywhere, and an offline cycle stores exactly the bytes an online one would have
sent.

In a `kGnssDebug` build the burst reads with the NMEA satellite scan suppressed
(`readFix(fix, /*scanSatellites=*/false)`). That scan listens for
`kSatelliteScanMs` — 3 s — on every read, which would space the samples 4 s apart
and stretch the burst to a dozen seconds. The per-fix dump still prints, so the
log shows each sample followed by one `FixAverager` summary line.

Set **`kFixAverageEnabled = false`** to go back to publishing the raw
acquisition fix; the burst is then compiled out entirely.

---

## Peak accelerometer readings

The normal report carries **one instantaneous** accelerometer triple per cycle —
at the default 60 s interval, one sample a minute. That says almost nothing about
what the car did: braking, cornering and potholes all happen *between* two
reports and are simply never seen.

Set **`kAccelPeakEnabled = true`** and
[`AccelPeakTracker`](src/sensors/AccelPeakTracker.h) starts a small background
task that samples the sensor every `kAccelSampleIntervalMs` (default 1000 ms) and
keeps a running **per-axis maximum**. The ordinary report then carries that
maximum instead of a live reading, and the window restarts:

```
      report                                                  report
         │                                                       │
         │ * * * * * * * * * * * * * * * * * * * * * * * * * * * │
         │ each sample folded into the running max               │
         └───────────────────────────────────────────────────────┘
                       takePeak() → the report, window reset
```

**Nothing extra is published and nothing extra is queued** — it is the same one
report per `interval_s` as always, and the payload format is unchanged. Memory is
**O(1)**: one sample is kept, never a list, so the interval can be arbitrarily
long. The very first report after boot falls back to a live reading, since no
window has closed yet.

### Three things to know before you trust the numbers

| | |
|---|---|
| **The triple is a composite** | The three axes are tracked *independently*, so the reported X, Y and Z can come from three different moments — it is not a reading that ever occurred. The dashboard derives a magnitude as √(x²+y²+z²) from these, so **that line reads higher than any real sample**. Tracking the largest \|a\| and keeping that whole sample is the alternative; it was considered and not chosen |
| **Peaks clip at ±2 g** | The driver runs the sensor in its ±2 g range. Braking (~0.8 g) and cornering (~0.5 g) are comfortably inside; a sharp pothole saturates |
| **1000 ms under-samples badly** | The ADXL345 free-runs at 100 Hz, so a 1 s poll sees **one sample in a hundred** and misses most transients. `kAccelSampleIntervalMs = 100` is the value actually worth using — it costs one extra I2C read per 100 ms and nothing else |

`sleep_between` narrows what this can see: the chip is powered down between
reports, so the sampling task only covers the awake part of each cycle. The
firmware logs that once rather than overriding the server's setting.

Sharing the sensor with the main loop's `kGnssDebug` console block means two
tasks call `Adxl345::read()`, so that method takes a mutex (via
[`ScopedLock`](src/util/ScopedLock.h)); the accumulator inside the tracker takes
another. Nothing else in the firmware became concurrent — the delivery path is
still driven entirely from the main task.

---

## Build & flash

```bash
cp src/config/Config.example.h src/config/Config.h   # first time only, then edit
pio run                 # compile
pio run -t upload       # flash
pio device monitor      # watch the serial output (115200 baud)
```

> **Note:** the PlatformIO env is `ttgo-t7-v14-mini32` (same ESP32-WROVER-B
> module as the T-SIM7000G). The board name only affects flash/RAM layout, not
> the modem pins, which are set in `Config.h`.

### Flash usage / size

The full firmware (WiFi + lwIP + the mbedTLS/TLS stack for `wss://`) is large.
Out of the box ESP-IDF only gave the app a **1 MB** partition, so the build
reported **~99% flash used**. The project tunes these four settings to fix this:

| Setting | Where | Default | Now | Why |
|---------|-------|---------|-----|-----|
| Partition table | [`platformio.ini`](platformio.ini) `board_build.partitions` | `partitions_singleapp.csv` (1 MB app) | **`partitions_singleapp_large.csv`** (1.5 MB app) | Biggest win — gives the app 50% more room. |
| `CONFIG_ESPTOOLPY_FLASHSIZE` | [`sdkconfig.defaults`](sdkconfig.defaults) | `2MB` | **`4MB`** | The board actually ships with 4 MB flash — the stock config wasted half of it. |
| `CONFIG_COMPILER_OPTIMIZATION_*` | [`sdkconfig.defaults`](sdkconfig.defaults) | `DEBUG` (`-Og`) | **`SIZE`** (`-Os`) | Smaller code (~10–15%). |
| `CONFIG_NEWLIB_NANO_FORMAT` | [`sdkconfig.defaults`](sdkconfig.defaults) | off | **on** | Links the compact "nano" `printf`/`scanf`. |

### Main task stack

```
CONFIG_ESP_MAIN_TASK_STACK_SIZE=12288   # sdkconfig.defaults; stock is 3584
```

`app_main()` is not just wiring — the whole tracking loop runs on the **main**
task, so every RSA-OAEP encryption in [`PayloadCrypto`](src/crypto/PayloadCrypto.h)
happens on that task's stack. A single mbedTLS modular exponentiation on a
3072-bit key costs a few kB by itself, with the SD-queue and MQTT publish calls
stacked on top. With the stock 3584 B the **first fix** reliably panicked:

```
I (24150) main: Fix: 50.541373, 13.711591  0.0 km/h
***ERROR*** A stack overflow in task main has been detected.
```

12 KB leaves comfortable headroom (RAM is not the constraint on this board —
the build uses under 3% of it). If you switch to an RSA-4096 receiver key, keep
this at 12 KB or above.

[`AccelPeakTracker`](src/sensors/AccelPeakTracker.h) runs on its own task, but it
only does one I2C read and three comparisons per tick — no crypto, no files — so
2 KB is plenty there. That task only exists while `kAccelPeakEnabled` is on.

### Long filenames on the SD card

```
CONFIG_FATFS_LFN_HEAP=y     # sdkconfig.defaults; stock is CONFIG_FATFS_LFN_NONE
CONFIG_FATFS_MAX_LFN=255
```

FatFs ships with long-filename support **off**, which limits the card to classic
**8.3** names. Every file this firmware uses breaks that rule —
`queue.jsonl` (5-char extension), `settings.json` (4-char), the queue's head-offset
sidecar `queue.jsonl.idx` (two dots), and the sibling `.tmp` files
[`SdCard`](src/sdcard/SdCard.h) stages writes through (`queue.jsonl.tmp` — two
dots again). With LFN off the card mounts perfectly and then
every `fopen()` fails with `ENOENT`, because FatFs rejects the *name*, not the
card:

```
E (281850) SdCard: append: cannot open /sdcard/queue.jsonl
E (281850) FixForwarder: offline and SD queue write failed - fix lost
```

This is easy to miss for two reasons: the healthy online path in
[`FixForwarder`](src/sdcard/FixForwarder.h) never writes to the card, so the
queue only breaks during an outage; and the settings cache fails *silently* —
[`SdCard::readLines()`](src/sdcard/SdCard.h) treats "cannot open" as "no file
yet", so the device just falls back to the `Config.h` defaults on every boot.

`LFN_HEAP` keeps the name buffer on the heap rather than the (already tight)
main-task stack. If you ever turn LFN back off, rename both files to 8.3 **and**
change the temp-file naming to replace the extension instead of appending
`.tmp`.

> ⚠️ **Changing `sdkconfig.defaults` may not regenerate the sdkconfig.**
> PlatformIO does not always notice the edit. If `sdkconfig.ttgo-t7-v14-mini32`
> still shows the old value, delete that generated file and run `pio run` again.

> ⚠️ **The partition table lives in `platformio.ini`, not sdkconfig.**
> PlatformIO's ESP-IDF builder picks the partition CSV from
> `board_build.partitions` and **ignores** `CONFIG_PARTITION_TABLE_*`. Setting it
> only in sdkconfig has no effect on the size check or the flashed layout.
> The sdkconfig options are kept in [`sdkconfig.defaults`](sdkconfig.defaults) so
> they survive a `menuconfig` run or a framework upgrade.

Together these take the build from **~99% of 1 MB** down to **~69% of 1.5 MB**
(≈1,027 KB firmware, including the FAT/SD store-and-forward stack, the battery
method log and the remote-settings/deep-sleep paths). After pulling these changes
do a clean rebuild so the new flash size and partition layout take effect:

```bash
pio run -t fullclean
pio run
```

> ℹ️ **Nano `printf` caveat:** the nano formatter has reduced support for some
> conversions (e.g. `%ll`, full floating-point width/precision). The firmware's
> log lines are fine with it, but keep it in mind if you add new `printf`-style
> formatting.

> ⚠️ **Why the platform is pinned to `espressif32@6.9.0`.**
> [`platformio.ini`](platformio.ini) pins `platform = espressif32@6.9.0`
> (ESP-IDF 5.3) **on purpose — do not let it float to the latest version.**
> The newer `espressif32 @ 7.0.0` pulls in `framework-espidf 6.0.0`, whose
> bundled **`mqtt` (esp-mqtt) and `json` (cJSON) components ship without their
> sources**: the `mqtt` folder contains no `CMakeLists.txt` and the `json`
> component is absent entirely. Because [`src/CMakeLists.txt`](src/CMakeLists.txt)
> requires both — `mqtt` for `mqtt_client.h` and `json` for `cJSON.h` — the
> build aborts at the CMake configuration stage with *"Failed to resolve
> component 'mqtt'"*, before a single source file is compiled. Reinstalling the
> framework does **not** fix it; the published 6.0.0 package itself is
> incomplete. `6.9.0` bundles both components correctly, so the project builds
> with no source changes. Revisit the pin only once a later `espressif32`
> release is confirmed to bundle `mqtt` and `json` properly.

---

## How GNSS is driven (reference)

The SIM7000G exposes its GNSS engine over the same UART used for AT commands:

| AT command | Used by | Purpose |
|------------|---------|---------|
| `AT` / `ATE0` | `Sim7000Modem` | Liveness check / disable echo |
| `AT+CPOWD=1` | `Sim7000Modem::powerOff` | Graceful full power-down |
| `AT+CGNSPWR=1/0` | `enableGnss` / `disableGnss` | GNSS engine power |
| `AT+CGNSMOD=1,g,b,a` | `setConstellations` | Enable GLONASS/BeiDou/Galileo |
| `AT+CGNSINF` | `readFix` | One-shot position/speed/time line — called once per acquisition poll, then once per averaging sample |
| `AT+CGNSTST=1/0` | `readSatelliteCounts` | Stream NMEA (for per-system counts) |

First fix outdoors from cold can take **30 s – several minutes**; until then
`fix.hasFix()` is `false`. Make sure the GNSS antenna is connected and has a
clear view of the sky.

---

## Troubleshooting: "Modem did not respond after power-on"

`Sim7000Modem::powerOn()` logs this when no AT command was answered, which ends
the run (`GNSS init failed … Halting.`). It means **no `OK` came back over the
UART** — which is not the same as "the modem stayed off". The start-up sequence
is built to rule out the software-side causes on its own:

1. **Probe before pulsing.** The modem keeps its power state across an ESP32
   reset, so after a re-flash it is often *already on*. PWRKEY is a toggle, so
   pulsing a running modem would switch it **off**. `powerOn()` therefore probes
   first and only pulses if the modem is genuinely silent. The probe retries
   `AT` several times (`isResponsive(int)`), because a modem that is up but
   still emitting boot URCs (`RDY`, `+CPIN: READY`) will miss the first one.
2. **Baud-rate hunt.** `detectBaudRate()` tries `kModemBaudRate` and then each
   of `kModemBaudCandidates`, two `AT`s per rate (an autobaud modem spends the
   first one measuring the bit timing). The rate that answered is logged. This
   matters because SIM7000G firmware ships at 115200 as often as at 9600, and a
   wrong rate looks identical to a dead modem.
3. **Patience after the pulse.** The poll runs for 30 × 1 s, re-probing every
   candidate rate each second, and logs each failed attempt. The datasheet
   quotes ~4.5 s to a usable UART, but a cold module on a sagging supply is
   slower.

If it still fails, the cause is on the hardware side — the error log repeats
this checklist:

| Check | What to do |
|-------|------------|
| **Supply** | The SIM7000G draws **~2 A peaks**. USB alone frequently cannot boot it. Attach a charged LiPo to the JST connector — this is the most common cause. |
| **PWRKEY polarity** | Most boards idle HIGH and pulse LOW. Some revisions invert this through a transistor: set `kModemPwrKeyActiveLow = false` in `Config.h`. |
| **TX/RX** | `kModemTxPin` / `kModemRxPin` (`27` / `26`) are the T-SIM7000G factory pins; swapped wiring gives exactly this silence. |

> **Big external / protected packs and heat.** A protected Li-ion pack (one with
> a BMS / "load balancer") disconnects its own output on over-temperature,
> over-current or over-discharge, and many stay latched until a **charger voltage
> resets the protection FET** — so a device that "won't boot until you plug in the
> charger" after a hot day (a 60 °C+ car is beyond Li-ion's safe range) is the
> pack protecting itself, not a firmware fault, and `battery_pct` can read high at
> the moment it cuts off. There is no software low-battery shutdown in this
> firmware. Soften the ~2 A transmit sag with a bulk capacitor (1000–4700 µF)
> across VBAT, short/thick leads, and a BMS rated for the peak current — and watch
> `temp_c` to catch the heat before the cut-off.

A *garbled* log (stray bytes rather than silence) points at the baud rate; total
silence points at power or PWRKEY.

> **If the device restarted rather than never started**, read
> [`boot.log`](#boot-log) off the card first — a run of `reset=BROWNOUT` lines
> says the supply is sagging (the same ~2 A peaks as above), while
> `reset=POWERON` with `(RTC CLEARED)` says the rail went away entirely, which is
> a pack or a connector, not the modem.

---

## Troubleshooting: satellites in view, but never a fix

The engine runs, the report lists 20+ satellites in view, `Sats used` stays at
**0**, all DOP values stay at **0.0**, and `waitForFix` eventually gives up.

This is almost always an **RF problem, not a slow cold start**, and the "in
view" count is what misleads: it comes from the almanac and is reported with no
antenna attached at all. Check the **tracked** column and **Strongest signal**
in the satellite report (see [Debug output](#debug-output)) — those measure
actual received signal.

A real cold start looks different: tracked climbs 0 → 1 → 4 over a minute or
two, and the DOP values become non-zero *before* the fix appears. Flat zeros for
minutes mean nothing is being received.

| Symptom in the report | Cause | Fix |
|---|---|---|
| `tracked: 0`, `Strongest signal: 0 dB-Hz` | No RF reaching the receiver | See the three checks below |
| `tracked: 1-3`, signal `< 25 dB-Hz` | Indoors / obstructed. The ephemeris needs ~30 s of *continuous* clean signal to decode, so this never resolves | Move to open sky |
| `tracked: 4+`, signal `35+`, still no fix | Genuinely unusual | Check `AT+CGNSMOD` constellation settings |

When there is no signal at all, in order of likelihood:

1. **Wrong u.FL socket.** The T-SIM7000G has two, and ships with two
   similar-looking antennas. The GNSS antenna belongs in the socket nearest the
   SIM7000G module (marked `GPS`), *not* the LTE one.
2. **Wrong antenna type.** The GNSS antenna is the small square ceramic patch on
   a cable — an **active** antenna. The LTE whip will not work.
3. **Antenna power.** The active antenna's amplifier is fed from the modem's own
   GPIO4 via `AT+SGPIO=0,4,1,1`. `enableGnss()` checks this command's result and
   logs a warning if the modem rejected it, which some firmware builds do.
