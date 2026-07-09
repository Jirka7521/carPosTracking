#include "sdcard/SdCard.h"

#include <cstdio>
#include <cstring>
#include <string>

#include "driver/sdspi_host.h"
#include "esp_log.h"
#include "esp_vfs_fat.h"

static const char* TAG = "SdCard";

SdCard::SdCard(spi_host_device_t spiHost, int misoPin, int mosiPin, int sclkPin,
               int csPin, const char* mountPoint)
    : spiHost_(spiHost),
      misoPin_(misoPin),
      mosiPin_(mosiPin),
      sclkPin_(sclkPin),
      csPin_(csPin),
      mountPoint_(mountPoint),
      card_(nullptr),
      mounted_(false) {}

SdCard::~SdCard() {
  // Unmount the filesystem and free the SPI bus so the peripheral can be reused.
  if (mounted_) {
    esp_vfs_fat_sdcard_unmount(mountPoint_, card_);
    mounted_ = false;
  }
  if (card_ != nullptr) {
    spi_bus_free(spiHost_);
    card_ = nullptr;
  }
}

bool SdCard::begin() {
  if (mounted_) {
    return true;  // idempotent - already up
  }

  // 1. Bring up the SPI bus the card sits on.
  spi_bus_config_t busCfg = {};
  busCfg.mosi_io_num     = mosiPin_;
  busCfg.miso_io_num     = misoPin_;
  busCfg.sclk_io_num     = sclkPin_;
  busCfg.quadwp_io_num   = -1;  // not used in single-line SPI mode
  busCfg.quadhd_io_num   = -1;
  busCfg.max_transfer_sz = 4000;

  esp_err_t err = spi_bus_initialize(spiHost_, &busCfg, SDSPI_DEFAULT_DMA);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "SPI bus init failed: %s", esp_err_to_name(err));
    return false;
  }

  // 2. Describe the card as an SPI slave on that bus (which chip-select pin).
  sdspi_device_config_t slotCfg = SDSPI_DEVICE_CONFIG_DEFAULT();
  slotCfg.gpio_cs = static_cast<gpio_num_t>(csPin_);
  slotCfg.host_id = spiHost_;

  sdmmc_host_t host = SDSPI_HOST_DEFAULT();
  host.slot         = spiHost_;

  // 3. Mount FAT. `format_if_mount_failed` formats a blank/corrupt card once;
  //    an existing filesystem (and its queue file) is left intact across boots.
  esp_vfs_fat_mount_config_t mountCfg = {};
  mountCfg.format_if_mount_failed = true;
  mountCfg.max_files              = 5;
  mountCfg.allocation_unit_size   = 16 * 1024;

  err = esp_vfs_fat_sdspi_mount(mountPoint_, &host, &slotCfg, &mountCfg, &card_);
  if (err != ESP_OK) {
    ESP_LOGE(TAG, "mount failed: %s", esp_err_to_name(err));
    spi_bus_free(spiHost_);
    card_ = nullptr;
    return false;
  }

  mounted_ = true;
  // Capacity is (csize * sector-size); log it as MB so the card is easy to spot.
  const uint64_t capacityMb =
      ((uint64_t)card_->csd.capacity * card_->csd.sector_size) / (1024 * 1024);
  ESP_LOGI(TAG, "microSD mounted at %s (%s, %llu MB)", mountPoint_,
           card_->cid.name, (unsigned long long)capacityMb);
  return true;
}

bool SdCard::appendLine(const char* path, const std::string& line) {
  if (!mounted_) {
    return false;
  }
  std::FILE* f = std::fopen(path, "a");
  if (f == nullptr) {
    ESP_LOGE(TAG, "append: cannot open %s", path);
    return false;
  }
  const bool ok = std::fwrite(line.data(), 1, line.size(), f) == line.size() &&
                  std::fputc('\n', f) != EOF;
  std::fclose(f);
  if (!ok) {
    ESP_LOGE(TAG, "append: write to %s failed", path);
  }
  return ok;
}

bool SdCard::readLine(std::FILE* f, std::string& out) {
  out.clear();
  int c;
  bool sawAny = false;
  while ((c = std::fgetc(f)) != EOF) {
    sawAny = true;
    if (c == '\n') {
      break;
    }
    out.push_back(static_cast<char>(c));
  }
  // Strip a trailing '\r' so files written on other platforms behave.
  if (!out.empty() && out.back() == '\r') {
    out.pop_back();
  }
  return sawAny;  // false only when nothing at all was read (clean EOF)
}

bool SdCard::readLines(const char* path, std::size_t maxLines,
                       std::vector<std::string>& linesOut) const {
  linesOut.clear();
  if (!mounted_) {
    return false;
  }
  std::FILE* f = std::fopen(path, "r");
  if (f == nullptr) {
    return true;  // no file yet == empty queue, not an error
  }
  std::string line;
  while (readLine(f, line)) {
    if (line.empty()) {
      continue;  // skip blank lines defensively
    }
    linesOut.push_back(line);
    if (maxLines != 0 && linesOut.size() >= maxLines) {
      break;
    }
  }
  std::fclose(f);
  return true;
}

std::size_t SdCard::countLines(const char* path) const {
  if (!mounted_) {
    return 0;
  }
  std::FILE* f = std::fopen(path, "r");
  if (f == nullptr) {
    return 0;
  }
  std::size_t count = 0;
  std::string line;
  while (readLine(f, line)) {
    if (!line.empty()) {
      ++count;
    }
  }
  std::fclose(f);
  return count;
}

bool SdCard::dropFirstLines(const char* path, std::size_t n) {
  if (!mounted_) {
    return false;
  }
  if (n == 0) {
    return true;  // nothing to drop
  }

  std::FILE* in = std::fopen(path, "r");
  if (in == nullptr) {
    return true;  // no file == nothing queued
  }

  // Stream the survivors into a sibling temp file, then swap it in. Streaming
  // (rather than loading the whole file) keeps memory bounded for big backlogs.
  const std::string tmpPath = std::string(path) + ".tmp";
  std::FILE* out = std::fopen(tmpPath.c_str(), "w");
  if (out == nullptr) {
    ESP_LOGE(TAG, "drop: cannot open temp %s", tmpPath.c_str());
    std::fclose(in);
    return false;
  }

  std::size_t skipped   = 0;
  std::size_t survivors = 0;
  bool        ok        = true;
  std::string line;
  while (readLine(in, line)) {
    if (line.empty()) {
      continue;
    }
    if (skipped < n) {
      ++skipped;
      continue;  // drop this (already-delivered / trimmed) entry
    }
    if (std::fwrite(line.data(), 1, line.size(), out) != line.size() ||
        std::fputc('\n', out) == EOF) {
      ok = false;
      break;
    }
    ++survivors;
  }
  std::fclose(in);
  std::fclose(out);

  if (!ok) {
    ESP_LOGE(TAG, "drop: rewrite of %s failed", path);
    std::remove(tmpPath.c_str());
    return false;
  }

  // Replace the original with the trimmed copy (or remove both if nothing left).
  std::remove(path);
  if (survivors == 0) {
    std::remove(tmpPath.c_str());
    return true;
  }
  if (std::rename(tmpPath.c_str(), path) != 0) {
    ESP_LOGE(TAG, "drop: rename %s -> %s failed", tmpPath.c_str(), path);
    return false;
  }
  return true;
}

bool SdCard::removeFile(const char* path) {
  if (!mounted_) {
    return false;
  }
  std::remove(path);  // ignore "no such file" - the goal is that it is gone
  return true;
}
