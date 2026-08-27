#pragma once

// =============================================================================
//  FixAverager  -  Publish the average of several readings, not one raw fix.
// -----------------------------------------------------------------------------
//  A single SIM7000G solution carries several metres of noise, and the *first*
//  solution after a lock is the least settled of all: the receiver is still
//  converging when it first declares a fix. Publishing that one reading is what
//  makes a parked car's track jitter.
//
//  So this class does what a surveyor does - takes a few readings and averages
//  them:
//
//      reading 1   the fix waitForFix() returned  -> DISCARDED
//      reading 2   |
//      reading 3   +--> averaged -> this is what gets published
//      reading 4   |
//
//  Four positions are read in total. The discarded one costs nothing extra: it
//  is the fix the acquisition produced anyway.
//
//  Collaborator:
//      GnssModule  -> acquires the lock and reads each sample
//
//  Usage - a drop-in for the GnssModule::waitForFix() call it replaces:
//      FixAverager averager(gnss);
//      GnssFix fix;
//      if (averager.acquire(fix, timeoutMs, pollStepMs, onEachPoll)) {
//        ...publish fix...   // already averaged, in place
//      }
//
//  A short burst is deliberately NOT allowed to fail a cycle: whatever samples
//  arrive are averaged (three, two, or one), and if none do, the acquisition fix
//  is published unchanged. A cycle never goes silent because the burst was
//  unlucky - see acquire().
// =============================================================================

#include <cstdint>
#include <functional>

#include "gnss/GnssData.h"
#include "gnss/GnssModule.h"

class FixAverager {
 public:
  // Borrows (does not own) a GnssModule.
  explicit FixAverager(GnssModule& gnss);

  // Acquire a fix, then replace it with the mean of the next
  // config::kFixAverageSampleCount readings.
  //
  // The first three arguments are passed straight through to
  // GnssModule::waitForFix(), so this call is a drop-in replacement for it:
  // `timeoutMs` still bounds the acquisition, `pollStepMs` still paces it, and
  // `onEachRead` still runs after every acquisition poll (the caller uses it to
  // flush the SD backlog and adopt a config that arrived mid-wait).
  //
  // Returns exactly what waitForFix() would have returned - true only when
  // `out` holds a real position. The averaging burst that follows a successful
  // acquisition can never turn a fix into a failure; at worst it leaves the
  // acquired fix alone.
  //
  // Costs kFixAverageSampleCount * kFixAverageStepMs of awake time (~3 s by
  // default) on top of the acquisition. That cost is FIXED - the burst never
  // retries a bad reading - so a poor sky is no slower than a good one.
  bool acquire(GnssFix& out, uint32_t timeoutMs, uint32_t pollStepMs,
               const std::function<void()>& onEachRead = {});

 private:
  // Runs the burst: reads kFixAverageSampleCount samples, spaced
  // kFixAverageStepMs apart, and writes their average into `out`. Leaves `out`
  // untouched and returns 0 when no sample was usable; otherwise returns how
  // many samples went into the average.
  uint8_t collect(GnssFix& out);

  GnssModule& gnss_;
};
