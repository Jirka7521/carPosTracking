#include "settings/SettingsStore.h"

#include <string>
#include <vector>

#include "esp_log.h"
#include "settings/SettingsCodec.h"

static const char* TAG = "SettingsStore";

SettingsStore::SettingsStore(SdCard& card, const char* filePath)
    : card_(card), filePath_(filePath) {}

DeviceSettings SettingsStore::load(const DeviceSettings& defaults) const {
  DeviceSettings settings = defaults;

  // The document is written as a single compact line, so one line is the whole
  // file. Reading it this way reuses SdCard's existing line primitives.
  std::vector<std::string> lines;
  if (!card_.readLines(filePath_, 1, lines)) {
    ESP_LOGW(TAG, "card unreadable - using default settings.");
    settings.clampToLimits();
    return settings;
  }
  if (lines.empty()) {
    ESP_LOGI(TAG, "no cached settings on card - using defaults.");
    settings.clampToLimits();
    return settings;
  }

  // Seeded with the defaults, so a document missing one of the two keys still
  // produces a complete, usable result.
  if (!SettingsCodec::decode(lines[0].data(), lines[0].size(), settings)) {
    ESP_LOGW(TAG, "cached settings are corrupt - using defaults.");
    settings = defaults;
  }

  settings.clampToLimits();
  ESP_LOGI(TAG, "loaded settings: interval=%us sleep_between=%s",
           (unsigned)settings.intervalSeconds(),
           settings.sleepBetweenSends() ? "yes" : "no");
  return settings;
}

bool SettingsStore::save(const DeviceSettings& settings) {
  const std::string json = SettingsCodec::encode(settings);
  if (json.empty()) {
    return false;
  }
  if (!card_.writeFile(filePath_, json)) {
    ESP_LOGW(TAG, "could not cache settings to %s", filePath_);
    return false;
  }
  ESP_LOGI(TAG, "cached settings to %s: %s", filePath_, json.c_str());
  return true;
}
