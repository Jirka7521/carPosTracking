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
//      INT1/INT2 are left unconnected here (interrupts are a future feature).
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
//  samples it once per report while AccelDebugStream samples it every second,
//  and two overlapping transactions on one I2C device handle would interleave.
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
  bool begin();

  // Read one sample. On success fills `out` (with valid = true) and returns true.
  // On any I2C error `out` is left invalid and false is returned.
  bool read(AccelSample& out);

 private:
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
