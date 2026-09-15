#pragma once

// =============================================================================
//  Dht22  -  Read the DHT22 / AM2302 temperature + humidity sensor.
// -----------------------------------------------------------------------------
//  Responsibility (single!): speak the DHT22's one-wire protocol and hand back
//  one checksummed AmbientSample. It stores nothing, aggregates nothing and
//  publishes nothing - AmbientWindowSampler collects the readings over a cycle
//  and TelemetryPublisher decides what reaches the wire.
//
//  WHY RMT AND NOT BIT-BANGING. The protocol encodes each bit in the LENGTH of
//  a high pulse: ~26 us means 0, ~70 us means 1, and the whole 40-bit frame
//  takes about 5 ms. Timing that with the CPU means either polling the pin in a
//  tight loop - where one FreeRTOS pre-emption or a WiFi interrupt lands in the
//  middle of a pulse and corrupts the frame - or disabling interrupts for the
//  full 5 ms, on a device whose radio is up and whose MQTT session is live.
//  Neither is acceptable here, so the RMT peripheral captures the pulse train in
//  hardware and the CPU decodes it afterwards at its leisure.
//
//  THE START PULSE, and the one subtlety in this driver: the exchange begins
//  with the HOST pulling the line low for >1 ms, which an RX-only channel cannot
//  do. The pad is therefore left in open-drain input+output mode after the RMT
//  channel claims it, so the CPU can still pull it down while the peripheral
//  keeps watching. The receive is armed BEFORE the start pulse is driven, which
//  means the captured stream also contains the host's own pulse and the sensor's
//  80/80 us response - decode() skips past them by taking the LAST 40 symbols.
//
//  THE 2 SECOND FLOOR is the sensor's, not a policy: the DHT22 samples its own
//  element that slowly and returns a stale frame (or nothing) if polled faster.
//  read() enforces it and reports "not ready" rather than handing back a value
//  it did not actually re-measure. The same rule covers the ~2 s warm-up after
//  power-on, which is why the first report of a deep-sleep cycle usually carries
//  no ambient fields at all.
//
//  Threading: one caller at a time. The window sampler owns the instance and is
//  the only thing that calls read(); nothing else in the firmware touches it.
// =============================================================================

#include <cstddef>
#include <cstdint>

#include "driver/rmt_rx.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "sensors/AmbientData.h"

class Dht22 {
 public:
  // Does not power the sensor (it sits on the 3V3 rail) - only claims the pin.
  //   dataPin           : GPIO wired to the sensor's DATA leg. Must be
  //                       output-capable: the host drives the start pulse, so
  //                       the input-only pins 34-39 cannot be used.
  //   minIntervalMs     : refuse to re-read inside this window (>= 2000)
  explicit Dht22(int dataPin, uint32_t minIntervalMs);
  ~Dht22();

  // Claim the pin and create the RMT receive channel. Returns false - logged,
  // and carried on from like every other optional subsystem - if the peripheral
  // could not be allocated.
  bool begin();

  // Run one exchange and decode it. Fills `out` and returns out.valid.
  //
  // Returns false without touching the sensor when called inside
  // minIntervalMs of the previous attempt, so a caller that polls too eagerly
  // gets "no reading" instead of a stale one.
  bool read(AmbientSample& out);

 private:
  // How many RMT symbols one frame can produce: 40 data bits, the sensor's
  // response pair, the host's own start pulse, and headroom for the glitches a
  // long lead picks up. Sized to the peripheral's 64-symbol block.
  static constexpr std::size_t kMaxSymbols = 64;

  // Bits per frame, and the bytes they pack into (humidity hi/lo, temperature
  // hi/lo, checksum).
  static constexpr std::size_t kFrameBits  = 40;
  static constexpr std::size_t kFrameBytes = 5;

  // A high pulse longer than this is a 1, shorter is a 0. The part emits ~26 us
  // and ~70 us, so the midpoint has enormous margin either side.
  static constexpr uint32_t kBitThresholdUs = 50;

  // RMT receive-done callback. Runs in interrupt context: it may only post the
  // event to the queue, never log or decode.
  static bool IRAM_ATTR onReceiveDone(rmt_channel_handle_t channel,
                                      const rmt_rx_done_event_data_t* data,
                                      void* userData);

  // Drive the >1 ms start pulse and release the line.
  void sendStartPulse() const;

  // Turn a captured symbol array into five bytes and verify the checksum.
  // Returns false when the frame is short, malformed or fails its checksum.
  bool decode(const rmt_symbol_word_t* symbols, std::size_t count,
              AmbientSample& out) const;

  int      dataPin_;
  uint32_t minIntervalMs_;

  rmt_channel_handle_t channel_;
  QueueHandle_t        doneQueue_;
  rmt_receive_config_t receiveConfig_;
  rmt_symbol_word_t    symbols_[kMaxSymbols];

  // esp_timer_get_time() of the last attempt, in microseconds; 0 until the
  // first one. Enforces both the inter-read floor and the power-on warm-up.
  int64_t lastAttemptUs_;
};
