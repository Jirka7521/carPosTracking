#include "GnssModule.h"

#include <cstdio>
#include <cstring>

#include "config/Config.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "gnss/CgnsinfParser.h"

static const char* TAG = "GnssModule";

GnssModule::GnssModule(Sim7000Modem& modem) : modem_(modem) {}

bool GnssModule::begin() {
  if (!modem_.begin()) {
    ESP_LOGE(TAG, "Failed to initialise modem transport.");
    return false;
  }
  if (!powerOnModule()) {
    return false;
  }
  // Select the constellations requested in Config.h *before* powering the
  // engine, then enable the GNSS engine.
  setConstellations(config::kEnableGps, config::kEnableGlonass,
                    config::kEnableBeidou, config::kEnableGalileo);
  return enableGnss();
}

// -----------------------------------------------------------------------------
//  Power management
// -----------------------------------------------------------------------------
bool GnssModule::powerOnModule() {
  return modem_.powerOn();
}

bool GnssModule::powerOffModule() {
  // CGNSPWR is cleared automatically when the modem powers down, but we turn
  // the engine off first so the state is unambiguous.
  disableGnss();
  return modem_.powerOff();
}

bool GnssModule::enableGnss() {
  // The active GNSS antenna's amplifier is powered through the modem's GPIO4 on
  // the T-SIM7000G. Without this the receiver can list satellites but the signal
  // is too weak to ever lock a fix, so power it before starting the engine.
  ESP_LOGI(TAG, "Powering GNSS antenna (modem GPIO4).");
  modem_.sendCommand("AT+SGPIO=0,4,1,1", 1000);

  ESP_LOGI(TAG, "Enabling GNSS engine.");
  return modem_.sendCommand("AT+CGNSPWR=1", 2000);
}

bool GnssModule::disableGnss() {
  ESP_LOGI(TAG, "Disabling GNSS engine.");
  return modem_.sendCommand("AT+CGNSPWR=0", 2000);
}

bool GnssModule::setConstellations(bool gps, bool glonass, bool beidou,
                                   bool galileo) {
  // GPS must always be enabled - the modem cannot operate without it.
  (void)gps;
  char cmd[32];
  snprintf(cmd, sizeof(cmd), "AT+CGNSMOD=1,%d,%d,%d", glonass ? 1 : 0,
           beidou ? 1 : 0, galileo ? 1 : 0);
  ESP_LOGI(TAG, "Setting constellations: %s", cmd);
  return modem_.sendCommand(cmd, 2000);
}

// -----------------------------------------------------------------------------
//  Reading the position
// -----------------------------------------------------------------------------
bool GnssModule::readFix(GnssFix& fix) {
  char response[256];
  if (!modem_.sendCommand("AT+CGNSINF", response, sizeof(response), 2000)) {
    ESP_LOGW(TAG, "No reply to AT+CGNSINF.");
    return false;
  }

  // Locate the data line within the (possibly multi-line) response.
  char* p = strstr(response, "+CGNSINF:");
  if (p == nullptr) {
    ESP_LOGW(TAG, "CGNSINF data line not found.");
    return false;
  }
  p += strlen("+CGNSINF:");
  while (*p == ' ') {
    ++p;  // Skip the space after the colon.
  }

  if (!CgnsinfParser::parse(p, fix)) {
    ESP_LOGW(TAG, "Failed to parse CGNSINF payload.");
    return false;
  }

  // Debug reporting (compiled out entirely when kGnssDebug is false).
  if (config::kGnssDebug) {
    debugPrintFix(fix);

    GnssSatelliteCounts counts;
    if (readSatelliteCounts(counts, config::kSatelliteScanMs)) {
      debugPrintSatellites(counts);
    }
  }

  return true;
}

bool GnssModule::readSatelliteCounts(GnssSatelliteCounts& counts,
                                     uint32_t scanMs) {
  nmea_.reset();

  // Ask the modem to stream raw NMEA sentences to the UART.
  if (!modem_.sendCommand("AT+CGNSTST=1", 2000)) {
    ESP_LOGW(TAG, "Could not start NMEA streaming.");
    return false;
  }

  // Listen for `scanMs`, feeding every line to the NMEA parser.
  char       line[128];
  TickType_t start = xTaskGetTickCount();
  while ((xTaskGetTickCount() - start) < pdMS_TO_TICKS(scanMs)) {
    int len = modem_.serial().readLine(line, sizeof(line), 200);
    if (len > 0) {
      nmea_.feedLine(line);
    }
  }

  // Stop streaming so normal AT command/response resumes cleanly.
  modem_.sendCommand("AT+CGNSTST=0", 2000);
  modem_.serial().flushInput();

  counts = nmea_.counts();
  return counts.valid;
}

// -----------------------------------------------------------------------------
//  Debug printing  (only reached when config::kGnssDebug == true)
// -----------------------------------------------------------------------------
void GnssModule::debugPrintFix(const GnssFix& fix) {
  printf("\n");
  printf("================ GNSS FIX ================\n");
  printf("  Engine running : %s\n", fix.engineRunning ? "yes" : "no");
  printf("  Fix status     : %s\n", fix.hasFix() ? "FIX" : "no fix");

  if (fix.time.valid) {
    printf("  UTC time       : %04u-%02u-%02u %02u:%02u:%02u.%03u\n",
           fix.time.year, fix.time.month, fix.time.day, fix.time.hour,
           fix.time.minute, fix.time.second, fix.time.millisecond);
  } else {
    printf("  UTC time       : (none)\n");
  }

  if (fix.position.valid) {
    printf("  Latitude       : %.6f deg\n", fix.position.latitudeDeg);
    printf("  Longitude      : %.6f deg\n", fix.position.longitudeDeg);
    printf("  Altitude       : %.1f m\n", fix.position.altitudeMeters);
  } else {
    printf("  Position       : (no fix yet)\n");
  }

  printf("  Speed          : %.2f km/h\n", fix.speedKmph);
  printf("  Course         : %.1f deg\n", fix.courseDeg);
  printf("  Sats in view   : %u\n", fix.satellitesInView);
  printf("  Sats used      : %u\n", fix.satellitesUsed);
  printf("  HDOP/PDOP/VDOP : %.1f / %.1f / %.1f\n", fix.hdop, fix.pdop,
         fix.vdop);
  printf("=========================================\n");
}

void GnssModule::debugPrintSatellites(const GnssSatelliteCounts& counts) {
  printf("----------- SATELLITES IN VIEW ----------\n");
  printf("  GPS     (USA)    : %u\n", counts.gps);
  printf("  GLONASS (Russia) : %u\n", counts.glonass);
  printf("  BeiDou  (China)  : %u\n", counts.beidou);
  printf("  Galileo (Europe) : %u\n", counts.galileo);
  printf("  TOTAL            : %u\n", counts.total());
  printf("-----------------------------------------\n\n");
}
