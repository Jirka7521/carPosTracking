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
- 🧭 **ADXL345 accelerometer** (GY-291) over I2C: the raw instantaneous X/Y/Z
  acceleration (in g) rides along with every position report.
- 🔋 **Battery monitor**: pack state of charge from the modem's `AT+CBC`, mapped
  through a **Li-ion discharge curve** (not a straight line), plus a charge-sense
  pin (GPIO35) — a value of `0` is the agreed "charging" sentinel. Also reports
  the **modem die temperature** (`AT+CPMUTEMP`, published as `temp_c`).
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
  and only ciphertext ever touches the card.
- ⚙️ **Remote settings over MQTT**: the reporting interval and a
  power-down-between-reports flag are pushed from the broker on a config topic,
  validated, and cached on the SD card so they survive a reboot with no network.
- 😴 **Deep sleep between reports**: when told to, the firmware powers the modem
  right down (GNSS engine, antenna amplifier and LTE PA all go with it) and puts
  the ESP32 into deep sleep for the rest of the interval.
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
│   └── GnssModule.h/.cpp    ← ★ The high-level API you call from your code
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
│   └── FixForwarder.h/.cpp  ← Publish-now-or-store; drain the backlog as a burst
│
├── settings/
│   ├── DeviceSettings.h/.cpp ← The two runtime knobs, validated & clamped
│   ├── SettingsCodec.h/.cpp  ← DeviceSettings ⇄ the config JSON (one format)
│   ├── SettingsStore.h/.cpp  ← Cache them, in the clear, on the SD card
│   └── RemoteSettings.h/.cpp ← Subscribe to the config topic; apply & persist
│
└── power/
    ├── BatteryData.h              ← Plain BatteryStatus struct (percent + charging)
    ├── BatteryMonitor.h/.cpp      ← Pack % via AT+CBC + charge-sense on GPIO35
    └── DeepSleepController.h/.cpp ← Ordered shutdown + wake sources + deep sleep
