#include "mqtt/PresenceReporter.h"

#include <string>

#include "esp_log.h"

static const char* TAG = "PresenceReporter";

PresenceReporter::PresenceReporter(MqttClient& mqtt, StatusPublisher& publisher,
                                   EventLog& log, const char* resetReason)
    : mqtt_(mqtt),
      publisher_(publisher),
      log_(log),
      resetReason_(resetReason),
      announcedCount_(0),
      batteryPct_(-1) {}

void PresenceReporter::service() {
  if (!mqtt_.isConnected()) {
    return;
  }

  // Whatever the card holds happened before this connection, so it goes out
  // first - see the banner. Paced inside, so this is cheap on every pass.
  log_.flush();

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
  // A restart reason that has not gone out yet would otherwise be lost for
  // good: this boot is about to end, and the next one has a reason of its own.
  // A no-op while disconnected.
  service();

  std::string envelope;
  if (!publisher_.sealOffline(reason, sleepS, batteryPct_, detail, envelope)) {
    ESP_LOGW(TAG, "offline status could not be sealed - not recorded");
    return;
  }
  // Out of range this waits on the card: a sleep in a car park is still part
  // of the history, it just reaches the dashboard with the next connection.
  log_.submit(envelope);
}
