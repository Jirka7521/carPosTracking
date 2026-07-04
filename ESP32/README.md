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
- 🔋 **Power the whole modem off** between reads to minimise battery drain
  (plus a lighter "GNSS engine only" off switch).
- 📶 **Optional WiFi** (station mode): connect to a network with one flag, or
  disable it entirely. Credentials are kept out of Git.
- 📡 **Optional MQTT publishing**: each fix is pushed to a broker over secure
  WebSocket (`wss://`), with automatic background (re)connection.
- 🔒 **End-to-end encryption** of every payload (RSA-OAEP-SHA256 + AES-256-GCM):
  only the holder of the receiver's private key can read a position — the broker
  cannot.
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
│
└── mqtt/
    ├── MqttClient.h/.cpp        ← Broker transport (esp-mqtt over wss/TLS)
    └── TelemetryPublisher.h/.cpp← Fix → JSON → encrypt → publish
```

### How the layers fit together

```
        ┌─────────────────────────────┐
        │          main.cpp           │   your application
        └───────┬─────────────────┬───┘
          uses  │                 │ uses
   ┌────────────▼────────┐  ┌─────▼────────────────┐
   │      GnssModule     │  │  TelemetryPublisher  │  fix → JSON → publish
   │ begin/readFix/power…│  └──┬───────────────┬───┘
   └───────┬─────────┬───┘     │ uses          │ uses
    uses   │         │ uses    │         ┌─────▼──────────┐
   ┌───────▼──────┐ ┌▼────────────┐      │ PayloadCrypto  │  seal envelope
   │ Sim7000Modem │ │CgnsinfParser│      │ RSA-OAEP+GCM   │
   │ power + AT   │ │NmeaParser   │      └────────────────┘
   └───────┬──────┘ └─────────────┘      ┌────────────────┐
    uses   │                             │   MqttClient   │  → broker (wss)
   ┌───────▼──────┐                      │  esp-mqtt/TLS  │
   │  SerialPort  │  raw UART bytes      └────────────────┘
   └──────────────┘
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
| `PayloadCrypto` | Seal a plaintext string into the encrypted JSON envelope. |
| `MqttClient` | Connect to the broker (esp-mqtt/TLS) and publish opaque bytes. |
| `TelemetryPublisher` | Format a `GnssFix` as JSON, encrypt it, publish it. |

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
| `kModemBaudRate` | `9600` | SIM7000G default |
| `kModemPwrKeyPin` | `4` | PWRKEY (power on/off) |
| `kEnableGps/Glonass/Beidou/Galileo` | all `true` | Constellations to use |
| **`kGnssDebug`** | `true` | **Turn the serial debug report on/off** |
| `kFixPollIntervalMs` | `5000` | Delay between reads in the example loop |
| `kSatelliteScanMs` | `3000` | How long to listen to NMEA when counting sats |
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
| `kReceiverPublicKeyPem` | — | **Receiver's RSA public key** (encrypts the payload) |

> All settings are `constexpr`, so when `kGnssDebug` is `false` the debug code
> is removed by the compiler — zero runtime cost in production. Likewise, when
> `kWifiEnabled` is `false` the WiFi connect code is dropped entirely, and when
> `kMqttEnabled` is `false` the publish path is skipped.

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
GnssFix ──(TelemetryPublisher formats)──► plaintext JSON
        ──(PayloadCrypto seals)─────────► encrypted envelope
        ──(MqttClient publishes)────────► broker topic  (devices/GNSSxx)
```

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

- `powerOffModule()` / `powerOnModule()` — switch the **entire modem** off/on
  via PWRKEY. Lowest current draw; re-enabling takes a few seconds (modem boot).
- `disableGnss()` / `enableGnss()` — switch only the **GNSS engine**. Faster to
  toggle, but the modem itself keeps running (higher idle current).

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
----------- SATELLITES IN VIEW ----------
  GPS     (USA)    : 8
  GLONASS (Russia) : 5
  BeiDou  (China)  : 6
  Galileo (Europe) : 4
  TOTAL            : 23
-----------------------------------------
```

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
reported **~99% flash used**. The project tunes four settings to fix this:

| Setting | Where | Default | Now | Why |
|---------|-------|---------|-----|-----|
| Partition table | [`platformio.ini`](platformio.ini) `board_build.partitions` | `partitions_singleapp.csv` (1 MB app) | **`partitions_singleapp_large.csv`** (1.5 MB app) | Biggest win — gives the app 50% more room. |
| `CONFIG_ESPTOOLPY_FLASHSIZE` | [`sdkconfig.defaults`](sdkconfig.defaults) | `2MB` | **`4MB`** | The board actually ships with 4 MB flash — the stock config wasted half of it. |
| `CONFIG_COMPILER_OPTIMIZATION_*` | [`sdkconfig.defaults`](sdkconfig.defaults) | `DEBUG` (`-Og`) | **`SIZE`** (`-Os`) | Smaller code (~10–15%). |
| `CONFIG_NEWLIB_NANO_FORMAT` | [`sdkconfig.defaults`](sdkconfig.defaults) | off | **on** | Links the compact "nano" `printf`/`scanf`. |

> ⚠️ **The partition table lives in `platformio.ini`, not sdkconfig.**
> PlatformIO's ESP-IDF builder picks the partition CSV from
> `board_build.partitions` and **ignores** `CONFIG_PARTITION_TABLE_*`. Setting it
> only in sdkconfig has no effect on the size check or the flashed layout.
> The sdkconfig options are kept in [`sdkconfig.defaults`](sdkconfig.defaults) so
> they survive a `menuconfig` run or a framework upgrade.

Together these take the build from **~99% of 1 MB** down to **~58% of 1.5 MB**
(≈896 KB firmware). After pulling these changes do a clean rebuild so the new
flash size and partition layout take effect:

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
