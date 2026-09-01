# CLAUDE.md — ESP32 GNSS firmware

Guidance for Claude Code when working in this folder (`ESP32/`). These
instructions override default behaviour — follow them exactly.

---

## ⭐ Working agreement (read first, every time)

1. **Confirm the assignment before writing code.** Restate what you understood
   the task to be, ask any clarifying questions, and **present a short plan**.
   Wait for the go-ahead before you start editing source files. Do not jump
   straight into code.
2. **Comment generously.** Explain *why*, not just *what*. Match the existing
   banner-style header comments and the density of the surrounding code.
3. **One class per file; organise code into classes and methods.** Every class
   gets its own `.h` **and** `.cpp` pair, placed in the matching feature folder
   under `src/`. No free functions doing real work, no multi-class files.
4. **Update every README after any change.** When behaviour, config, structure,
   or build steps change, update **all** affected `README.md` files (this
   folder's [`README.md`](README.md) and any sibling project READMEs the change
   touches, e.g. [`../DESKTOP/README.md`](../DESKTOP/README.md),
   [`../README.md`](../README.md)) in the same change.
5. **Use best coding practices throughout** — see the checklist near the bottom.
6. **Never change what doesn't need to be changed.** Touch only what the task
   requires. No drive-by reformatting, renaming, reordering, or "while I'm here"
   edits; no reflowing unrelated lines; keep diffs minimal and focused so they
   are easy to review. If you spot something worth improving outside the task,
   mention it — don't silently change it.

---

## What this project is

Firmware for the **LilyGO TTGO T-SIM7000G** (ESP32-WROVER-B + SIMCom SIM7000G).
It reads a GNSS fix (position, speed, UTC time) from the modem's integrated
receiver using all constellations (GPS/GLONASS/BeiDou/Galileo), then
**end-to-end encrypts** each fix and **publishes it to an MQTT broker** over
secure WebSocket (`wss://`). The broker only ever sees ciphertext; the
[desktop companion](../DESKTOP/README.md) holds the private key and decrypts.

- **Language / framework:** C++ on **ESP-IDF** (v5.3), built with **PlatformIO**.
- **Design:** small, single-purpose classes, one per file, heavily commented.

Read [`README.md`](README.md) for the full architecture, the layer diagram, and
the AT-command reference before making non-trivial changes.

---

## Project layout

```
src/
├── main.cpp                 ← app entry (app_main): wires classes together & loops
├── config/
│   ├── Config.example.h     ← committed template (NO secrets)
│   └── Config.h             ← git-ignored: real pins, WiFi/MQTT creds, keys, flags
├── serial/     SerialPort              ← UART wrapper
├── modem/      Sim7000Modem            ← modem power (PWRKEY) + AT transport
├── wifi/       WifiManager             ← optional WiFi station
├── gnss/       GnssData / CgnsinfParser / NmeaParser / GnssModule
├── crypto/     PayloadCrypto / AckCrypto  ← RSA-OAEP + AES-256-GCM envelope
├── mqtt/       MqttClient / TelemetryPublisher / TelemetrySample / AckWatcher
├── power/      AdcSampler / BatteryMonitor / BatteryMethods / BatteryReporter
│               BatteryCsvLogger / ChargerWatcher / BootJournal
│               DeepSleepController
├── sensors/    Adxl345 / AccelPeakTracker  ← ADXL345 accelerometer
├── sdcard/     SdCard / FixQueue / RetryQueue / QueueIndex / FixForwarder
├── settings/   DeviceSettings / SettingsStore / SettingsCodec / SettingsApplier
│               RemoteSettings          ← the broker-supplied runtime config
└── util/       ScopedLock              ← small shared helpers
```

Each folder is one feature; each class is `Name.h` + `Name.cpp`. Add a new
feature as a **new folder** with its own class(es), and register nothing by
hand — [`src/CMakeLists.txt`](src/CMakeLists.txt) globs `src/**/*.{c,cpp}`
automatically and exposes `src/` as an include root, so include headers by their
folder-qualified path: `#include "gnss/GnssModule.h"`.

---

## Build, flash, monitor

```bash
cp src/config/Config.example.h src/config/Config.h   # first time only, then edit
pio run                 # compile
pio run -t upload       # flash
pio device monitor      # serial monitor @ 115200 baud
pio run -t fullclean    # clean rebuild (needed after partition/flash-size changes)
```

- PlatformIO env: **`ttgo-t7-v14-mini32`**.
- **Do not float the platform version.** [`platformio.ini`](platformio.ini) pins
  `platform = espressif32@6.9.0` on purpose — 7.0.0 ships `esp-mqtt`/`cJSON`
  without sources and the build fails at CMake config. See the README note.
