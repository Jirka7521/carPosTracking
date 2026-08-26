#include "power/BatteryCsvLogger.h"

#include <cstdarg>
#include <cstdio>
#include <vector>

#include "esp_log.h"

static const char* TAG = "BatteryCsvLogger";

// The column list, and with it the file format. Any change here MUST be matched
// in buildRow() below - the two are written to be read side by side, and a file
// whose first line does not match this string is never appended to (begin()
// rotates to a numbered sibling instead), so a mismatch shows up as a new file
// rather than as silently misaligned data.
static const char* kHeader =
    "uptime_ms,gps_utc,gps_time_valid,has_fix,sats_used,"
    "raw_mean,raw_median,"
    "v1_naive_mv,v2_calper_mv,v3_calmean_mv,v4_calmed_mv,v5_modem_mv,"
    "adc_valid,v5_valid,"
    "p1_lin,p1_curve,p1_sig,p2_lin,p2_curve,p2_sig,p3_lin,p3_curve,p3_sig,"
    "p4_lin,p4_curve,p4_sig,p5_lin,p5_curve,p5_sig,"
    "modem_pct,modem_bcs,solar_raw,solar_mv,input_present,"
    "trend_charging,trend_usable,"
    "fw_pct,fw_charging,fw_valid,v_spread_mv,p_spread";

// One row is 41 short fields; 384 bytes leaves room to spare without putting
// anything large on the stack.
static constexpr std::size_t kRowBytes = 384;

// How many numbered files begin() will try before giving up. Nine is the rig's
// limit too: needing a tenth means the format is churning, not that the device
// has been running a long time.
static constexpr int kMaxFileSlots = 9;

// Appends one printf chunk at `pos`, returning the new position (or -1 once the
// buffer is full, which then poisons every later call). Keeps buildRow() free of
// a length check per field.
static int appendFmt(char* buf, std::size_t cap, int pos, const char* fmt, ...) {
  if (pos < 0 || static_cast<std::size_t>(pos) >= cap) {
    return -1;
  }
  va_list ap;
  va_start(ap, fmt);
  const int written = vsnprintf(buf + pos, cap - pos, fmt, ap);
  va_end(ap);
  if (written < 0 || static_cast<std::size_t>(pos + written) >= cap) {
    return -1;
  }
  return pos + written;
}

BatteryCsvLogger::BatteryCsvLogger(SdCard& card, const char* basePath,
                                   std::size_t maxRows)
    : card_(card), basePath_(basePath), maxRows_(maxRows) {}

std::string BatteryCsvLogger::numberedPath(int n) const {
  std::string base(basePath_);
  if (n <= 1) {
    return base;
  }

  const std::size_t dot = base.find_last_of('.');
  const std::string digit(1, static_cast<char>('0' + n));
  if (dot == std::string::npos) {
    return base + digit;
  }
  return base.substr(0, dot) + digit + base.substr(dot);
}

bool BatteryCsvLogger::begin() {
  if (!card_.isMounted()) {
    ESP_LOGW(TAG, "no SD card - battery CSV logging disabled");
    return false;
  }

  for (int n = 1; n <= kMaxFileSlots; ++n) {
    const std::string candidate = numberedPath(n);

    // Missing (or truncated to nothing): ours to start, header first.
    if (card_.fileSize(candidate.c_str()) == 0) {
      if (!card_.appendLine(candidate.c_str(), kHeader)) {
        ESP_LOGW(TAG, "could not write the header to %s", candidate.c_str());
        return false;
      }
      path_  = candidate;
      ready_ = true;
      ESP_LOGI(TAG, "battery log -> %s (new)", path_.c_str());
      return true;
    }

    // Existing: only append to it when its header is exactly ours.
    std::vector<std::string> firstLine;
    if (card_.readLines(candidate.c_str(), 1, firstLine) &&
        !firstLine.empty() && firstLine[0] == kHeader) {
      path_  = candidate;
      ready_ = true;
      ESP_LOGI(TAG, "battery log -> %s (appending)", path_.c_str());
      return true;
    }

    ESP_LOGW(TAG, "%s has a different header - trying the next file",
             candidate.c_str());
  }

  ESP_LOGW(TAG, "no free battery log slot (1-%d) - logging disabled",
           kMaxFileSlots);
  return false;
}

