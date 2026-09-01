#pragma once
#include <cstddef>
#include <cstdint>

#include "driver/spi_common.h"
#include "driver/uart.h"

// =============================================================================
//  Config.example.h  -  Committed template (NO secrets).
// -----------------------------------------------------------------------------
//  This is the version-controlled template for `Config.h`. The real `Config.h`
//  holds your private WiFi credentials and is git-ignored so it never reaches
//  GitHub.
//
//  First-time setup:
//      cp src/config/Config.example.h src/config/Config.h
//  then open Config.h and fill in kWifiSsid / kWifiPassword.
//
//  Keep this template free of real credentials.
// =============================================================================

namespace config {

// -----------------------------------------------------------------------------
//  UART wiring between the ESP32 and the SIM7000G modem.
//  These are the factory pin assignments for the T-SIM7000G board. UART0 is
//  intentionally avoided because it is used by the USB serial console.
// -----------------------------------------------------------------------------
constexpr uart_port_t kModemUartPort = UART_NUM_1;  // Hardware UART peripheral
constexpr int         kModemTxPin    = 27;          // ESP32 TX  -> modem RX
constexpr int         kModemRxPin    = 26;          // ESP32 RX  <- modem TX
constexpr int         kModemBaudRate = 9600;        // SIM7000G default baud

// Baud rates tried, in order, when the modem does not answer at kModemBaudRate.
constexpr int    kModemBaudCandidates[]  = {115200, 9600, 57600, 38400, 19200};
constexpr size_t kModemBaudCandidateCount =
    sizeof(kModemBaudCandidates) / sizeof(kModemBaudCandidates[0]);

// -----------------------------------------------------------------------------
//  Power control pin.
//  PWRKEY toggles the whole modem on/off. On the T-SIM7000G it idles HIGH and
//  is pulsed LOW to switch the modem state.
// -----------------------------------------------------------------------------
constexpr int kModemPwrKeyPin = 4;

// PWRKEY polarity as seen *from the ESP32*, which is not the same across board
constexpr bool kModemPwrKeyActiveLow = true;

// -----------------------------------------------------------------------------
//  Which satellite constellations to use.
//  The SIM7000G can fuse several systems at once for a faster, more accurate
//  fix. GPS is always enabled by the modem and cannot be turned off.
// -----------------------------------------------------------------------------
constexpr bool kEnableGps     = true;   // USA  (always on, kept for clarity)
constexpr bool kEnableGlonass = true;   // Russia
constexpr bool kEnableBeidou  = true;   // China
constexpr bool kEnableGalileo = true;   // Europe

// -----------------------------------------------------------------------------
//  Debugging.
//  Set this to `true` to print, on every read, the full decoded fix plus the
//  number of satellites seen per constellation. Set to `false` for silent,
//  production operation (the print code is then removed by the optimiser).
// -----------------------------------------------------------------------------
constexpr bool kGnssDebug = true;

// -----------------------------------------------------------------------------
//  Timing knobs.
// -----------------------------------------------------------------------------
constexpr uint32_t kSatelliteScanMs = 3000;  // How long to listen to NMEA when
                                             // counting satellites

// How long to keep asking the engine for a position before giving up on this
// cycle, and how long to pause between those AT+CGNSINF polls. The timeout
// matters most when kDefaultSleepBetweenSends is on: after a full modem
// power-down every wake-up is a cold start, which can legitimately take a
// couple of minutes under a poor sky view. Without a bound the device would sit
// awake forever in a tunnel or a garage, draining exactly the battery the sleep
// was meant to save.
//
// The timeout is a runtime setting (`fix_timeout_s`) - this value is only the
// DEFAULT for a device that has never received a config message. The poll step
// stays compile-time: it is a property of how fast the modem answers, not
// something an operator has any reason to tune from a dashboard.
constexpr uint32_t kFixAcquireTimeoutSeconds = 180;
constexpr uint32_t kFixPollStepMs            = 2000;

// -----------------------------------------------------------------------------
//  Averaged position reports.
//
//  A single GNSS solution carries several metres of noise, and the FIRST
//  solution after a lock is the least settled of all - the receiver is still
//  converging when it first declares a fix. Publishing that one reading is what
//  makes a parked car's track wander.
//
//  With averaging on, every report is built from four positions instead of one:
//  the fix the acquisition returned is discarded, and the next
//  kFixAverageSampleCount readings are averaged into the position that gets
//  published (and stored on the card). See FixAverager.
//
//  kFixAverageStepMs is the gap between those readings and should not go below
//  1000: the modem solves at 1 Hz, so a faster poll simply returns the same
//  solution again - which the averager then skips, leaving fewer samples in the
//  mean. The burst costs kFixAverageSampleCount * kFixAverageStepMs of awake
//  time per cycle (~3 s by default), and never more: a reading that comes back
//  without a fix is skipped rather than retried.
//
//  Set kFixAverageEnabled to `false` to publish the raw acquisition fix as
//  before; the burst is then compiled out entirely.
// -----------------------------------------------------------------------------
constexpr bool     kFixAverageEnabled     = true;
constexpr uint8_t  kFixAverageSampleCount = 3;
constexpr uint32_t kFixAverageStepMs      = 1000;

// -----------------------------------------------------------------------------
//  ADXL345 accelerometer (GY-291) over I2C.
//
//  A 3-axis accelerometer wired to the ESP32's I2C bus. With the
//  breakout's CS tied to 3V3 and SDO to GND it answers on I2C address 0x53. Each
//  position report carries the raw instantaneous X/Y/Z acceleration in g.
//
//  Set kAdxlEnabled to `false` to skip the sensor entirely; the driver is then
//  compiled out and the accel fields are simply absent from the payload.
//
//  INT1/INT2 are physically wired (GPIO32/33) but unused for now - reserved for a
//  future motion/tap interrupt that could wake the device early.
// -----------------------------------------------------------------------------
constexpr bool kAdxlEnabled = true;

constexpr int      kI2cSdaPin      = 21;      // ESP32 SDA -> ADXL345 SDA
constexpr int      kI2cSclPin      = 22;      // ESP32 SCL -> ADXL345 SCL
constexpr uint32_t kI2cClockHz     = 400000;  // 400 kHz fast-mode I2C
constexpr uint8_t  kAdxlI2cAddress = 0x53;    // CS->3V3, SDO->GND

constexpr int kAdxlInt1Pin = 32;  // reserved (interrupts not used yet)
constexpr int kAdxlInt2Pin = 33;  // reserved (interrupts not used yet)

// -----------------------------------------------------------------------------
//  Peak accelerometer readings.
//
//  A report normally carries ONE instantaneous accelerometer sample - at the
//  default 60 s interval, one reading a minute. That says almost nothing about
//  what the car did: braking, cornering and potholes all happen *between* two
//  reports and are simply never seen.
//
//  Set kAccelPeakEnabled to `true` and a small background task samples the
//  sensor every kAccelSampleIntervalMs, keeping a running PER-AXIS maximum. The
//  ordinary position report then carries that maximum instead of a single live
//  reading, and the window restarts. Nothing extra is published and nothing
//  extra is queued - it is the same one report per interval as always.
//
//  Memory is O(1): one sample is kept, never a list, so the interval can be as
//  long as you like.
//
//  Two honest caveats:
//    * The three axes are tracked INDEPENDENTLY, so the reported triple can be
//      assembled from three different moments and is not a reading that ever
//      occurred. Anything deriving a magnitude from it (the dashboard does) will
//      read high.
//    * At kAccelSampleIntervalMs = 1000 you see one sample in a hundred - the
//      ADXL345 free-runs at 100 Hz - so most short transients are missed
//      entirely. 100 ms is the value actually worth using if you care about
//      catching events; it costs one extra I2C read per 100 ms and nothing else.
//
//  Note the sensor runs in its +/-2 g range, so a peak clips there. Braking
//  (~0.8 g) and cornering (~0.5 g) are fine; a sharp pothole will saturate.
//
//  Requires kAdxlEnabled. With this `false` the task is never created and the
//  report carries a live reading exactly as before.
// -----------------------------------------------------------------------------
constexpr bool     kAccelPeakEnabled      = false;
constexpr uint32_t kAccelSampleIntervalMs = 1000;

// -----------------------------------------------------------------------------
//  Battery monitor (single-cell Li-ion pack, incl. 1S parallel packs).
//
//  The pack percentage is read from the modem's AT+CBC (no extra ADC wiring) and
//  mapped to 0-100 % through a Li-ion discharge curve (see BatteryMonitor.cpp).
//  Charging is detected on GPIO35: the board's charger
//  pulls that pin to ~0 while charging, so when its ADC reads below
//  kBatteryChargeAdcThreshold the monitor reports the sentinel percent = 0, which
//  the API/FE render as "charging".
//
//  While enabled the monitor ALSO reports the modem's die temperature (AT+CPMUTEMP,
//  published as temp_c) - one extra AT round-trip on the same modem, no separate
//  flag. It is a proxy for how hot the device is running (the pack has no sensor).
//
//  Set kBatteryEnabled to `false` to skip the monitor; the battery AND temperature
//  fields are then absent from the payload.
// -----------------------------------------------------------------------------
constexpr bool kBatteryEnabled = true;

constexpr int kBatteryChargeSensePin     = 35;   // ADC1_CH7, input-only
constexpr int kBatteryChargeAdcThreshold = 200;  // raw counts; below => charging

// Outer clamps for the Li-ion state-of-charge curve: at or below kBatteryEmptyMv
// reads 1 %, at or above kBatteryFullMv reads 100 %, and the curve shapes
// everything in between. They bracket a single Li-ion cell's usable range; the
// result is clamped to 1..100 so that 0 stays an unambiguous "charging" sentinel.
constexpr uint32_t kBatteryEmptyMv = 3300;  // ~0 %
constexpr uint32_t kBatteryFullMv  = 4200;  // ~100 %

// -----------------------------------------------------------------------------
//  Battery method log (CSV on the microSD card; one column is published).
//
//  BatteryMonitor above owns the charge DETECTION and the AT+CBC fallback. This
//  block configures the measuring path: BatteryMethods measures the pack
//  every way this board allows - five voltage sources, three state-of-charge
//  models each, the modem's own percentage and the charge-input pin - and
//  BatteryCsvLogger writes one row per reporting cycle to a plaintext CSV. Every
//  row carries the uptime in milliseconds and the current GNSS UTC, so a capture
//  can be lined up against the position backlog.
//
//  It exists because the published percent is only as good as the curve behind
//  it, and that curve cannot be calibrated without real captures showing how far
//  apart the methods actually land.
//
//  ⚠ GPIO35 means two different things in this firmware, and both are correct:
//  BatteryMonitor reads it as a CHARGING flag (the charger pulls it to ~0), while
//  this path reads it as VBAT through the on-board divider. On the T-SIM7000G
//  that pin is cut off from the cell whenever USB power is connected (LilyGO
//  issue #128) - which is exactly why sources 1-4 read ~0 on USB while the
//  modem's AT+CBC keeps answering (with the charger rail, not the cell).
//
//  Set kBatteryLogEnabled to `false` and the whole path is compiled out; nothing
//  else in the firmware depends on it.
// -----------------------------------------------------------------------------
constexpr bool kBatteryLogEnabled = true;

// Plaintext, like the boot log - it holds diagnostics, not position data (the
// fix TIME is logged; the coordinates deliberately are not). A file whose header
// does not match the current column list is left alone and the logger steps to
// battery2.csv ... battery9.csv, so a format change never corrupts old captures.
constexpr char kSdBatteryLogPath[] = "/sdcard/battery.csv";

// Safety cap on the data rows (the header does not count). At ~120 bytes a row
// the default is about 2.4 MB, which is weeks of captures at a 30 s interval.
// 0 means "no cap".
constexpr uint32_t kSdMaxBatteryLogRows = 20000;

// The two sense pins. kBatteryVbatSensePin is the SAME pin as
// kBatteryChargeSensePin above - see the warning in this block's banner.
constexpr int kBatteryVbatSensePin  = 35;  // ADC1_CH7, pack voltage (divided)
constexpr int kBatterySolarSensePin = 36;  // ADC1_CH0, charge input (solar/VIN)

// On-board divider ratios: the measured voltage is multiplied back up by these.
// The solar one is an ASSUMPTION that varies across board revisions - check it
// against the raw count column before trusting the solar millivolts.
constexpr float kBatteryDividerRatio = 2.0f;
constexpr float kSolarDividerRatio   = 2.0f;

// ADC conversions per measurement, and how far apart in time they are taken.
//
// Both numbers exist because of the same problem. Under a SIM7000 transmit burst
// (~2 A) or a WiFi publish, VBAT sags for a few tens of milliseconds. A burst of
// back-to-back conversions finishes in MICROSECONDS, so a sag that coincides
// with it drags EVERY sample down together - and neither an average nor a median
// can reject what all the samples share. Spacing the conversions puts them on
// both sides of such a burst instead of inside one, which is what turns the sag
// into a minority the outlier filter below can delete.
//
// 48 x 40 ms spans about 1.9 s. That comes out of the idle wait between reports,
// not out of the reporting cadence (the interval is anchored at fix capture), so
// the only real cost is ~2 s more awake time per cycle when sleeping between
// sends - small next to a GNSS acquire. The gap is quantised to the FreeRTOS
// tick (10 ms by default), so keep it a multiple of 10.
constexpr uint32_t kBatteryAdcSamples     = 48;
constexpr uint32_t kBatteryAdcSampleGapMs = 40;

// How far a sample may sit from the burst median before it is thrown away,
// counted in median absolute deviations. Measuring against the burst's OWN
// spread rather than a fixed millivolt threshold is what keeps this free of
// per-board tuning: a quiet pack keeps a tight window, a noisy one widens it by
// itself.
//
// 3 is the conventional choice - it keeps essentially all of a clean burst and
// still cuts a transmit droop, which lands far outside it. Lower it to 2 to
// reject harder, but watch that the "too few survivors" fallback in
// BatteryMethods is not then firing every cycle.
constexpr uint32_t kBatteryOutlierMadFactor = 3;

// Quiet pause before the sweep starts. The GNSS acquire loop's per-poll hook
// flushes the MQTT backlog over WiFi, and it can finish microseconds before the
// first conversion would otherwise fire; this lets the rail come back up first.
// Cheap insurance next to the spacing above, which is what actually does the
// work. 0 skips the pause.
constexpr uint32_t kBatteryQuietSettleMs = 200;

// Above this on the charge-input pin, a charge source is considered present.
constexpr uint32_t kSolarInputThresholdMv = 1000;

// Below this the ADC path is treated as having no battery in front of it, and
// the four ADC sources are logged as absent rather than as a flat pack. This is
// the USB case above: the pin reads ~0, not a low cell.
constexpr uint32_t kBatteryNoReadingMv = 2000;

// -----------------------------------------------------------------------------
//  Which measurement becomes the PUBLISHED battery percent.
//
//  The block above measures the pack five ways and scores each three ways, which
//  is how the best method was found; this is where that answer is put to work.
//  The default publishes the capture's "p4_curve" column - the calibrated MEDIAN
//  of the ADC burst, scored with the piecewise Li-ion curve - because that is the
//  method that tracked the real pack best across the captures on the card.
//
//  The two indices are the BatterySource / BatteryModel enums in
//  power/BatteryMethodsData.h. They are plain ints here so this file keeps
//  depending on nothing from src/power/; main.cpp casts them where it builds the
//  BatteryReporter. The comments name the matching battery.csv column, so a
//  capture and a payload can be read side by side.
//
//  Set kBatteryReportFromMethods to `false` to go back to publishing
//  BatteryMonitor's own AT+CBC figure - a one-line rollback if a capture ever
//  says the ADC path has drifted.
// -----------------------------------------------------------------------------
constexpr bool kBatteryReportFromMethods = true;

constexpr int kBatteryReportSourceIndex = 3;  // kSourceCalMedian -> "p4_*"
constexpr int kBatteryReportModelIndex  = 1;  // kModelCurve      -> "*_curve"

// -----------------------------------------------------------------------------
//  Charger-disconnect report.
//
//  While the charger is connected the pack is INVISIBLE - on the T-SIM7000G the
//  sense pin is cut off from the cell whenever USB power is present (LilyGO
//  issue #128) - so the first true battery reading of a trip only exists once the
//  charger comes off. A cycle that got a position publishes that reading by
//  itself; a cycle that did NOT publishes nothing at all, and on a car parked
//  indoors the next lock may never come.
//
//  So on the disconnect edge, and only when this cycle found no position, the
//  firmware spends one more acquire chasing one. If that also comes back empty
//  nothing is published and the next cycle carries on as usual.
//
//  0 disables the extra attempt entirely.
// -----------------------------------------------------------------------------
constexpr uint32_t kUnplugFixTimeoutSeconds = 60;

// -----------------------------------------------------------------------------
//  WiFi (station mode).
//
//  ⚠ SECRETS: fill kWifiSsid / kWifiPassword in your local Config.h only. Leave
//  them blank here in the committed template.
//
//  Set kWifiEnabled to `false` to skip WiFi entirely; the connect code is then
//  removed by the optimiser and the SSID/password below are ignored.
// -----------------------------------------------------------------------------
constexpr bool kWifiEnabled = true;

constexpr char kWifiSsid[]     = "";  // <-- your network name (set in Config.h)
constexpr char kWifiPassword[] = "";  // <-- your network password (set in Config.h)

// How long to wait for an IP address before giving up a connection attempt, and
// how many times to retry the association before reporting failure.
constexpr uint32_t kWifiConnectTimeoutMs = 15000;
constexpr int      kWifiMaxRetries       = 5;

// After the burst of kWifiMaxRetries fast retries fails, keep trying to
// reconnect in the background at this interval until an IP is obtained.
constexpr uint32_t kWifiReconnectIntervalMs = 30000;

// -----------------------------------------------------------------------------
//  MQTT broker.
//
//
//  The broker URI scheme decides the transport AND whether the hop is TLS:
//      wss://host:port/path   encrypted MQTT-over-WebSocket  (recommended)
//      ws://host:port/path    plain MQTT-over-WebSocket
//      mqtts://host:port      encrypted MQTT-over-TCP
//      mqtt://host:port       plain MQTT-over-TCP
//
//  ⚠ SECRET: put kMqttPassword in your local Config.h only; leave it blank in
//  this committed template.
// -----------------------------------------------------------------------------
constexpr bool kMqttEnabled = true;

// Full broker URI. Build it from your host, port and (for WebSocket) path.
constexpr char kMqttBrokerUri[] = "url for mqtt broker";  // <-- set in Config.h, leave blank here

// Broker login. Username is not secret; the password is (keep it in Config.h).
constexpr char kMqttUsername[] = "admin";
constexpr char kMqttPassword[] = "";  // <-- set in Config.h, leave blank here

// MQTT client id shown in the broker's logs.
constexpr char kMqttClientId[] = "GNSSXX";

// How long publishConfirmed() waits for the broker's QoS-2 delivery ack before
// treating a publish as failed. A fix (or a whole burst) is only removed from
// the SD queue once this ack arrives, so it must be comfortably longer than a
// normal round-trip to the broker over the (possibly slow) cellular/WiFi link.
constexpr uint32_t kMqttPublishAckTimeoutMs = 8000;

// -----------------------------------------------------------------------------
//  Telemetry topic + device identity.
//  The single place the publish path is defined (matches the desktop tools).
// -----------------------------------------------------------------------------
constexpr char kDeviceId[]       = "GNSSXX";
constexpr char kTelemetryTopic[] = "devices/GNSSXX";

// -----------------------------------------------------------------------------
//  Remote settings (broker -> device).
//
//  The device subscribes to this topic and expects a small *plaintext* JSON
//  document (it carries no position data, so there is nothing to protect
//  end-to-end; the wss:// hop still encrypts it in transit):
//
//      { "version": 7, "interval_s": 60, "sleep_between": true,
//        "fix_timeout_s": 180, "queue_max_fixes": 20000,
//        "retry_interval_h": 24, "retry_max_age_h": 168 }
//
//      version          server-assigned revision number, echoed back in every
//                       report as settings_version so the dashboard can tell
//                       whether the device is actually running this document
//      interval_s       seconds between position reports
//      sleep_between    power the modem down and deep-sleep the ESP32 in between
//      fix_timeout_s    how long to keep asking for a position before giving up
//                       on the cycle (see kFixAcquireTimeoutSeconds)
//      queue_max_fixes  how many undelivered fixes the SD queue may hold before
//                       the oldest are dropped (see kSdMaxQueuedFixes)
//      retry_interval_h hours between attempts on an API-rejected fix
//      retry_max_age_h  give up on a rejected fix older than this (0 = never)
//      config_check_s   how often an AWAKE device asks the broker to re-send
//                       this document (a backstop - see kDefaultConfigCheckSeconds)
//
//  Every key is optional: the decoder merges what it finds into the settings
//  already in force, so a document carrying only "interval_s" changes only that.
//  That is what keeps this device compatible with an older, two-key publisher.
//
//  ⚠ PUBLISH THIS MESSAGE WITH THE RETAIN FLAG SET. When sleep_between is on the
//  device is only online for a few seconds per cycle, so it will essentially
//  never catch a live publish - it relies on the broker replaying the retained
//  message the instant it subscribes. See the README.
//
//  Whatever arrives is validated, clamped to the bounds below, and cached on the
//  SD card (kSdSettingsFilePath) so the last known-good configuration survives a
//  reboot with no network. The defaults below are only used by a device that has
//  never successfully received a config message.
// -----------------------------------------------------------------------------
constexpr char kConfigTopic[] = "devices/GNSSXX/config";

// How long to wait, after starting the MQTT client, for the broker to replay the
// retained config before getting on with the first position report. This window
// has to cover the TCP connect and the TLS handshake as well as the delivery
// itself, so it is a good deal longer than the message alone would need. A
// timeout is not fatal - we simply carry on with the cached settings.
constexpr uint32_t kConfigFetchTimeoutMs = 8000;

// -----------------------------------------------------------------------------
//  Delivery acknowledgements.
// -----------------------------------------------------------------------------
//  A QoS-2 ack from the broker only proves Mosquitto took the message. It says
//  nothing about whether the API decrypted it, accepted it, and wrote it to the
//  database - so on its own it let a rejected (or never-received) fix be deleted
//  from the SD queue and silently lost.
//
//  With acks on, the API publishes an encrypted verdict per envelope to the topic
//  below, and a fix leaves the card only once it is confirmed stored. Rejected
//  fixes move to the retry file (see the SD section) instead of being dropped.
//
//  Set kAckEnabled to false to restore the old behaviour exactly - useful if the
//  broker ACL is not ready yet, since without a read grant on the ack topic the
//  broker silently delivers nothing and every fix would wait out the timeout.
// -----------------------------------------------------------------------------
constexpr bool kAckEnabled = false;

constexpr char kAckTopic[] = "devices/GNSSXX/ack";

// How long to wait for the API's verdict after the broker has acked the publish.
// This covers the API's decrypt, validate and database write, so it is generous:
// a timeout is not fatal (the fix simply stays queued and is re-sent next cycle,
// which the API dedupes) but it does block the main loop for this long.
constexpr uint32_t kAckTimeoutMs = 10000;

// This device's OWN RSA-3072 PRIVATE key, used to open the acks.
//
// NOTE the direction is the opposite of kReceiverPublicKeyPem: for telemetry we
// hold the receiver's PUBLIC key, but an ack is sealed TO us, so here we hold the
// private half and the server holds only the public one.
//
// Generate the pair yourself - it must never reach the server:
//   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out GNSSXX_ack_private.pem
//   openssl rsa -in GNSSXX_ack_private.pem -pubout -out GNSSXX_ack_public.pem
//   dotnet run -- import-device-key --device GNSSXX --ack-public-pem GNSSXX_ack_public.pem
//
// Then paste the PRIVATE half here in your own Config.h. SECRET: never commit it
// and never fill it in in this example file.
constexpr char kDeviceAckPrivateKeyPem[] = "";

// Defaults for a device with no cached settings and no broker message yet.
// The remaining four defaults are the constants they replace at runtime, and
// live with the subsystem they belong to: kFixAcquireTimeoutSeconds (timing),
// kSdMaxQueuedFixes, kRetryIntervalHours and kRetryMaxAgeHours (microSD).
constexpr uint32_t kDefaultSendIntervalSeconds = 60;
constexpr bool     kDefaultSleepBetweenSends   = false;

// Accepted range for every numeric setting. A broker message outside these
// bounds is clamped rather than rejected, so a typo can never wedge the device
// into a busy-loop (interval 0) or an effectively dead one. The API validates
// against exactly these numbers and answers 400 instead - see the bounds table
// in its README. If you change one here, change it there too.
constexpr uint32_t kMinSendIntervalSeconds = 5;
constexpr uint32_t kMaxSendIntervalSeconds = 86400;  // 24 h

// A fix timeout below the poll step could never see a single reply; the upper
// bound stops a bad config parking a sleeping device awake for hours.
constexpr uint32_t kMinFixTimeoutSeconds = 15;
constexpr uint32_t kMaxFixTimeoutSeconds = 3600;  // 1 h

// The floor keeps a short outage survivable; the ceiling bounds how much of the
// card a runaway backlog may claim - at ~1 KB per sealed envelope this is about
// 100 MB, which is months of queue at any sane reporting interval. It is a
// policy bound, not a technical one: FixQueue carries a head offset, so popping
// costs O(batch) however deep the file gets.
//
// Note the API enforces the same ceiling in a database check constraint, so
// changing it here alone is not enough - see DeviceConfigRules.MaxQueueMaxFixes.
constexpr uint32_t kMinQueueMaxFixes = 100;
constexpr uint32_t kMaxQueueMaxFixes = 100000;

// Retry pacing bounds. The floor of one hour is deliberate: the reject reasons
// that do clear are fixed by a human on the server, so retrying faster would
// only burn power and airtime to be told the same thing.
constexpr uint32_t kMinRetryIntervalHours = 1;
constexpr uint32_t kMaxRetryIntervalHours = 720;  // 30 days

// No floor: 0 is the meaningful "never give up" value. The ceiling is a year.
constexpr uint32_t kMaxRetryMaxAgeHours = 8760;

// How often an AWAKE device asks the broker to re-send the retained config.
//
// This is only a backstop. A config normally arrives by push, within a second,
// because the subscription is already open - and a device that reconnects (or
// wakes from deep sleep) is handed the retained document automatically. What
// this recovers from is a connection that looks alive but delivers nothing,
// which is invisible from this side. A deep-sleeping device ignores it entirely.
//
// The floor of one minute is deliberate: each check is one SUBSCRIBE and one
// wake-up, and a device that did it every few seconds would burn battery
// chasing a message that push would have brought it anyway.
//
// An hour is the default for the same reason. Because the awake interval wait is
// cut into chunks of at most this value, the number sets how often the device
// wakes and puts a SUBSCRIBE on the wire while it is otherwise idle - so a
// shorter check costs battery and airtime on every device, every hour, for ever.
// What it buys is only faster recovery from a half-open socket, because a real
// change already arrives by push within a second and a reconnect replays the
// retained document anyway. Catching a dead-but-open connection within the hour
// is ample for that; a quarter of an hour was four times the cost for no
// practical gain.
constexpr uint32_t kDefaultConfigCheckSeconds = 3600;  // 1 hour
constexpr uint32_t kMinConfigCheckSeconds     = 60;
constexpr uint32_t kMaxConfigCheckSeconds     = 86400;  // 24 h

// -----------------------------------------------------------------------------
//  End-to-end encryption.
//  The GNSS payload is encrypted (RSA-OAEP-SHA256 + AES-256-GCM) for the holder
//  of the receiver PRIVATE key, so the broker only ever sees ciphertext. This
//  device only needs the receiver PUBLIC key.
//
//  Generate the key pair with desktop/create_certificates.py, then paste the
//  contents of desktop/certs/receiver_public.pem below (in your Config.h). The
//  public key is not a secret, but keep this template's copy as a placeholder.
// -----------------------------------------------------------------------------
constexpr char kReceiverPublicKeyPem[] =
    "-----BEGIN PUBLIC KEY-----\n"
    "PASTE receiver_public.pem HERE (one C string line per PEM line, each\n"
    "ending with \\n). See desktop/certs/receiver_public.pem.\n"
    "-----END PUBLIC KEY-----\n";

// -----------------------------------------------------------------------------
//  microSD store-and-forward queue.
//
//  When the broker cannot be reached, each fix is sealed into the very same
//  encrypted envelope that would have been transmitted and appended to a queue
//  file on the microSD card (one envelope per line). On reconnect the whole
//  queue is drained in a single MQTT burst (a JSON array of envelopes) and the
//  file is cleared only after the broker's QoS-2 ack. Because only ciphertext
//  ever touches the card, a stolen card leaks nothing.
//
//  Set kSdEnabled to `false` to skip the card entirely; the SD/queue code is
//  then compiled out and undelivered fixes are simply dropped.
// -----------------------------------------------------------------------------
constexpr bool kSdEnabled = true;

// SPI pins for the microSD slot. These are the factory wiring of the
// T-SIM7000G board (shared HSPI bus dedicated to the card).
constexpr spi_host_device_t kSdSpiHost = SPI2_HOST;  // a.k.a. HSPI on the ESP32
constexpr int kSdPinMiso = 2;   // card DO  -> ESP32
constexpr int kSdPinMosi = 15;  // card DI  <- ESP32
constexpr int kSdPinSclk = 14;  // SPI clock
constexpr int kSdPinCs   = 13;  // chip select

// Where the FAT filesystem is mounted and the queue file lives inside it. The
// queue is line-delimited JSON (".jsonl"): one encrypted envelope per line.
constexpr char kSdMountPoint[]    = "/sdcard";
constexpr char kSdQueueFilePath[] = "/sdcard/queue.jsonl";

// Cached copy of the runtime settings last received from the broker. Unlike the
// queue this file is written in the CLEAR: it holds no position data, only the
// reporting interval and the sleep flag, so there is nothing worth encrypting.
constexpr char kSdSettingsFilePath[] = "/sdcard/settings.json";

// Safety cap on how many undelivered fixes the queue may hold. During a long
// outage the oldest entries are dropped once this many have accumulated, so the
// card can never fill up. Each envelope is ~1 KB, so the default is ~20 MB -
// tiny next to any real card, and about two weeks of backlog at the default
// reporting interval.
//
// This is a runtime setting (`queue_max_fixes`); the value here is only the
// DEFAULT. It is expressed as a count rather than a duration because a queued
// line is bare ciphertext with no timestamp to age it by - one fix goes in per
// reporting cycle, so the dashboard turns the count into "about N days at the
// current interval" for the human.
constexpr uint32_t kSdMaxQueuedFixes = 20000;

// How many queued envelopes go into a single burst message. A typical outage
// fits in one burst; a very long backlog is drained in several back-to-back
// bursts so the JSON array and the MQTT buffer never exceed the modest internal
// RAM (this board has no PSRAM, and the TLS stack is already memory-hungry).
//
// Size this by the arithmetic, not by taste. One envelope is ~1 KB - over half
// of it is the base64 RSA-3072-wrapped AES key, which every envelope carries
// separately - so a burst of N costs roughly N KB. That N KB is then held THREE
// times over at the moment of publish:
//
//   1. the batch vector read off the card (kept alive so rejected fixes can be
//      parked for retry after the API's verdict),
//   2. the joined JSON array, which must be ONE contiguous allocation, and
//   3. esp-mqtt's outbox copy - also contiguous - because we publish at QoS 2
//      and the client must be able to re-send until PUBCOMP.
//
// On top of that sit the mbedTLS session buffers (~20 KB) and the WebSocket
// buffer, all in the same internal DRAM. At 40 that came to ~117 KB of burst
// plus TLS, and the outbox's contiguous ~39 KB request began failing outright
// ("outbox_enqueue: Memory exhausted") once the heap was fragmented. 10 keeps
// the peak near 30 KB with comfortable margin.
//
// Lowering this costs no throughput worth measuring: the drain loop already
// fires bursts back-to-back within kBacklogFlushBudgetMs, and each burst is
// dominated by the broker and API ack waits, not by its size.
constexpr uint32_t kSdMaxBurstFixes = 10;

// How long to wait before re-attempting a backlog flush that achieved NOTHING.
// The backlog is flushed whenever the link is up - including on cycles with no
// position fix - and the attempt is offered on every GNSS poll (~kFixPollStepMs)
// while waiting for one. Without this pause a broker that accepts publishes but
// never acks would burn the whole acquire window in back-to-back
// kMqttPublishAckTimeoutMs timeouts. Note it applies only to an attempt that
// moved no data at all: a flush that shipped anything (or merely ran out of the
// budget below) is repeated on the very next poll, which is what lets a long
// backlog drain in back-to-back bursts. An MQTT reconnect clears the wait
// immediately, so a genuine link-up always drains at once.
constexpr uint32_t kBacklogFlushRetryMs = 600000;  // 10 minutes

// How long a single backlog flush may keep draining before it hands the CPU
// back. A flush keeps shipping bursts until the queue is empty, and the deepest
// backlog kSdMaxQueuedFixes allows is 500 bursts - minutes of work - which would
// otherwise all happen inside one GNSS poll callback, stalling the poll loop and
// the remote-settings poll beside it. With this budget the drain is spread over
// consecutive polls instead, losing nothing: whatever is already confirmed has
// left the card, and the next poll resumes where this one stopped.
constexpr uint32_t kBacklogFlushBudgetMs = 30000;  // 30 seconds

// Fixes the API explicitly REJECTED (bad timestamp, out-of-range value, unknown
// device, ...). They are kept apart from the live queue so a permanently
// unacceptable fix cannot sit at its head blocking fresh ones, and are re-offered
// on the slow schedule below.
//
// Retrying is worth it because several reject reasons are server-side and clear
// on their own: an unknown or deactivated device starts working the moment its
// row is provisioned, and a decrypt failure clears when the key is fixed.
constexpr char kSdRetryFilePath[] = "/sdcard/retry.jsonl";

// How long to wait between attempts on a rejected fix. Once a day: the reasons
// that do clear are fixed by a human on the server, so retrying sooner would just
// burn power and airtime to be told the same thing. Runtime setting
// (`retry_interval_h`); this is only the DEFAULT.
constexpr uint32_t kRetryIntervalHours = 24;

// Give up on a rejected fix that is still being refused after this long. This is
// the only path that deliberately discards data, so it is logged at error level.
// Zero disables the cap (retry forever - the file cap below still applies).
// Runtime setting (`retry_max_age_h`); this is only the DEFAULT.
constexpr uint32_t kRetryMaxAgeHours = 168;  // 7 days

// Safety cap on the retry file. Much smaller than the live queue: a healthy
// system rejects nothing, so a large number here would only mask a real problem.
constexpr uint32_t kSdMaxRetryEntries = 2000;

// -----------------------------------------------------------------------------
//  Boot log.
//
//  One plaintext line per boot on the card, plus the recent history printed to
//  the serial console at start-up. It records what nothing else does: the
//  esp_reset_reason() (POWERON / BROWNOUT / PANIC / TASK_WDT / DEEPSLEEP), a
//  boot counter, and whether the RTC domain kept its contents - which is what
//  separates "it crashed and rebooted" from "it lost power", because RTC memory
//  survives a reset but not a dropped rail.
//
//  Written in the CLEAR like the settings cache: it holds no position data, only
//  restart forensics. Set kBootLogEnabled to `false` and the whole thing is
//  compiled out.
// -----------------------------------------------------------------------------
constexpr bool kBootLogEnabled = true;

constexpr char kSdBootLogPath[] = "/sdcard/boot.log";

// Safety cap on the log. At ~70 bytes a line the default is about 14 KB, and
// 200 boots is far more history than any diagnosis needs - a device that reboots
// enough to wrap this has already told you what you needed to know.
constexpr uint32_t kSdMaxBootLogLines = 200;

// How many PREVIOUS boots to print at start-up, above the current one. Ten fits
// a terminal window and is enough to see a reboot loop for what it is.
constexpr uint32_t kBootLogPrintLines = 10;

// -----------------------------------------------------------------------------
//  Deep sleep (only used when the "sleep_between" setting is on).
//
//  Between reports the modem is powered right down - which also cuts the GNSS
//  engine, the active antenna's amplifier and the LTE PA - the SD card is
//  unmounted, and the ESP32 enters deep sleep. Deep sleep does not resume: the
//  chip REBOOTS on wake and app_main() runs from the top, re-mounting the card
//  and reconnecting WiFi/MQTT. Budget for that: every cycle pays a fresh TLS
//  handshake and a cold GNSS acquisition, so an interval of a few minutes or
//  more is where this actually starts saving energy.
//
//  The RTC timer is always armed, for (interval_s - time spent since the fix was
//  captured), so reports stay on a steady cadence rather than drifting by however
//  long the publish took.
// -----------------------------------------------------------------------------

// Optional second wake source: an external signal on an RTC-capable GPIO (ext0),
// e.g. an ignition-sense or motion line that should report a position early.
//
//   kWakeGpioPin = -1  ->  disabled; the timer is the only wake source.
//   kWakeGpioPin >= 0  ->  ext0 wake is armed on that pin, and the matching
//                          internal pull (down for level 1, up for level 0) is
//                          held through sleep.
//
// Pins 2, 4, 13, 14, 15, 26 and 27 are already taken by the modem and the SD
// card. Free RTC-capable choices on the T-SIM7000G are 32 and 33. Note that
// 34-39 are input-only and have no internal pull resistors, so those need an
// external one.
constexpr int kWakeGpioPin   = -1;  // -1 = timer-only (no external wake)
constexpr int kWakeGpioLevel = 1;   // 1 = wake when the pin reads HIGH

// Floor on the deep-sleep duration. If publishing a fix overran the interval
// there is no time left to sleep, but bouncing straight back through a reboot
// would be worse than pausing briefly first.
constexpr uint32_t kMinDeepSleepMs = 1000;

}  // namespace config