- After a build, prefer verifying it **compiles** (`pio run`) before claiming a
  change is done — this is firmware, there is no quick unit run for most of it.

---

## Configuration & secrets

- **All tunables are `constexpr` in [`src/config/Config.h`](src/config/Config.h)**
  (pins, feature flags, timeouts, WiFi/MQTT credentials, the receiver's RSA
  public key). Because they are `constexpr`, disabled paths (`kGnssDebug`,
  `kWifiEnabled`, `kMqttEnabled` = `false`) are compiled out — zero runtime cost.
- **`Config.h` is git-ignored and holds secrets. Never commit it, never print its
  secret values, never paste credentials anywhere else.** The committed
  [`Config.example.h`](src/config/Config.example.h) carries empty placeholders.
- **When you add a new setting**, add it to *both* `Config.h` **and**
  `Config.example.h` (placeholder only), and document it in the README's config
  table. That is the whole list — the API's provisioning template and the
  dashboard's reference table both derive from `Config.example.h` now and need no
  edit. The one follow-up is to run `dotnet build` in [`../API/`](../API/) and
  commit the refreshed
  [`ConfigTemplate.h.txt`](../API/CarPosAPI/Services/Provisioning/ConfigTemplate.h.txt)
  alongside your change; the build warns (`CARPOS001`) when it is behind.

---

## Coding conventions (match the existing code)

- **One class per file**, `.h` + `.cpp`, in the feature folder. Headers use
  `#pragma once`.
- **Naming:** `PascalCase` types, `camelCase` methods/locals, trailing-underscore
  private members (`modem_`, `nmea_`), `k`-prefixed `constexpr` config
  (`kModemBaudRate`), `SCREAMING_CASE` only for macros.
- **Indentation:** 2 spaces, no tabs. Keep lines reasonable (~80–100 cols) to
  match the surrounding files.
- **Comments:** banner block at the top of each header describing the class's one
  job and its collaborators (see [`GnssModule.h`](src/gnss/GnssModule.h) as the
  model); inline comments explain the *why*.
- **Ownership:** classes **borrow** collaborators via references passed to the
  constructor (they do not own them); mark single-arg constructors `explicit`.
- **Logging:** use `ESP_LOGI/W/E` with a file-local `static const char* TAG`.
- **Errors:** return `bool`/status and log, following the existing pattern;
  don't throw. A failed optional subsystem (WiFi/MQTT) logs a warning and lets
  tracking continue — don't halt the whole app for a non-essential failure.
- **Encryption parity:** [`PayloadCrypto`](src/crypto/PayloadCrypto.h) must stay
  byte-for-byte compatible with the desktop
  [`crypto_box.py`](../DESKTOP/crypto_box.py). Any change to the envelope format,
  algorithms, or encoding must be mirrored on both sides — call this out
  explicitly and update both READMEs.

---

## Best-practices checklist (apply to every change)

- [ ] Assignment confirmed and a plan agreed **before** coding.
- [ ] New logic lives in an appropriately-scoped class/method, one class per file.
- [ ] Code is clearly and thoroughly commented (why, not just what).
- [ ] Style matches the surrounding code (naming, 2-space indent, banner headers).
- [ ] No secrets added to tracked files; new settings mirrored into
      `Config.example.h`.
- [ ] Feature flags keep disabled paths compiled out where it makes sense.
- [ ] `pio run` compiles cleanly (watch flash usage — the firmware is large).
- [ ] **All affected `README.md` files updated** in the same change.
- [ ] Change is self-contained and easy to review; **only what the task needs
      was touched** — no unrelated churn, reformatting, or renames.

---

## Gotchas

- **Flash is tight.** Full firmware (WiFi + TLS/mbedTLS for `wss://`) is ~896 KB.
  The partition table is set via `board_build.partitions` in
  [`platformio.ini`](platformio.ini) (**not** sdkconfig — PlatformIO ignores
  `CONFIG_PARTITION_TABLE_*`). Size tuning lives in
  [`sdkconfig.defaults`](sdkconfig.defaults). Keep an eye on the size report.
- **Nano `printf`** is enabled (`CONFIG_NEWLIB_NANO_FORMAT`): reduced support for
  `%ll` and full float width/precision. Keep new format strings simple.
- **First GNSS fix** from cold can take 30 s to several minutes; `fix.hasFix()`
  is `false` until then. Not a bug.
- This is **ESP-IDF**, not Arduino — there are no `.ino` files and no
  `setup()`/`loop()`; the entry point is `extern "C" void app_main(void)`.
