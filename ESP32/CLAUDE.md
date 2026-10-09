# CLAUDE.md — ESP32 GNSS firmware

Firmware specifics. The shared rules — plan first, minimal diffs, git workflow,
subagents, and the cross-subsystem contracts — are in [../CLAUDE.md](../CLAUDE.md).

## What this is

Firmware for the **LilyGO TTGO T-SIM7000G** (ESP32-WROVER-B + SIMCom SIM7000G). It
takes a GNSS fix from the modem's receiver (GPS/GLONASS/BeiDou/Galileo), seals it with
the server's RSA public key (RSA-OAEP + AES-256-GCM) and publishes it over MQTT on
`wss://`. Only the API holds the matching private key. Around that: battery reporting,
an ADXL345 motion wake, deep sleep, an SD-card store-and-forward queue, status / Last
Will messages, and remote settings + schedules pushed from the API.

**C++ on ESP-IDF v5.3, built with PlatformIO — not Arduino.** There are no `.ino` files
and no `setup()`/`loop()`; the entry point is `extern "C" void app_main(void)` in
`src/main.cpp`. Read [README.md](README.md) (architecture, layer diagram, AT commands,
config table) before non-trivial changes.

## Layout

```
src/
├── main.cpp     app_main(): wires the classes together and runs the loop
├── config/      Config.example.h (committed template) · Config.h (git-ignored, secrets)
├── serial/      UART wrapper
├── modem/       SIM7000G power (PWRKEY) + AT transport
├── gnss/        CGNSINF / NMEA parsing, fix model, fix averaging
├── crypto/      PayloadCrypto (seals fixes) · AckCrypto (opens delivery acks)
├── mqtt/        client, telemetry / status / presence publishing, ack watcher
├── wifi/        optional WiFi station
├── power/       battery sampling + reporting, charger, low-battery guard,
│                power switch, deep sleep, boot journal
├── sensors/     ADXL345 accelerometer + peak tracking
├── motion/      MotionTracker — motion-wake state machine
├── sdcard/      SD store-and-forward: fix queue, retry queue, index, forwarder
├── settings/    remote settings (codec / store / applier) and schedules
│                (codec / store / evaluator / selector)
├── status/      indicator LEDs
└── util/        ScopedLock, device clock / civil time, statistics
```

- One feature per folder, one class per `Name.h` + `Name.cpp`. A new feature gets a
  **new folder**.
- Nothing to register: [src/CMakeLists.txt](src/CMakeLists.txt) globs `src/**/*.{c,cpp}`
  and makes `src/` the include root — include by folder path:
  `#include "gnss/GnssModule.h"`.

## Build, flash, monitor

```bash
cp src/config/Config.example.h src/config/Config.h   # first time only, then fill in
pio run                 # compile — the only automated check (test/ is empty)
pio run -t upload       # flash
pio device monitor      # serial @ 115200
pio run -t fullclean    # needed after partition / flash-size changes
```

- `pio` is often not on the shell PATH (the VS Code extension doesn't add it); use
  `~/.platformio/penv/Scripts/pio` instead.
- Env: **`ttgo-t7-v14-mini32`**.
- **Don't float the platform version.** [platformio.ini](platformio.ini) pins
  `espressif32@6.9.0` on purpose: 7.0.0 ships `esp-mqtt` / `cJSON` without sources and
  the build fails at CMake configure.
- Never run `upload` or `monitor` unless asked — the board may not be connected, and
  flashing is the user's call.

## Configuration & secrets

- Every tunable is a `constexpr` in `src/config/Config.h`: pins, timeouts, credentials,
  the receiver's RSA public key, and the `k…Enabled` feature flags. Because they are
  `constexpr`, a disabled path is compiled out at zero cost — keep it that way for new
  optional features.
- **`Config.h` is git-ignored and holds real secrets** — never commit it or print its
  values. The committed [Config.example.h](src/config/Config.example.h) carries
  placeholders only.
- **Adding a setting** = add it to `Config.h` **and** `Config.example.h` (placeholder),
  document it in the README's configuration table, then run `dotnet build` in
  [../API/CarPosAPI/](../API/CarPosAPI/) and commit the refreshed
  `ConfigTemplate.h.txt` with it (the build warns `CARPOS001` while it is stale). The
  dashboard's parameter table derives from the template; nothing else needs editing.

## Conventions (match the existing code)

- `#pragma once`; 2-space indent, no tabs; lines ~80–100 cols.
- Naming: `PascalCase` types, `camelCase` methods and locals, trailing-underscore
  private members (`modem_`), `k`-prefixed `constexpr` config (`kModemBaudRate`),
  `SCREAMING_CASE` only for macros.
- Every header opens with a banner block: the class's single responsibility, its
  collaborators, and the reasoning behind non-obvious choices — see
  [src/status/StatusLed.h](src/status/StatusLed.h) or
  [src/gnss/GnssModule.h](src/gnss/GnssModule.h).
- Classes **borrow** collaborators by reference through the constructor (no ownership);
  single-argument constructors are `explicit`.
- Logging: `ESP_LOGI/W/E` with a file-local `static const char* TAG`.
- Errors: return `bool` / a status and log — no exceptions. A failing optional subsystem
  (WiFi, MQTT, SD, accelerometer) logs a warning and tracking carries on; never halt
  the app for a non-essential failure.
- Anything that crosses to the API (crypto envelopes, settings JSON, schedules, status
  messages, new topics) is a contract — see the table in [../CLAUDE.md](../CLAUDE.md).

## Gotchas

- **Flash is tight.** `firmware.bin` is already over 1 MB of the 1.5 MB app partition
  (WiFi + mbedTLS for `wss://`). Check the size line `pio run` prints after every change.
  The partition table is set by `board_build.partitions` in `platformio.ini` —
  PlatformIO ignores `CONFIG_PARTITION_TABLE_*` in sdkconfig. Size tuning lives in
  [sdkconfig.defaults](sdkconfig.defaults).
- **Nano printf** (`CONFIG_NEWLIB_NANO_FORMAT`): limited `%ll` and float width /
  precision. Keep format strings simple.
- **Deep sleep reboots** — `app_main()` runs again from the top on every wake; nothing on
  the stack or heap survives. State that must persist goes to RTC memory
  (`RTC_DATA_ATTR`, as `BootJournal` and `MotionTracker` do) or to the SD card (as
  `SettingsStore` does).
- **The first GNSS fix** from cold takes 30 s to several minutes; `hasFix()` is false
  until then. Not a bug.
- **Mosquitto silently drops** messages on a topic the device has no ACL grant for — it
  still ACKs the subscribe. A new topic is not done until the broker ACL grants it.
- **Hardware behaviour can't be verified from here.** After `pio run` passes, list what
  the user should check on the device (serial log lines, LEDs, what the dashboard shows).
