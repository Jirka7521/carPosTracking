#include "power/AdcSampler.h"

#include "esp_adc/adc_cali_scheme.h"
#include "esp_log.h"
#include "util/ScopedLock.h"

static const char* TAG = "AdcSampler";

// Everything on this board that is measured through the ADC is a divided rail
// swinging across the full 0-3.3 V range, so one attenuation serves every pin
// and the channel setup can stay uniform.
static constexpr adc_atten_t    kAtten    = ADC_ATTEN_DB_12;
static constexpr adc_bitwidth_t kBitwidth = ADC_BITWIDTH_DEFAULT;

// The unit this class owns. Both sense pins on the T-SIM7000G (GPIO35, GPIO36)
// are ADC1 inputs, and ADC2 is unusable while WiFi is up - so ADC1 it is.
static constexpr adc_unit_t kUnit = ADC_UNIT_1;

// Fallback reference voltage, only consulted on a chip whose eFuse carries
// neither Vref nor two-point data. 1100 mV is the ESP32's nominal design value.
static constexpr uint32_t kDefaultVrefMv = 1100;

AdcSampler::AdcSampler()
    : pins_{},
      pinCount_(0),
      handle_(nullptr),
      cali_(nullptr),
      ready_(false),
      lock_(nullptr) {}

AdcSampler::~AdcSampler() {
  if (cali_ != nullptr) {
    adc_cali_delete_scheme_line_fitting(cali_);
    cali_ = nullptr;
  }
  if (handle_ != nullptr) {
    adc_oneshot_del_unit(handle_);
    handle_ = nullptr;
  }
  if (lock_ != nullptr) {
    vSemaphoreDelete(lock_);
    lock_ = nullptr;
  }
  ready_ = false;
}

bool AdcSampler::begin() {
  if (ready_) {
    return true;  // idempotent: two owners of this object share one unit
  }

  // Before the unit exists, so no conversion can ever run unguarded. A failure
  // here is logged and carried on from: ScopedLock treats a null handle as "no
  // synchronisation", which is what this class did before it was shared.
  lock_ = xSemaphoreCreateMutex();
  if (lock_ == nullptr) {
    ESP_LOGW(TAG, "no ADC lock - conversions will run unsynchronised");
  }

  adc_oneshot_unit_init_cfg_t initCfg = {};
  initCfg.unit_id                     = kUnit;
  esp_err_t err = adc_oneshot_new_unit(&initCfg, &handle_);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "ADC unit init failed: %s", esp_err_to_name(err));
    return false;
  }
  ready_ = true;

  // Calibration is best-effort (see the header): report which flavour the chip
  // actually has, because it decides how much the millivolt columns can be
  // trusted, and a silent fallback to the nominal Vref is exactly the kind of
  // several-percent error that later gets blamed on the battery.
  adc_cali_line_fitting_efuse_val_t efuseVal =
      ADC_CALI_LINE_FITTING_EFUSE_VAL_DEFAULT_VREF;
  adc_cali_scheme_line_fitting_check_efuse(&efuseVal);

  adc_cali_line_fitting_config_t caliCfg = {};
  caliCfg.unit_id                        = kUnit;
  caliCfg.atten                          = kAtten;
  caliCfg.bitwidth                       = kBitwidth;
  caliCfg.default_vref                   = kDefaultVrefMv;
  err = adc_cali_create_scheme_line_fitting(&caliCfg, &cali_);
  if (err != ESP_OK) {
    cali_ = nullptr;
    ESP_LOGW(TAG, "ADC calibration unavailable (%s) - raw counts only",
             esp_err_to_name(err));
    return true;  // raw reads still work; that is enough to be "ready"
  }

  switch (efuseVal) {
    case ADC_CALI_LINE_FITTING_EFUSE_VAL_EFUSE_TP:
      ESP_LOGI(TAG, "ADC1 ready (calibration: eFuse two-point)");
      break;
    case ADC_CALI_LINE_FITTING_EFUSE_VAL_EFUSE_VREF:
      ESP_LOGI(TAG, "ADC1 ready (calibration: eFuse Vref)");
      break;
    default:
      ESP_LOGW(TAG, "ADC1 ready (calibration: default %umV Vref - readings may "
                    "be off by a few percent)",
               (unsigned)kDefaultVrefMv);
      break;
  }
  return true;
}

bool AdcSampler::addPin(int gpio) {
  if (!ready_) {
    return false;
  }

  // Held for the whole method: the table this appends to is what every
  // conversion reads. In practice all the pins are claimed during bring-up,
  // before the sampling task exists, but that ordering is a property of
  // main.cpp rather than of this class - so it is not relied on here.
  ScopedLock guard(lock_);

  adc_channel_t existing = ADC_CHANNEL_0;
  if (channelFor(gpio, existing)) {
    return true;  // already configured - adding a pin twice is harmless
  }
  if (pinCount_ >= kMaxPins) {
    ESP_LOGW(TAG, "no free slot for GPIO%d (max %u pins)", gpio,
             (unsigned)kMaxPins);
    return false;
  }

  // Let the driver map the pin, so the GPIO number in Config.h stays the single
  // source of truth and no channel table has to be kept in step with it.
  adc_unit_t    unit    = kUnit;
  adc_channel_t channel = ADC_CHANNEL_0;
  esp_err_t     err     = adc_oneshot_io_to_channel(gpio, &unit, &channel);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "GPIO%d is not an ADC pin: %s", gpio, esp_err_to_name(err));
    return false;
  }
  if (unit != kUnit) {
    // ADC2 shares its hardware with the WiFi radio, so a pin over there cannot
    // be read reliably on this device. Refuse rather than return noise.
    ESP_LOGW(TAG, "GPIO%d is on ADC%d, not ADC1", gpio, (int)unit + 1);
    return false;
  }

  adc_oneshot_chan_cfg_t chanCfg = {};
  chanCfg.atten                  = kAtten;
  chanCfg.bitwidth               = kBitwidth;
  err = adc_oneshot_config_channel(handle_, channel, &chanCfg);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "ADC channel config failed for GPIO%d: %s", gpio,
             esp_err_to_name(err));
    return false;
  }

  pins_[pinCount_].gpio    = gpio;
  pins_[pinCount_].channel = channel;
  ++pinCount_;
  return true;
}

bool AdcSampler::readRaw(int gpio, int& rawOut) const {
  if (!ready_) {
    return false;
  }

  // One conversion at a time across the whole firmware - see the banner. Taking
  // a mutex through its handle does not mutate the handle, so this stays const.
  ScopedLock guard(lock_);

  adc_channel_t channel = ADC_CHANNEL_0;
  if (!channelFor(gpio, channel)) {
    return false;
  }
  return adc_oneshot_read(handle_, channel, &rawOut) == ESP_OK;
}

bool AdcSampler::readMv(int gpio, int& mvOut) const {
  int raw = 0;
  if (!readRaw(gpio, raw)) {
    return false;
  }
  return rawToMv(raw, mvOut);
}

bool AdcSampler::rawToMv(int raw, int& mvOut) const {
  if (cali_ == nullptr) {
    return false;
  }
  return adc_cali_raw_to_voltage(cali_, raw, &mvOut) == ESP_OK;
}

bool AdcSampler::channelFor(int gpio, adc_channel_t& channelOut) const {
  for (std::size_t i = 0; i < pinCount_; ++i) {
    if (pins_[i].gpio == gpio) {
      channelOut = pins_[i].channel;
      return true;
    }
  }
  return false;
}