bool BatteryCsvLogger::append(uint32_t uptimeMs, const GnssFix& fix,
                              const BatteryMethodsSample& methods,
                              const BatteryStatus& fw) {
  if (!ready_) {
    return false;
  }

  char buf[kRowBytes];
  int  pos = 0;

  // --- timestamps -----------------------------------------------------------
  // The GNSS clock is only written when the receiver actually decoded one; an
  // empty field plus its own valid flag says "not known yet" without inviting a
  // spreadsheet to read 1970 as a real moment.
  char utc[24] = {0};
  if (fix.time.valid) {
    // The modulos are there for the compiler, not for the data: they bound each
    // field's width so -Wformat-truncation can prove the buffer is big enough.
    // A field wide enough to need them would already be a corrupt timestamp.
    snprintf(utc, sizeof(utc), "%04u-%02u-%02uT%02u:%02u:%02uZ",
             (unsigned)(fix.time.year % 10000), (unsigned)(fix.time.month % 100),
             (unsigned)(fix.time.day % 100), (unsigned)(fix.time.hour % 100),
             (unsigned)(fix.time.minute % 100),
             (unsigned)(fix.time.second % 100));
  }
  pos = appendFmt(buf, sizeof(buf), pos, "%lu,%s,%d,%d,%u",
                  (unsigned long)uptimeMs, utc, fix.time.valid ? 1 : 0,
                  fix.hasFix() ? 1 : 0, (unsigned)fix.satellitesUsed);

  // --- the raw ADC burst behind sources 1-4 ---------------------------------
  pos = appendFmt(buf, sizeof(buf), pos, ",%u,%u", (unsigned)methods.rawMean,
                  (unsigned)methods.rawMedian);

  // --- one voltage per source, then the two validity flags ------------------
  for (std::size_t s = 0; s < kBatterySourceCount; ++s) {
    pos = appendFmt(buf, sizeof(buf), pos, ",%u", (unsigned)methods.mv[s]);
  }
  // "adc_valid" covers sources 1-4 as a group: they live or die together on
  // whether the sense pin can see the pack at all (on USB power it cannot).
  const bool adcValid =
      methods.mvValid[kSourceNaive] || methods.mvValid[kSourceCalMean];
  pos = appendFmt(buf, sizeof(buf), pos, ",%d,%d", adcValid ? 1 : 0,
                  methods.mvValid[kSourceModem] ? 1 : 0);

  // --- every model applied to every source ----------------------------------
  for (std::size_t s = 0; s < kBatterySourceCount; ++s) {
    for (std::size_t m = 0; m < kBatteryModelCount; ++m) {
      pos = appendFmt(buf, sizeof(buf), pos, ",%d", (int)methods.percent[s][m]);
    }
  }

  // --- what the modem thinks, and what the charge input says ----------------
  pos = appendFmt(buf, sizeof(buf), pos, ",%d,%d,%u,%u,%d",
                  (int)methods.modemPercent, (int)methods.modemStatus,
                  (unsigned)methods.solarRaw, (unsigned)methods.solarMv,
                  methods.inputPresent ? 1 : 0);

  // --- the trend detector ---------------------------------------------------
  pos = appendFmt(buf, sizeof(buf), pos, ",%d,%d",
                  methods.trendCharging ? 1 : 0, methods.trendUsable ? 1 : 0);

  // --- and finally what the SHIPPED monitor concluded, for comparison -------
  // fw_pct is written as -1 when that read failed, so it is never confused with
  // the monitor's own "0 = charging" sentinel, which is a real answer.
  pos = appendFmt(buf, sizeof(buf), pos, ",%d,%d,%d,%u,%d",
                  fw.valid ? (int)fw.percent : -1, fw.charging ? 1 : 0,
                  fw.valid ? 1 : 0, (unsigned)methods.spreadMv,
                  (int)methods.spreadPct);

  if (pos < 0) {
    ESP_LOGW(TAG, "row did not fit in %u bytes - not written",
             (unsigned)sizeof(buf));
    return false;
  }

  if (!card_.appendLine(path_.c_str(), buf)) {
    ESP_LOGW(TAG, "could not append to %s", path_.c_str());
    return false;
  }

  if (++sinceTrimCheck_ >= kTrimCheckInterval) {
    sinceTrimCheck_ = 0;
    trimToCap();
  }
  return true;
}

bool BatteryCsvLogger::trimToCap() {
  if (maxRows_ == 0 || !ready_) {
    return true;  // uncapped: the card is big and the rows are small
  }

  const std::size_t lines = card_.countLines(path_.c_str());
  if (lines <= maxRows_ + 1) {  // +1: the header is not a data row
    return true;
  }

  const std::size_t drop      = lines - 1 - maxRows_;
  std::size_t       index     = 0;
  std::size_t       survivors = 0;

  // Keep line 0 (the header) whatever happens, then drop the oldest `drop` data
  // rows. dropFirstLines() cannot be used here for exactly that reason: it would
  // take the header with them.
  const bool ok = card_.rewriteLines(
      path_.c_str(),
      [&index, drop](const std::string&) {
        const bool keep = (index == 0) || (index > drop);
        ++index;
        return keep;
      },
      survivors);

  if (!ok) {
    ESP_LOGW(TAG, "could not trim %s to %u rows", path_.c_str(),
             (unsigned)maxRows_);
  }
  return ok;
}
