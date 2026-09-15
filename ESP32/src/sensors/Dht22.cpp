#include "sensors/Dht22.h"

#include <cstring>

#include "driver/gpio.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/task.h"

static const char* TAG = "Dht22";

namespace {

// One microsecond per RMT tick. The pulses this decodes are 26-80 us, so a
// microsecond is both fine enough to separate them and coarse enough that a
// whole frame fits in one 64-symbol memory block.
constexpr uint32_t kResolutionHz = 1000000;

// Pulses shorter than this are ringing on the lead, not signal.
constexpr uint32_t kGlitchFilterNs = 1000;

// An idle stretch this long ends the frame. The longest legitimate gap inside a
// frame is the 80 us response, so 200 us terminates promptly without ever
// cutting a frame short.
constexpr uint32_t kIdleThresholdNs = 200000;

// The host holds the line down this long to ask for a reading. The datasheet
// wants at least 1 ms; 1.2 ms leaves room for scheduler jitter without
// approaching the 10 ms ceiling.
constexpr uint32_t kStartPulseMs = 2;

// A frame is ~5 ms. Ten is generous enough to cover a slow sensor and short
// enough that a missing one cannot stall the sampling task.
constexpr uint32_t kReceiveTimeoutMs = 50;

}  // namespace

Dht22::Dht22(int dataPin, uint32_t minIntervalMs)
    : dataPin_(dataPin),
      // The part cannot be read faster than this no matter what Config.h says;
      // clamping here means a mistaken config costs accuracy, not correctness.
      minIntervalMs_(minIntervalMs < 2000 ? 2000 : minIntervalMs),
      channel_(nullptr),
      doneQueue_(nullptr),
      receiveConfig_(),
      symbols_(),
      lastAttemptUs_(0) {}

Dht22::~Dht22() {
  if (channel_ != nullptr) {
    rmt_disable(channel_);
    rmt_del_channel(channel_);
    channel_ = nullptr;
  }
  if (doneQueue_ != nullptr) {
    vQueueDelete(doneQueue_);
    doneQueue_ = nullptr;
  }
}

bool IRAM_ATTR Dht22::onReceiveDone(rmt_channel_handle_t channel,
                                    const rmt_rx_done_event_data_t* data,
                                    void* userData) {
  // Interrupt context: post the event and get out. No logging, no decoding.
  QueueHandle_t  queue      = static_cast<QueueHandle_t>(userData);
  BaseType_t     higherWoke = pdFALSE;
  xQueueSendFromISR(queue, data, &higherWoke);
  (void)channel;
  return higherWoke == pdTRUE;
}

bool Dht22::begin() {
  doneQueue_ = xQueueCreate(1, sizeof(rmt_rx_done_event_data_t));
  if (doneQueue_ == nullptr) {
    ESP_LOGE(TAG, "could not create the receive queue");
    return false;
  }

  rmt_rx_channel_config_t channelCfg = {};
  channelCfg.gpio_num          = static_cast<gpio_num_t>(dataPin_);
  channelCfg.clk_src           = RMT_CLK_SRC_DEFAULT;
  channelCfg.resolution_hz     = kResolutionHz;
  channelCfg.mem_block_symbols = kMaxSymbols;

  esp_err_t err = rmt_new_rx_channel(&channelCfg, &channel_);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "RMT channel on GPIO %d failed: %s", dataPin_,
             esp_err_to_name(err));
    channel_ = nullptr;
    return false;
  }

  rmt_rx_event_callbacks_t callbacks = {};
  callbacks.on_recv_done             = &Dht22::onReceiveDone;
  err = rmt_rx_register_event_callbacks(channel_, &callbacks, doneQueue_);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "RMT callback registration failed: %s", esp_err_to_name(err));
    return false;
  }

  err = rmt_enable(channel_);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "RMT enable failed: %s", esp_err_to_name(err));
    return false;
  }

  // Hand the pad back its output driver. rmt_new_rx_channel() routes the pin to
  // the peripheral as an input; re-declaring it open-drain input+output leaves
  // that input path intact while letting the CPU pull the line down for the
  // start pulse. Open-drain rather than push-pull because the sensor drives the
  // same wire - two push-pull outputs fighting is a short, not a protocol.
  gpio_set_direction(static_cast<gpio_num_t>(dataPin_),
                     GPIO_MODE_INPUT_OUTPUT_OD);
  gpio_set_level(static_cast<gpio_num_t>(dataPin_), 1);

  receiveConfig_.signal_range_min_ns = kGlitchFilterNs;
  receiveConfig_.signal_range_max_ns = kIdleThresholdNs;

  ESP_LOGI(TAG, "DHT22 ready on GPIO %d (min interval %ums).", dataPin_,
           (unsigned)minIntervalMs_);
  return true;
}

