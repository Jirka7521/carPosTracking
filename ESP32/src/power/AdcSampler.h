#pragma once

// =============================================================================
//  AdcSampler  -  The one owner of the ESP32's ADC1 oneshot unit.
// -----------------------------------------------------------------------------
//  Responsibility (single!): hold the ADC1 oneshot handle plus its calibration
//  scheme, and hand out raw counts / calibrated millivolts for any GPIO on that
//  unit. It knows nothing about batteries, solar panels or percentages - it is
//  the analog equivalent of SerialPort: a thin, reusable transport that the
//  measuring classes sit on top of.
//
//  Why it exists at all: the IDF refuses a SECOND handle on a unit that is
//  already claimed ("adc1 is already in use", ESP_ERR_NOT_FOUND - see
//  adc_oneshot.c). BatteryMonitor used to create the unit itself, which left no
//  way for BatteryMethods to read GPIO36 alongside it. One owner, borrowed by
//  reference like every other collaborator in this firmware, is the fix.
//
//  Calibration: on the ESP32 the only scheme is LINE FITTING, which is what the
//  Arduino world calls analogReadMilliVolts()/esp_adc_cal. It needs eFuse Vref
//  or two-point values that not every chip has burnt, so it is OPTIONAL: a chip
//  without them still reads raw counts happily, hasCalibration() reports false
//  and the caller falls back to naive maths. That is a measurable loss of
//  accuracy (several percent), not a failure.
// =============================================================================

#include <cstddef>

#include "esp_adc/adc_cali.h"
#include "esp_adc/adc_oneshot.h"

class AdcSampler {
 public:
  AdcSampler();
  ~AdcSampler();

  // Claim the ADC1 oneshot unit and (best effort) its calibration scheme.
  // Returns true once raw reads are possible; check hasCalibration() separately
  // for whether millivolt reads are trustworthy.
  bool begin();

  // True between a successful begin() and destruction.
  bool isReady() const { return ready_; }

  // True when the chip carried the eFuse data the line-fitting scheme needs, so
  // readMv()/rawToMv() return calibrated values rather than failing.
  bool hasCalibration() const { return cali_ != nullptr; }

  // Route `gpio` to its ADC1 channel and configure it for full-scale reads
  // (12 dB attenuation - the whole 0-3.3 V swing). Must be called once per pin
  // after begin(); calling it again for the same pin is a no-op. Returns false
  // for a pin that is not on ADC1, or when the channel could not be configured.
  bool addPin(int gpio);

  // One conversion on `gpio` (which must have been added). Returns false if the
  // pin is unknown or the conversion failed.
  bool readRaw(int gpio, int& rawOut) const;

  // One conversion converted to millivolts AT THE PIN - the divider on the board
  // is the caller's business, not ours. Needs hasCalibration().
  bool readMv(int gpio, int& mvOut) const;

  // Convert a raw count the caller already has (e.g. the mean of a burst) to
  // millivolts. Needs hasCalibration().
  bool rawToMv(int raw, int& mvOut) const;

 private:
  // Look up a previously added pin. Linear over a handful of entries - a map
  // would cost more than the scan it replaces.
  bool channelFor(int gpio, adc_channel_t& channelOut) const;

  // How many distinct pins this sampler can serve. Two are in use today (the
  // charge/VBAT sense and the solar input); the slack is free.
  static constexpr std::size_t kMaxPins = 4;

  struct PinEntry {
    int           gpio;
    adc_channel_t channel;
  };

  PinEntry    pins_[kMaxPins];
  std::size_t pinCount_;

  adc_oneshot_unit_handle_t handle_;  // owned; nullptr until begin()
  adc_cali_handle_t         cali_;    // owned; nullptr when uncalibrated
  bool                      ready_;
};
