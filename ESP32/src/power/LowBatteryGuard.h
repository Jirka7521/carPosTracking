#pragma once

// =============================================================================
//  LowBatteryGuard  -  Stop the tracker before the pack runs flat.
// -----------------------------------------------------------------------------
//  Responsibility (single!): decide when the pack is too low to keep running,
//  and when it has recovered enough to start again. It measures through
//  BatteryMethods, remembers its verdict in RTC memory, and does nothing else -
//  DeepSleepController performs the shutdown and PresenceReporter says why.
//
//  Without it the device simply ran until the cell browned out: a silent stop
//  with nothing on the dashboard but a gap, and a Li-ion cell taken down to its
//  protection board's cut-off every time. With it the device says "battery low"
//  while it still can, then sleeps with everything off, waking only on a timer
//  to look at the pack again.
//
//      running ──(pack < cut-off)──▶ cut-off pending ──(main loop)──▶ LATCHED
//         ▲                                                              │
//         └──────(pack >= resume, or charging)────── boot re-check ◀─────┘
//
//  Two thresholds, not one: a pack that has just been relieved of the modem's
//  load bounces back a little, so a single threshold would see a "recovered"
//  pack on the first re-check, start everything up, sag below it again, and
//  loop. The gap between cut-off and resume is that hysteresis.
//
//  The boot check (checkAtBoot) runs before WiFi, the modem and MQTT exist, so a
//  re-check that finds the pack still low goes straight back to sleep having
//  spent a few tens of milliseconds and no radio time at all.
//
//  Charging always wins. On the T-SIM7000G the pack sense pin reads ~0 with USB
//  connected (see BatteryMethods), which a reading reports as ABSENT; an absent
//  reading clears the latch and never cuts off. Fail open in general: if the
//  ADC is unavailable the guard stays out of the way and tracking continues -
//  it exists to protect the device, not to be one more way it can stop.
//
//  RTC memory, magic-word guarded like ChargerWatcher: the latch has to survive
//  the deep sleep it causes. A real power loss clears it, which at worst costs
//  one more "battery low" report after a cold boot.
// =============================================================================

#include <cstdint>

#include "power/BatteryData.h"
#include "power/BatteryMethods.h"
#include "power/BatteryMethodsData.h"

class LowBatteryGuard {
 public:
  // What the boot check concluded.
  //   Run    : start up as normal
  //   Hold   : latched, and the pack has not recovered - go straight back to
  //            sleep without starting anything
  //   CutOff : not latched yet, but the pack is already below the cut-off -
  //            start up just far enough to report it, then shut down
  enum class BootVerdict { Run, Hold, CutOff };

  // Borrows `methods` (it must outlive this object).
  //   enabled  : false makes every check answer Run / never pending
  //   cutoffMv : pack voltage below which the device shuts down
  //   resumeMv : pack voltage at or above which a latched device starts again
  LowBatteryGuard(BatteryMethods& methods, bool enabled, uint32_t cutoffMv,
                  uint32_t resumeMv);

  // Take a spot reading and decide whether this boot may run. Call once, early,
  // after the pack sense pin is claimed and before anything loads the rail.
  BootVerdict checkAtBoot();

  // Fold in the reading this cycle already took. `measured` is BatteryMethods'
  // window; `fw` is BatteryMonitor's verdict, which carries the charging flag
  // and the AT+CBC voltage used when the window has nothing. Sets the cut-off
  // pending when the pack is below the threshold and not charging.
  void evaluateCycle(const BatteryMethodsSample& measured,
                     const BatteryStatus& fw);

  // True once a check has found the pack below the cut-off. The main loop acts
  // on it at its next safe point: latch(), then sleepForLowBattery().
  bool cutoffPending() const { return pending_; }

  // Remember, across the sleep that follows, that we stopped for the battery.
  void latch();

 private:
  // Forget the latch: the pack recovered or the charger is on.
  static void clearLatch();

  bool     enabled_;
  uint32_t cutoffMv_;
  uint32_t resumeMv_;
  bool     pending_;

  BatteryMethods& methods_;
};
