#include "power/ChargerWatcher.h"

#include <cstdint>

#include "esp_attr.h"
#include "esp_log.h"

static const char* TAG = "ChargerWatcher";

// -----------------------------------------------------------------------------
//  RTC-backed state - see the header for why this cannot be a plain static.
//
//  The magic word is the guard BootJournal uses for the same reason: RTC slow
//  memory holds its contents through deep sleep and CPU resets but comes up as
//  garbage after a loss of the rail, so a mismatch is how "no history" is
//  recognised rather than trusting whatever bit pattern happens to be there.
// -----------------------------------------------------------------------------
static constexpr uint32_t kRtcMagic = 0x0C4A6E20U;  // "CHARGE"

// Three states, not a bool: "unknown" has to stay distinguishable from "absent",
// or the first cycle of a device that boots unplugged would look like a
// disconnect and publish a report nobody asked for.
static constexpr uint8_t kStateUnknown = 0;
static constexpr uint8_t kStatePresent = 1;
static constexpr uint8_t kStateAbsent  = 2;

RTC_DATA_ATTR static uint32_t rtcMagic;
RTC_DATA_ATTR static uint8_t  rtcChargerState;

ChargerWatcher::ChargerWatcher() : known_(false), present_(false) {
  if (rtcMagic != kRtcMagic) {
    // No history: the first boot of this device, or a real power cut. Claim the
    // region now so the NEXT wake recognises it as ours.
    rtcMagic        = kRtcMagic;
    rtcChargerState = kStateUnknown;
    return;
  }
  if (rtcChargerState == kStateUnknown) {
    return;  // ours, but nothing recorded in it yet
  }

  known_   = true;
  present_ = (rtcChargerState == kStatePresent);
}

bool ChargerWatcher::update(bool chargerPresent) {
  // The edge is evaluated BEFORE the state is overwritten. A first-ever call has
  // nothing to compare against and must not fire - see the header.
  const bool disconnected = known_ && present_ && !chargerPresent;

  known_          = true;
  present_        = chargerPresent;
  rtcMagic        = kRtcMagic;
  rtcChargerState = chargerPresent ? kStatePresent : kStateAbsent;

  if (disconnected) {
    ESP_LOGI(TAG, "charger disconnected - the pack is visible to the ADC again");
  }
  return disconnected;
}
