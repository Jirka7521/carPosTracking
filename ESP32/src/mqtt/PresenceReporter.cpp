#include "mqtt/PresenceReporter.h"

#include "esp_log.h"

static const char* TAG = "PresenceReporter";

PresenceReporter::PresenceReporter(MqttClient& mqtt, StatusPublisher& publisher,
                                   const char* resetReason)
    : mqtt_(mqtt),
      publisher_(publisher),
      resetReason_(resetReason),
      announcedCount_(0),
      batteryPct_(-1) {}

void PresenceReporter::service() {
  if (!mqtt_.isConnected()) {
    return;
  }
  const uint32_t count = mqtt_.connectCount();
  if (count == announcedCount_) {
    return;  // this connection has already been announced
  }

  // One attempt per connection, recorded BEFORE trying: a link that keeps
  // timing out must not turn every main-loop pass into another blocking
  // publish. The dashboard falls back on the fixes themselves to tell that the
  // device is up, so a lost online message costs nothing lasting.
  announcedCount_ = count;
  if (publisher_.publishOnline(resetReason_)) {
    resetReason_ = nullptr;  // delivered - later reconnects are not restarts
  }
}

void PresenceReporter::noteBattery(const BatteryStatus& battery) {
  if (!battery.valid) {
    return;
  }
  // percent 0 is the charging sentinel and is passed on as such - the API
  // reads it the same way it reads it in a position.
  batteryPct_ = battery.percent;
}

void PresenceReporter::reportOffline(OfflineReason reason, uint32_t sleepS,
                                     const char* detail) {
  if (!mqtt_.isConnected()) {
    ESP_LOGI(TAG, "not connected - going offline without a status message");
    return;
  }
  // A restart reason that has not gone out yet would otherwise be lost for
  // good: this boot is about to end, and the next one has a reason of its own.
  service();
  publisher_.publishOffline(reason, sleepS, batteryPct_, detail);
}
