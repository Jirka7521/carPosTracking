#include "sensors/Adxl345.h"

#include "esp_log.h"
#include "util/ScopedLock.h"

static const char* TAG = "Adxl345";

namespace {
// ADXL345 register map (only the handful this driver touches).
constexpr uint8_t kRegDevId      = 0x00;  // reads back the fixed ID 0xE5
constexpr uint8_t kRegPowerCtl   = 0x2D;  // measure / standby control
constexpr uint8_t kRegDataFormat = 0x31;  // range + resolution
constexpr uint8_t kRegDataX0      = 0x32;  // first of the 6 data bytes (X,Y,Z)

constexpr uint8_t kDevIdValue   = 0xE5;   // expected DEVID
constexpr uint8_t kPowerCtlMeasure = 0x08;  // D3 = Measure mode
constexpr uint8_t kDataFormatFullRes = 0x08;  // D3 = FULL_RES, range bits 00 = +/-2g

// In full-resolution mode the scale is a fixed 256 LSB per g, so one raw count
// is 1/256 g regardless of the +/- range. See the header note.
constexpr double kCountsPerG = 256.0;

// Per-transaction I2C timeout. Generous - these are tiny transfers on a short
// bus, so anything slower than this is a wiring/hardware fault, not congestion.
constexpr int kI2cTimeoutMs = 100;
}  // namespace

Adxl345::Adxl345(int sdaPin, int sclPin, uint32_t clockHz, uint8_t i2cAddress)
    : sdaPin_(sdaPin),
      sclPin_(sclPin),
      clockHz_(clockHz),
      address_(i2cAddress) {}

bool Adxl345::begin() {
  // 1. Create the I2C master bus on the configured pins. Internal pull-ups are
  //    enabled as a convenience; the GY-291 breakout also carries its own.
  i2c_master_bus_config_t busCfg = {};
  busCfg.i2c_port                 = I2C_NUM_0;
  busCfg.sda_io_num               = static_cast<gpio_num_t>(sdaPin_);
  busCfg.scl_io_num               = static_cast<gpio_num_t>(sclPin_);
  busCfg.clk_source               = I2C_CLK_SRC_DEFAULT;
  busCfg.glitch_ignore_cnt        = 7;
  busCfg.flags.enable_internal_pullup = true;

  esp_err_t err = i2c_new_master_bus(&busCfg, &bus_);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "I2C bus init failed: %s", esp_err_to_name(err));
    return false;
  }

  // 2. Attach the ADXL345 as a device on that bus.
  i2c_device_config_t devCfg = {};
  devCfg.dev_addr_length       = I2C_ADDR_BIT_LEN_7;
  devCfg.device_address        = address_;
  devCfg.scl_speed_hz          = clockHz_;

  err = i2c_master_bus_add_device(bus_, &devCfg, &dev_);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "I2C add device 0x%02X failed: %s", address_,
             esp_err_to_name(err));
    return false;
  }

  // 3. Confirm the part is actually there and is an ADXL345.
  uint8_t devId = 0;
  if (!readRegisters(kRegDevId, &devId, 1) || devId != kDevIdValue) {
    ESP_LOGW(TAG, "ADXL345 not found (DEVID=0x%02X, expected 0x%02X)", devId,
             kDevIdValue);
    return false;
  }

  // 4. Full-resolution mode (fixed 3.9 mg/LSB), then leave standby for measure.
  if (!writeRegister(kRegDataFormat, kDataFormatFullRes) ||
      !writeRegister(kRegPowerCtl, kPowerCtlMeasure)) {
    ESP_LOGW(TAG, "ADXL345 configuration write failed");
    return false;
  }

  // 5. Arm the read lock. Created last, so it only exists on a sensor that is
  //    actually usable. A failure here is not fatal: read() still works, just
  //    without serialisation, which is no worse than before this class had a
  //    second caller.
  lock_ = xSemaphoreCreateMutex();
  if (lock_ == nullptr) {
    ESP_LOGW(TAG, "could not create the ADXL345 read lock - reads unserialised");
  }

  ready_ = true;
  ESP_LOGI(TAG, "ADXL345 ready on SDA=%d SCL=%d addr=0x%02X", sdaPin_, sclPin_,
           address_);
  return true;
}

bool Adxl345::read(AccelSample& out) {
  out = AccelSample();  // reset to invalid; only a full success marks it valid
  if (!ready_) {
    return false;
  }

  // One transaction at a time: the main loop and the debug stream both sample
  // this device, and the I2C driver makes no ordering promise across tasks.
  ScopedLock guard(lock_);

  // Six consecutive registers hold X0,X1,Y0,Y1,Z0,Z1 (little-endian per axis).
  uint8_t raw[6] = {0};
  if (!readRegisters(kRegDataX0, raw, sizeof(raw))) {
    ESP_LOGW(TAG, "ADXL345 data read failed");
    return false;
  }

  const int16_t xCounts = static_cast<int16_t>(raw[0] | (raw[1] << 8));
  const int16_t yCounts = static_cast<int16_t>(raw[2] | (raw[3] << 8));
  const int16_t zCounts = static_cast<int16_t>(raw[4] | (raw[5] << 8));

  out.xG    = xCounts / kCountsPerG;
  out.yG    = yCounts / kCountsPerG;
  out.zG    = zCounts / kCountsPerG;
  out.valid = true;
  return true;
}

bool Adxl345::writeRegister(uint8_t reg, uint8_t value) {
  const uint8_t payload[2] = {reg, value};
  const esp_err_t err =
      i2c_master_transmit(dev_, payload, sizeof(payload), kI2cTimeoutMs);
  return err == ESP_OK;
}

bool Adxl345::readRegisters(uint8_t reg, uint8_t* buf, std::size_t len) {
  // Write the start register, then read `len` bytes in one bus transaction.
  const esp_err_t err =
      i2c_master_transmit_receive(dev_, &reg, 1, buf, len, kI2cTimeoutMs);
  return err == ESP_OK;
}