```

### How the layers fit together

```
        ┌─────────────────────────────┐
        │          main.cpp           │   your application
        └───────┬─────────────────┬───┘
          uses  │                 │ uses
   ┌────────────▼────────┐  ┌─────▼──────────────┐
   │      GnssModule     │  │    FixForwarder    │  publish now, or store
   │ begin/readFix/power…│  │ process / drain    │  & burst on reconnect
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
| `Adxl345` | I2C driver: configure the ADXL345 and return one X/Y/Z sample (g). |
| `BatteryMonitor` | Pack % (Li-ion curve over the modem's `AT+CBC`) + charging detection (GPIO35). |
| `PayloadCrypto` | Seal a plaintext string into the encrypted JSON envelope (and stamp its `id`). |
| `AckCrypto` | The inverse: open an ack sealed to this device's own private key. |
| `MqttClient` | Connect to the broker (esp-mqtt/TLS); publish, subscribe, confirm QoS-2 delivery. |
| `TelemetryPublisher` | Format a `TelemetrySample` as JSON and encrypt it (`sealSample`); publish one. |
| `AckWatcher` | Collect the API's per-envelope verdicts; answer "was this fix actually stored?". |
| `SdCard` | Mount/format the microSD (FAT) and read/append/trim/rewrite files. |
| `FixQueue` | Persistent FIFO of encrypted envelopes on the card (with a size cap). |
| `RetryQueue` | Fixes the API rejected, with a next-attempt time and a give-up age. |
| `FixForwarder` | Publish a fix (plus any backlog) or store it; drain the queue as a burst. |
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
| `kFixAcquireTimeoutSeconds` | `180` | Give up waiting for a fix after this long |
| `kFixPollStepMs` | `2000` | Gap between `AT+CGNSINF` polls while acquiring |
| **`kAdxlEnabled`** | `true` | **Enable/disable the ADXL345 accelerometer** |
| `kI2cSdaPin` / `kI2cSclPin` | `21` / `22` | I2C data / clock GPIOs |
| `kI2cClockHz` | `400000` | I2C bus speed (fast mode) |
| `kAdxlI2cAddress` | `0x53` | ADXL345 address (CS→3V3, SDO→GND) |
| `kAdxlInt1Pin` / `kAdxlInt2Pin` | `32` / `33` | INT pins — reserved, interrupts not used yet |
| **`kBatteryEnabled`** | `true` | **Enable/disable the battery monitor** |
| `kBatteryChargeSensePin` | `35` | Charge-sense ADC pin; reads ~0 while charging |
| `kBatteryChargeAdcThreshold` | `200` | Raw ADC counts below which = charging (report `0`) |
| `kBatteryEmptyMv` / `kBatteryFullMv` | `3300` / `4200` | Clamp ends of the Li-ion SoC curve (≤empty→1 %, ≥full→100 %) |
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
| `kAckEnabled` | `false` | Wait for the API to confirm a fix was stored before dropping it |
| `kAckTopic` | `devices/GNSSXX/ack` | Topic the API publishes its delivery verdicts to |
| `kAckTimeoutMs` | `10000` | Wait for the API's verdict (covers decrypt + validate + DB write) |
| `kDeviceAckPrivateKeyPem` | — | **This device's RSA private key (secret)** — decrypts the acks |
| `kDefaultSendIntervalSeconds` | `60` | Interval used until the broker says otherwise |
| `kDefaultSleepBetweenSends` | `false` | Sleep flag used until the broker says otherwise |
| `kMinSendIntervalSeconds` | `5` | Lower clamp on a broker-supplied `interval_s` |
| `kMaxSendIntervalSeconds` | `86400` | Upper clamp on a broker-supplied `interval_s` |
| **`kSdEnabled`** | `true` | **Enable/disable the microSD store-and-forward queue** |
| `kSdSpiHost` | `SPI2_HOST` | SPI peripheral the card is wired to (HSPI) |
| `kSdPinMiso/Mosi/Sclk/Cs` | `2/15/14/13` | T-SIM7000G microSD SPI pins |
| `kSdMountPoint` | `/sdcard` | FAT mount point |
| `kSdQueueFilePath` | `/sdcard/queue.jsonl` | Line-delimited queue file (one envelope per line) |
| `kSdSettingsFilePath` | `/sdcard/settings.json` | Cached runtime settings (**plaintext**) |
| `kSdMaxQueuedFixes` | `20000` | Cap on stored fixes; oldest are dropped past this |
| `kSdMaxBurstFixes` | `40` | Max envelopes per burst message (RAM/MQTT safety bound) |
| `kSdRetryFilePath` | `/sdcard/retry.jsonl` | Fixes the API **rejected**, awaiting a scheduled retry |
| `kRetryIntervalHours` | `24` | How long to wait between attempts on a rejected fix |
| `kRetryMaxAgeHours` | `168` | Give up on a fix still refused after this long (`0` = never) |
| `kSdMaxRetryEntries` | `2000` | Cap on the retry file; oldest are dropped past this |
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
```

- **Same encryption as transmit.** What is stored is the *exact* envelope that
  would have been sent (`RSA-OAEP-SHA256 + AES-256-GCM`), one per line in
  `queue.jsonl`. A lost/stolen card therefore leaks nothing — the device holds
  only the public key and cannot even read back its own stored positions.
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

Set `kAckEnabled = false` to restore the old behaviour exactly: the broker ack
becomes the only confirmation, and nothing is written to `retry.jsonl`.

---

## Remote settings (broker → device)

Two things about the device are tunable at runtime, from the broker, without a
reflash: **how often it reports a position**, and **whether it powers itself down
in between**. The device subscribes to `kConfigTopic`
(`devices/GNSS01/config`) and expects a small **plaintext** JSON document:

```json
{ "interval_s": 60, "sleep_between": true }
```

| Field | Type | Meaning |
|-------|------|---------|
| `interval_s` | number | Seconds between position reports. Clamped to `[kMinSendIntervalSeconds, kMaxSendIntervalSeconds]`. |
| `sleep_between` | boolean | Power the modem down and deep-sleep the ESP32 between reports. |

Either field may be omitted; what is absent is simply left as it was.

### ⚠️ Publish it **retained**

```bash
mosquitto_pub -h jimajer.cz -t 'devices/GNSS01/config' -r \
              -m '{"interval_s":60,"sleep_between":true}'
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

### Long filenames on the SD card

```
CONFIG_FATFS_LFN_HEAP=y     # sdkconfig.defaults; stock is CONFIG_FATFS_LFN_NONE
CONFIG_FATFS_MAX_LFN=255
```

FatFs ships with long-filename support **off**, which limits the card to classic
**8.3** names. Every file this firmware uses breaks that rule —
`queue.jsonl` (5-char extension), `settings.json` (4-char), and the sibling
`.tmp` files [`SdCard`](src/sdcard/SdCard.h) stages writes through
(`queue.jsonl.tmp` — two dots). With LFN off the card mounts perfectly and then
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

Together these take the build from **~99% of 1 MB** down to **~64% of 1.5 MB**
(≈983 KB firmware, including the FAT/SD store-and-forward stack and the
remote-settings/deep-sleep paths). After pulling these changes do a clean rebuild
so the new flash size and partition layout take effect:

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
| `AT+CGNSINF` | `readFix` | One-shot position/speed/time line |
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
