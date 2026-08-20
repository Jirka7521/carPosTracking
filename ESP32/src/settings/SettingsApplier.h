#pragma once

// =============================================================================
//  SettingsApplier  -  Push the runtime settings into the objects they govern.
// -----------------------------------------------------------------------------
//  Responsibility (single!): translate a DeviceSettings into calls on the
//  collaborators whose behaviour it controls. Right now that is the two SD-backed
//  queues, whose limits used to be fixed at construction from Config.h and are
//  now whatever the server last said.
//
//  Why a class for three setter calls? Because the settings are adopted in two
//  different places in main() - once after the retained config is fetched at
//  start-up, and again after every poll() in the loop - and a copy-pasted block
//  in both is exactly the sort of thing that gets updated in one place only when
//  a seventh knob appears. Adding a knob now means editing one method here.
//
//  Not every setting passes through this class. `interval_s`, `sleep_between`
//  and `fix_timeout_s` are read directly by the loop that uses them, so pushing
//  them anywhere would just be indirection; only the settings that live inside
//  another object's state need applying.
//
//  Borrows its collaborators (they must outlive it), like every other class here.
// =============================================================================

#include "sdcard/FixQueue.h"
#include "sdcard/RetryQueue.h"
#include "settings/DeviceSettings.h"

class SettingsApplier {
 public:
  SettingsApplier(FixQueue& queue, RetryQueue& retryQueue);

  // Make `settings` current. Safe (and cheap) to call with settings that have
  // not changed: every setter below is a no-op when the value already matches.
  void apply(const DeviceSettings& settings);

 private:
  FixQueue&   queue_;
  RetryQueue& retryQueue_;
};
