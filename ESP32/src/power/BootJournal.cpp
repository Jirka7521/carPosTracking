#include "power/BootJournal.h"

#include <cstdio>
#include <string>
#include <vector>

#include "esp_attr.h"
#include "esp_log.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "power/DeepSleepController.h"

static const char* TAG = "BootJournal";

// -----------------------------------------------------------------------------
//  RTC-backed state.
//
//  These four live in RTC slow memory, which keeps its contents through deep
//  sleep and through CPU resets (panic, watchdog, software reset) but NOT
//  through an actual loss of the rail. That asymmetry is the whole point: the
//  magic word below is intact after a crash and gone after a power cut, so one
//  comparison separates the two failure modes the serial log could never tell
//  apart. Any value would do as the magic; this one is just recognisable in a
//  hex dump of the RTC region.
// -----------------------------------------------------------------------------
static constexpr uint32_t kRtcMagic = 0xB007106U;  // "BOOTLOG"

RTC_DATA_ATTR static uint32_t rtcMagic;
RTC_DATA_ATTR static uint32_t rtcBootCount;
RTC_DATA_ATTR static uint32_t rtcPrevUptimeS;
RTC_DATA_ATTR static uint32_t rtcLastBatteryMv;

// One composed line, with room for the widest reset/wake names plus the
// "(RTC CLEARED)" suffix. Stack-allocated per boot and then discarded.
static constexpr std::size_t kLineBytes = 160;

BootJournal::BootJournal(SdCard& card, const char* filePath,
                         std::size_t maxLines, std::size_t printLines)
    : card_(card),
      filePath_(filePath),
      maxLines_(maxLines),
      printLines_(printLines) {}

const char* BootJournal::resetCauseName() {
  switch (esp_reset_reason()) {
    case ESP_RST_POWERON:
      // The interesting one: the chip was powered up from cold. Paired with a
      // cleared RTC domain this is a genuine loss of supply - a flat pack, a
      // tripped protection FET, a yanked connector.
      return "POWERON";
    case ESP_RST_BROWNOUT:
      // The supply sagged past the detector's threshold. On a battery device
      // this is the classic "cell cannot deliver the peak current" signature.
      return "BROWNOUT";
    case ESP_RST_PANIC:
      return "PANIC";
    case ESP_RST_INT_WDT:
      return "INT_WDT";
    case ESP_RST_TASK_WDT:
      return "TASK_WDT";
    case ESP_RST_WDT:
      return "WDT";
    case ESP_RST_DEEPSLEEP:
      return "DEEPSLEEP";
    case ESP_RST_SW:
      return "SW";
    case ESP_RST_EXT:
      return "EXT";  // reset button / external reset pin
    case ESP_RST_SDIO:
      return "SDIO";
    default:
      return "UNKNOWN";
  }
}

void BootJournal::noteBattery(uint16_t millivolts) {
  rtcLastBatteryMv = millivolts;
  noteUptime();
}

void BootJournal::noteUptime() {
  // Seconds, because that is all the resolution the line prints and it keeps the
  // arithmetic in 32 bits (nano-printf cannot be trusted with %llu anyway).
  rtcPrevUptimeS = static_cast<uint32_t>(esp_timer_get_time() / 1000000LL);
}

void BootJournal::printRecent(std::size_t n) const {
  if (n == 0 || !card_.isMounted()) {
    return;
  }

  // Keep only the last `n` lines as the file streams past. `next` is where the
  // oldest of them currently sits once the ring has wrapped.
  std::vector<std::string> ring;
  ring.reserve(n);
  std::size_t next = 0;

  card_.forEachLine(filePath_, [&ring, &next, n](const std::string& line) {
    if (ring.size() < n) {
      ring.push_back(line);
    } else {
      ring[next] = line;
      next       = (next + 1) % n;
    }
  });

  // Oldest first. The ring only wrapped if it actually filled, in which case the
  // walk starts at `next`; otherwise the lines are already in order.
  const std::size_t count = ring.size();
  const bool        wrapped = (count == n);
  for (std::size_t i = 0; i < count; ++i) {
    printf("  %s\n", ring[wrapped ? (next + i) % n : i].c_str());
  }
}

bool BootJournal::trimToCap() const {
  if (maxLines_ == 0) {
    return true;  // uncapped
  }
  const std::size_t lines = card_.countLines(filePath_);
  if (lines <= maxLines_) {
    return true;
  }
  return card_.dropFirstLines(filePath_, lines - maxLines_);
}

bool BootJournal::begin() {
  // 1. Was the RTC domain still powered? Everything else keys off this.
  const bool rtcValid = (rtcMagic == kRtcMagic);
  if (!rtcValid) {
    rtcMagic         = kRtcMagic;
    rtcBootCount     = 0;
    rtcPrevUptimeS   = 0;
    rtcLastBatteryMv = 0;
  }
  ++rtcBootCount;

  // 2. The two fields that are only meaningful when the previous run left them
  //    behind. Rendered as "?" rather than 0 so a power loss cannot be misread
  //    as "ran for no time on a flat pack".
  char uptime[16];
  char battery[16];
  if (rtcValid) {
    std::snprintf(uptime, sizeof(uptime), "%us", (unsigned)rtcPrevUptimeS);
  } else {
    std::snprintf(uptime, sizeof(uptime), "?");
  }
  if (rtcValid && rtcLastBatteryMv != 0) {
    std::snprintf(battery, sizeof(battery), "%umV", (unsigned)rtcLastBatteryMv);
  } else {
    std::snprintf(battery, sizeof(battery), "?");
  }

  // 3. Compose. Fixed column widths so the block below lines up; every field is
  //    %s or %u with an explicit cast, because nano-printf is unreliable with
  //    %ll and with float widths (see CLAUDE.md).
  char line[kLineBytes];
  std::snprintf(line, sizeof(line),
                "#%04u reset=%-9s wake=%-16s prev_up=%-8s heap=%-6u bat=%-8s%s",
                (unsigned)rtcBootCount, resetCauseName(),
                DeepSleepController::wakeCauseName(), uptime,
                (unsigned)esp_get_free_heap_size(), battery,
                rtcValid ? "" : " (RTC CLEARED)");

  // 4. Console first, and print the history BEFORE appending - otherwise this
  //    boot's line would show up twice, once from the file and once as the
  //    current one. Raw printf in a banner block, matching the sensor dump in
  //    main.cpp: this is a table for a human, not a log event.
  printf("---------------- BOOT LOG ----------------\n");
  printRecent(printLines_);
  printf("> %s\n", line);
  printf("------------------------------------------\n\n");

  // 5. Persist. A card that is absent or unwritable costs us the history, not
  //    the boot - the caller carries on either way.
  if (!card_.isMounted()) {
    ESP_LOGW(TAG, "no card - this boot is on the console only.");
    return false;
  }
  if (!card_.appendLine(filePath_, line)) {
    ESP_LOGW(TAG, "could not append to %s", filePath_);
    return false;
  }
  if (!trimToCap()) {
    ESP_LOGW(TAG, "could not trim %s to %u lines", filePath_,
             (unsigned)maxLines_);
  }
  return true;
}
