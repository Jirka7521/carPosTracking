# Car Position Tracking — GNSS subsystem (ESP32 + SIM7000G)

Firmware for the **LilyGO TTGO T-SIM7000G** (ESP32-WROVER-B + SIMCom SIM7000G)
that reads the GNSS position from the modem's *integrated* GNSS receiver using
**all available constellations** (GPS, GLONASS, BeiDou, Galileo) and returns
**position, speed and time**.

It is written in **C++** on top of **ESP-IDF** (built with **PlatformIO**) and
is split into small, single-purpose classes so it is easy to read, extend and
reuse.

---

## Features

- 📍 Read latitude, longitude, altitude, **speed** and **UTC time**.
- 🛰️ Uses GPS + GLONASS + BeiDou + Galileo together for a faster, better fix.
- 🔋 **Power the whole modem off** between reads to minimise battery drain
  (plus a lighter "GNSS engine only" off switch).
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
│   └── Config.h             ← ★ EDIT ME: pins, baud, constellations, debug flag
│
├── serial/
│   ├── SerialPort.h/.cpp    ← Thin wrapper over the ESP-IDF UART driver
│
├── modem/
│   ├── Sim7000Modem.h/.cpp  ← Modem power (PWRKEY) + AT command transport
│
└── gnss/
    ├── GnssData.h           ← Plain data structs (GnssFix, GnssSatelliteCounts…)
    ├── CgnsinfParser.h/.cpp ← Decodes the AT+CGNSINF position reply
    ├── NmeaParser.h/.cpp    ← Counts satellites per constellation (NMEA GSV)
    └── GnssModule.h/.cpp    ← ★ The high-level API you call from your code
```

### How the layers fit together

```
        ┌─────────────────────────────┐
        │          main.cpp           │   your application
        └──────────────┬──────────────┘
                       │ uses
        ┌──────────────▼──────────────┐
        │          GnssModule         │   high-level GNSS API
        │  begin / readFix / power…   │
        └───────┬───────────────┬─────┘
         uses   │               │ uses
   ┌────────────▼───┐   ┌───────▼──────────────┐
   │ Sim7000Modem   │   │ CgnsinfParser        │  text → GnssFix
   │ power + AT I/O │   │ NmeaParser           │  GSV  → sat counts
   └────────┬───────┘   └──────────────────────┘
            │ uses
   ┌────────▼───────┐
   │   SerialPort   │   raw UART bytes
   └────────────────┘
```

Each class has exactly one job, which makes the system easy to follow and to
test:

| Class | Responsibility |
|-------|----------------|
| `SerialPort` | Send/receive bytes over a UART; read a line. Nothing else. |
| `Sim7000Modem` | Turn the modem on/off (PWRKEY) and run AT commands. |
| `CgnsinfParser` | Turn one `+CGNSINF:` line into a `GnssFix`. |
| `NmeaParser` | Count satellites per constellation from `GSV` sentences. |
| `GnssModule` | The friendly API: configure, read a fix, manage power, debug. |

---

## Configuration

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

> All settings are `constexpr`, so when `kGnssDebug` is `false` the debug code
> is removed by the compiler — zero runtime cost in production.

---

## Using it in your own code

```cpp
#include "config/Config.h"
#include "gnss/GnssModule.h"
#include "modem/Sim7000Modem.h"
#include "serial/SerialPort.h"

static SerialPort   serial(config::kModemUartPort, config::kModemTxPin,
                           config::kModemRxPin, config::kModemBaudRate);
static Sim7000Modem modem(serial, config::kModemPwrKeyPin);
static GnssModule   gnss(modem);

gnss.begin();                 // power on + enable engine + select constellations

GnssFix fix;
if (gnss.readFix(fix) && fix.hasFix()) {
    double lat   = fix.position.latitudeDeg;
    double lon   = fix.position.longitudeDeg;
    double speed = fix.speedKmph;
    // fix.time.{year,month,day,hour,minute,second}
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
pio run                 # compile
pio run -t upload       # flash
pio device monitor      # watch the serial output (115200 baud)
```

> **Note:** the PlatformIO env is `ttgo-t7-v14-mini32` (same ESP32-WROVER-B
> module as the T-SIM7000G). The board name only affects flash/RAM layout, not
> the modem pins, which are set in `Config.h`.

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
