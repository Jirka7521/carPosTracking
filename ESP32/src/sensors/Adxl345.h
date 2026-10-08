#pragma once

// =============================================================================
//  Adxl345  -  Minimal I2C driver for the ADXL345 3-axis accelerometer.
// -----------------------------------------------------------------------------
//  Responsibility (single!): bring up the I2C bus, configure the ADXL345, and
//  hand back one instantaneous acceleration sample (X/Y/Z in g) on request. It
//  knows nothing about telemetry or the rest of the app.
//
//  Wiring (GY-291 breakout on the T-SIM7000G):
//      CS -> 3V3 and SDO -> GND  =>  I2C mode, address 0x53
//      SDA/SCL on the ESP32 pins passed to the constructor
//      INT1 -> the motion-wake GPIO (config::kMotionWakePin); INT2 unused
//
//  The one interrupt it drives is ACTIVITY, for the motion wake: armActivity()
//  sets it up on INT1, takeActivity() polls and clears it while the ESP32 is
//  awake, and DeepSleepController arms it as an ext1 wake source for the sleep.
//  Register values follow docs/MOTION-WAKE-THRESHOLDS.md. Whoever ARMS it is not
//  this class's business - it only knows the registers.
//
//  The device is read in FULL-RESOLUTION mode, where the scale is a fixed
//  3.9 mg/LSB (256 LSB/g) regardless of the selected +/- range - so the raw
//  16-bit counts convert to g by dividing by 256.
//
//  For now this class also owns the I2C *bus*, since the ADXL345 is the only
//  device on it. If a second I2C peripheral is ever added, lift the bus creation
//  out into a small shared I2cBus class and pass the handle in.
//
//  Thread safety: read() is safe to call from several tasks - the main loop
//  samples it while AccelPeakTracker samples it on its own cadence, and two
//  overlapping transactions on one I2C device handle would interleave.
//  begin() is deliberately NOT locked: it runs once at start-up, before any
//  other task exists.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "driver/i2c_master.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "sensors/AccelData.h"

class Adxl345 {
 public:
  // Stores its wiring; does not touch hardware until begin().
  //   sdaPin / sclPin : ESP32 I2C data / clock GPIOs
  //   clockHz         : I2C bus speed (e.g. 400000 for fast mode)
  //   i2cAddress      : 7-bit device address (0x53 with SDO tied low)
  Adxl345(int sdaPin, int sclPin, uint32_t clockHz, uint8_t i2cAddress);

  // Create the I2C bus + device, verify the ADXL345 is present (DEVID = 0xE5)
  // and put it into measurement mode. Returns true when ready to read. Safe to
  // treat as an optional subsystem: on failure it logs and returns false, and
  // read() then simply reports an invalid sample.
  //
  // Also switches activity detection OFF and clears its latch. After a motion
  // wake the interrupt that woke us is still latched with INT1 held high, and the
  // sensor may still be in its low-power sleep rate - neither belongs in a
  // running device until something arms it again on purpose.
  bool begin();

  // Read one sample. On success fills `out` (with valid = true) and returns true.
  // On any I2C error `out` is left invalid and false is returned.
  bool read(AccelSample& out);

  // Arm ACTIVITY detection on INT1: AC-coupled on X, Y and Z, so gravity and the
  // parking slope cancel out whichever way up the sensor is mounted.
  //   thresholdSteps : THRESH_ACT, 62.5 mg per step. 0 is raised to 1 - the
  //                    datasheet warns that 0 misbehaves.
  //   lowPower       : drop to the 25 Hz low-power output rate (~40 uA instead of
  //                    ~140 uA). Meant for a deep sleep, where nothing else reads
  //                    the sensor; awake callers leave it false.
  //
  // The AC reference is the acceleration at the moment this runs, so call it with
  // the device at rest - before a deep sleep, as the very last step. Returns false
  // when the sensor is not ready or a register write failed, in which case the
  // caller must not count on INT1.
  bool armActivity(uint8_t thresholdSteps, bool lowPower);

  // Read and clear the activity latch (INT_SOURCE). True when activity has been
  // seen since it was armed or last taken. Reading also drops INT1 again. False
  // on an I2C error as well - a missed poll is caught by the next one.
  bool takeActivity();

  // Activity detection off, latch cleared, normal 100 Hz output rate restored.
  bool disarmActivity();

 private:
  // disarmActivity() without the lock, for begin() - which runs before the lock
  // exists and before any other task could race it.
  bool disarmActivityUnlocked();

  // Write a single configuration register.
  bool writeRegister(uint8_t reg, uint8_t value);
  // Burst-read `len` bytes starting at `reg` (auto-incrementing address).
  bool readRegisters(uint8_t reg, uint8_t* buf, std::size_t len);

  int      sdaPin_;
  int      sclPin_;
  uint32_t clockHz_;
  uint8_t  address_;

  i2c_master_bus_handle_t bus_   = nullptr;
  i2c_master_dev_handle_t dev_   = nullptr;
  bool                    ready_ = false;

  // Serialises read(); created by begin(). Null when the sensor never came up,
  // which is harmless - ScopedLock ignores a null handle and read() bails on
  // ready_ anyway.
  SemaphoreHandle_t lock_ = nullptr;
};