void Dht22::sendStartPulse() const {
  const gpio_num_t pin = static_cast<gpio_num_t>(dataPin_);
  gpio_set_level(pin, 0);
  vTaskDelay(pdMS_TO_TICKS(kStartPulseMs));
  gpio_set_level(pin, 1);  // release; the sensor takes the line from here
}

bool Dht22::decode(const rmt_symbol_word_t* symbols, std::size_t count,
                   AmbientSample& out) const {
  // The capture also holds the host's start pulse and the sensor's 80/80 us
  // response, and how many symbols those occupy depends on exactly when the
  // receive armed. The data bits are always the LAST 40, so count back from the
  // end rather than trying to parse a preamble of variable length.
  if (count < kFrameBits) {
    ESP_LOGW(TAG, "short frame: %u symbols", (unsigned)count);
    return false;
  }
  const rmt_symbol_word_t* bits = symbols + (count - kFrameBits);

  uint8_t bytes[kFrameBytes] = {0};
  for (std::size_t i = 0; i < kFrameBits; ++i) {
    // Each symbol is one low period followed by one high period, and it is the
    // HIGH one whose length carries the bit. Which half of the symbol that is
    // depends on the polarity the capture started at, so pick by level rather
    // than assuming.
    uint32_t highUs = 0;
    if (bits[i].level0 == 1) {
      highUs = bits[i].duration0;
    } else if (bits[i].level1 == 1) {
      highUs = bits[i].duration1;
    } else {
      ESP_LOGW(TAG, "symbol %u has no high period", (unsigned)i);
      return false;
    }

    if (highUs > kBitThresholdUs) {
      bytes[i / 8] = static_cast<uint8_t>(bytes[i / 8] | (0x80u >> (i % 8)));
    }
  }

  const uint8_t expected = static_cast<uint8_t>(bytes[0] + bytes[1] + bytes[2] +
                                                bytes[3]);
  if (expected != bytes[4]) {
    ESP_LOGW(TAG, "checksum mismatch (got 0x%02X, want 0x%02X)", bytes[4],
             expected);
    return false;
  }

  const uint16_t rawHumidity =
      static_cast<uint16_t>((bytes[0] << 8) | bytes[1]);
  const uint16_t rawTemperature =
      static_cast<uint16_t>((bytes[2] << 8) | bytes[3]);

  // The DHT22 marks a negative temperature with the TOP BIT of its temperature
  // word and leaves the rest as a plain magnitude - it is not two's complement.
  // Treating it as signed is the classic way to read -1.5 C as +3276.7 C.
  const bool negative = (rawTemperature & 0x8000u) != 0;
  const float magnitude =
      static_cast<float>(rawTemperature & 0x7FFFu) / 10.0f;

  out.humidityPct   = static_cast<float>(rawHumidity) / 10.0f;
  out.temperatureC  = negative ? -magnitude : magnitude;
  out.valid         = true;
  return true;
}

bool Dht22::read(AmbientSample& out) {
  out = AmbientSample();

  if (channel_ == nullptr) {
    return false;
  }

  // The floor covers two different things with one rule: the sensor's own
  // sampling rate, and the ~2 s it needs after power-on before its first
  // conversion exists. lastAttemptUs_ starts at 0, so the first call on a fresh
  // boot is refused - which is why the first report after a deep-sleep wake
  // carries no ambient fields.
  const int64_t nowUs = esp_timer_get_time();
  if (nowUs - lastAttemptUs_ < static_cast<int64_t>(minIntervalMs_) * 1000) {
    return false;
  }
  lastAttemptUs_ = nowUs;

  // Arm the receive BEFORE the start pulse, so the capture cannot miss the
  // sensor's response while the CPU is still getting round to it.
  esp_err_t err = rmt_receive(channel_, symbols_, sizeof(symbols_),
                              &receiveConfig_);
  if (err != ESP_OK) {
    ESP_LOGW(TAG, "rmt_receive failed: %s", esp_err_to_name(err));
    return false;
  }

  sendStartPulse();

  rmt_rx_done_event_data_t done = {};
  if (xQueueReceive(doneQueue_, &done, pdMS_TO_TICKS(kReceiveTimeoutMs)) !=
      pdTRUE) {
    // No frame at all: the sensor is absent, unpowered or on another pin.
    ESP_LOGW(TAG, "no response on GPIO %d", dataPin_);
    return false;
  }

  return decode(done.received_symbols, done.num_symbols, out);
}
