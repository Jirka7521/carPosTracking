#pragma once

// =============================================================================
//  BatteryCsvLogger  -  One CSV row per measurement, on the microSD card.
// -----------------------------------------------------------------------------
//  Responsibility (single!): turn a BatteryMethodsSample (plus the timestamps
//  that make it interpretable) into one line of CSV on the card, and keep that
//  file from growing without bound. It measures nothing itself and prints
//  nothing per row - BatteryMethods measures, this stores.
//
//  Two timestamps ride on every row and they answer different questions:
//    * uptime_ms  - milliseconds since boot (esp_timer). Always present, always
//                   monotonic, and the only usable x-axis before a fix exists.
//    * gps_utc    - the UTC of the current GNSS fix. Absent until the receiver
//                   has one, which is exactly why uptime_ms cannot be dropped;
//                   once present it is what lets a capture be lined up against
//                   the position backlog and against anything that happened in
//                   the real world.
//
//  Written in the CLEAR, like the boot log and the settings cache: it holds
//  diagnostics, not position data. (The fix TIME is on the row; the fix's
//  latitude and longitude deliberately are not.)
//
//  Optional, like every SD-backed subsystem here: no card means no rows, a
//  warning, and a device that carries on tracking.
//
//  Header handling mirrors the Arduino rig this was ported from: a file whose
//  first line does not match the current header is left alone and the logger
//  steps to the next numbered file, because appending today's 43 columns to
//  yesterday's 31 produces something no spreadsheet can read.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>

#include "gnss/GnssData.h"
#include "power/BatteryData.h"
#include "power/BatteryMethodsData.h"
#include "sdcard/SdCard.h"

class BatteryCsvLogger {
 public:
  // Borrows `card` and `basePath` (both must outlive this object). `maxRows`
  // caps the data rows in the file, the header excluded (0 means "no cap").
  BatteryCsvLogger(SdCard& card, const char* basePath, std::size_t maxRows);

  // Pick the file to write and make sure it starts with the current header.
  // Returns true when rows can be appended.
  bool begin();

  // The file actually in use, which is `basePath` unless a header mismatch
  // pushed us onto a numbered sibling. Empty until begin() succeeds.
  const std::string& path() const { return path_; }

  // Append one row. `fix` supplies the GNSS timestamp (and whether it is real),
  // `methods` the measurements, `fw` the percent the SHIPPED BatteryMonitor
  // produced for the same moment - carried so a capture shows what the device
  // actually believed, next to every alternative it could have believed.
  //
  // Returns false on an IO error; the caller is expected to shrug and continue.
  bool append(uint32_t uptimeMs, const GnssFix& fix,
              const BatteryMethodsSample& methods, const BatteryStatus& fw);

 private:
  // Compose `basePath` with `n` spliced in before the extension: 1 gives the
  // base path itself, 2 gives ".../battery2.csv", and so on.
  std::string numberedPath(int n) const;

  // Drop the oldest data rows once the file exceeds the cap, KEEPING the header.
  //
  // Deliberately not called on every append: it streams the whole file twice
  // (count, then rewrite), which at the default cap is megabytes. Once every
  // kTrimCheckInterval rows the amortised cost is negligible, and the file can
  // only ever overshoot the cap by that many rows.
  bool trimToCap();

  // How many appends pass between cap checks. See trimToCap().
  static constexpr std::size_t kTrimCheckInterval = 256;

  SdCard&     card_;
  const char* basePath_;
  std::size_t maxRows_;

  std::string path_;               // resolved log file (empty until begin())
  bool        ready_        = false;
  std::size_t sinceTrimCheck_ = 0;  // appends since the last cap check
};
