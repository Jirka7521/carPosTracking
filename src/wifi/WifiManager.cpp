#include "WifiManager.h"

#include <cstring>

#include "esp_event.h"
#include "esp_log.h"
#include "esp_netif.h"
#include "esp_wifi.h"
#include "nvs_flash.h"

static const char* TAG = "WifiManager";

// Event-group bits used to hand the connection result back from the (async)
// event callback to the connect() call that is blocking on it.
static constexpr EventBits_t kConnectedBit = BIT0;  // Got an IP address
static constexpr EventBits_t kFailedBit    = BIT1;  // Gave up after retries

WifiManager::WifiManager(const char* ssid, const char* password, int maxRetries)
    : ssid_(ssid),
      password_(password),
      maxRetries_(maxRetries),
      initialised_(false),
      connected_(false),
      retryCount_(0),
      events_(nullptr) {}

bool WifiManager::begin() {
  if (initialised_) {
    return true;
  }

  // 1. NVS is required by the WiFi driver to store calibration / PHY data.
  esp_err_t nvs = nvs_flash_init();
  if (nvs == ESP_ERR_NVS_NO_FREE_PAGES || nvs == ESP_ERR_NVS_NEW_VERSION_FOUND) {
    // The partition is full or from an older layout - wipe and retry.
    ESP_ERROR_CHECK(nvs_flash_erase());
    nvs = nvs_flash_init();
  }
  if (nvs != ESP_OK) {
    ESP_LOGE(TAG, "NVS init failed: %s", esp_err_to_name(nvs));
    return false;
  }

  // 2. Network interface + default event loop + the station netif object.
  ESP_ERROR_CHECK(esp_netif_init());
  ESP_ERROR_CHECK(esp_event_loop_create_default());
  esp_netif_create_default_wifi_sta();

  // 3. Bring up the WiFi driver with default resource sizes.
  wifi_init_config_t initConfig = WIFI_INIT_CONFIG_DEFAULT();
  ESP_ERROR_CHECK(esp_wifi_init(&initConfig));

  // 4. Subscribe to the WiFi and IP events we care about. `this` is passed so
  //    the static callback can route events back into our member state.
  events_ = xEventGroupCreate();
  ESP_ERROR_CHECK(esp_event_handler_instance_register(
      WIFI_EVENT, ESP_EVENT_ANY_ID, &WifiManager::eventHandler, this, nullptr));
  ESP_ERROR_CHECK(esp_event_handler_instance_register(
      IP_EVENT, IP_EVENT_STA_GOT_IP, &WifiManager::eventHandler, this,
      nullptr));

  // 5. Station mode, with the credentials from Config.h.
  wifi_config_t wifiConfig = {};
  std::strncpy(reinterpret_cast<char*>(wifiConfig.sta.ssid), ssid_,
               sizeof(wifiConfig.sta.ssid) - 1);
  std::strncpy(reinterpret_cast<char*>(wifiConfig.sta.password), password_,
               sizeof(wifiConfig.sta.password) - 1);

  ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
  ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_STA, &wifiConfig));

  initialised_ = true;
  ESP_LOGI(TAG, "WiFi stack initialised (SSID \"%s\").", ssid_);
  return true;
}

bool WifiManager::connect(uint32_t timeoutMs) {
  if (!initialised_) {
    ESP_LOGE(TAG, "connect() called before begin().");
    return false;
  }

  // Fresh attempt: clear previous result bits and the retry counter.
  retryCount_ = 0;
  connected_  = false;
  xEventGroupClearBits(events_, kConnectedBit | kFailedBit);

  ESP_LOGI(TAG, "Connecting to \"%s\"...", ssid_);
  // Starting the driver fires WIFI_EVENT_STA_START, where we call
  // esp_wifi_connect(); the rest of the handshake runs in eventHandler().
  ESP_ERROR_CHECK(esp_wifi_start());

  // Block until the callback reports success, failure, or we time out.
  EventBits_t bits = xEventGroupWaitBits(events_, kConnectedBit | kFailedBit,
                                         pdFALSE, pdFALSE,
                                         pdMS_TO_TICKS(timeoutMs));

  if (bits & kConnectedBit) {
    ESP_LOGI(TAG, "Connected.");
    return true;
  }
  if (bits & kFailedBit) {
    ESP_LOGE(TAG, "Failed to connect after %d retries.", maxRetries_);
  } else {
    ESP_LOGE(TAG, "Connect timed out after %lu ms.", (unsigned long)timeoutMs);
  }
  return false;
}

bool WifiManager::isConnected() const { return connected_; }

void WifiManager::disconnect() {
  if (!initialised_) {
    return;
  }
  ESP_LOGI(TAG, "Disconnecting WiFi.");
  esp_wifi_disconnect();
  esp_wifi_stop();
  connected_ = false;
}

void WifiManager::eventHandler(void* arg, const char* eventBase,
                               int32_t eventId, void* eventData) {
  auto* self = static_cast<WifiManager*>(arg);

  if (eventBase == WIFI_EVENT && eventId == WIFI_EVENT_STA_START) {
    // Driver is up - kick off the association.
    esp_wifi_connect();
    return;
  }

  if (eventBase == WIFI_EVENT && eventId == WIFI_EVENT_STA_DISCONNECTED) {
    self->connected_ = false;
    if (self->retryCount_ < self->maxRetries_) {
      ++self->retryCount_;
      ESP_LOGW(TAG, "Disconnected; retry %d/%d.", self->retryCount_,
               self->maxRetries_);
      esp_wifi_connect();
    } else {
      // Out of retries - unblock connect() with a failure.
      xEventGroupSetBits(self->events_, kFailedBit);
    }
    return;
  }

  if (eventBase == IP_EVENT && eventId == IP_EVENT_STA_GOT_IP) {
    auto* event = static_cast<ip_event_got_ip_t*>(eventData);
    ESP_LOGI(TAG, "Got IP: " IPSTR, IP2STR(&event->ip_info.ip));
    self->connected_  = true;
    self->retryCount_ = 0;
    xEventGroupSetBits(self->events_, kConnectedBit);
    return;
  }
}
