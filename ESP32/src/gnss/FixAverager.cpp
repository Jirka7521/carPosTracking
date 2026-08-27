#include "FixAverager.h"

#include "config/Config.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "FixAverager";

namespace {

// Bring a difference of two longitudes back into [-180, +180] degrees.
//
// Needed because a mean of raw longitudes is wrong on the ±180° meridian:
// averaging -179.9 and +179.9 - two points 22 km apart - gives 0.0, which is
// the Gulf of Guinea. Averaging the *differences* from a reference sample and
// wrapping each one avoids that entirely. The samples in a burst are seconds
// apart, so each loop below runs at most once.
double wrapDegrees(double degrees) {
  while (degrees > 180.0) {
    degrees -= 360.0;
  }
  while (degrees < -180.0) {
    degrees += 360.0;
  }
  return degrees;
}

// Do two readings carry the same UTC instant?
//
// The receiver solves at 1 Hz. If a burst read lands inside the same solution
// window as the previous one, the modem replays the identical position - and
// counting it again would silently weight one solution twice, defeating the
// point of averaging. Compared down to the millisecond because CGNSINF reports
// one, even though it is usually .000.
bool sameInstant(const GnssTime& a, const GnssTime& b) {
  return a.valid && b.valid && a.year == b.year && a.month == b.month &&
         a.day == b.day && a.hour == b.hour && a.minute == b.minute &&
         a.second == b.second && a.millisecond == b.millisecond;
}

}  // namespace

FixAverager::FixAverager(GnssModule& gnss) : gnss_(gnss) {}

bool FixAverager::acquire(GnssFix& out, uint32_t timeoutMs, uint32_t pollStepMs,
                          const std::function<void()>& onEachRead) {
  // The acquisition is untouched - same timeout, same poll step, same per-poll
  // hook. What comes back is reading #1 of four, and it is about to be thrown
  // away: it is the receiver's first solution after the lock and the least
  // settled one it will produce.
  if (!gnss_.waitForFix(out, timeoutMs, pollStepMs, onEachRead)) {
    return false;
  }

  // constexpr, so with averaging switched off in Config.h the burst below is
  // removed by the optimiser and this call is exactly the old waitForFix().
  if (!config::kFixAverageEnabled) {
    return true;
  }

  const uint8_t accepted = collect(out);

  if (accepted == 0) {
    // Nothing usable arrived - the lock lapsed the moment we got it, or every
    // read failed. Publishing the acquisition fix is better than publishing
    // nothing: it is a real position, just not an averaged one.
    ESP_LOGW(TAG,
             "No usable readings in the burst - publishing the acquisition fix "
             "unaveraged.");
  } else if (accepted < config::kFixAverageSampleCount) {
    ESP_LOGW(TAG, "Averaged %u of %u readings: %.6f, %.6f", (unsigned)accepted,
             (unsigned)config::kFixAverageSampleCount, out.position.latitudeDeg,
             out.position.longitudeDeg);
  } else {
    ESP_LOGI(TAG, "Averaged %u readings: %.6f, %.6f", (unsigned)accepted,
             out.position.latitudeDeg, out.position.longitudeDeg);
  }

  // A short burst never fails the cycle: we had a fix on entry and we still
  // have one now.
  return true;
}

uint8_t FixAverager::collect(GnssFix& out) {
  uint8_t accepted = 0;

  GnssFix sample;
  GnssFix last;  // the most recent accepted sample

  // Every position is accumulated as a *difference* from the first accepted
  // sample rather than as an absolute. That is what makes the meridian case
  // correct (see wrapDegrees), and it keeps the sums tiny, so no precision is
  // lost adding metre-scale differences to a 50-degree coordinate.
  double referenceLat = 0.0;
  double referenceLon = 0.0;
  double latDeltaSum  = 0.0;
  double lonDeltaSum  = 0.0;
  double altitudeSum  = 0.0;
  double speedSum     = 0.0;

  for (uint8_t i = 0; i < config::kFixAverageSampleCount; ++i) {
    // Wait first, then read: the reading that arrives immediately after the
    // acquisition is the very solution we are discarding.
    vTaskDelay(pdMS_TO_TICKS(config::kFixAverageStepMs));

    // Read with the NMEA satellite scan suppressed. That scan listens for
    // kSatelliteScanMs (3 s by default) on every debug read, which would space
    // these samples 4 s apart instead of 1 s and stretch the burst to a dozen
    // seconds. The per-fix debug dump still prints, so the log shows each
    // sample and then the average of them.
    if (!gnss_.readFix(sample, /*scanSatellites=*/false)) {
      ESP_LOGW(TAG, "Burst read %u/%u failed - skipping it.", (unsigned)(i + 1),
               (unsigned)config::kFixAverageSampleCount);
      continue;
    }

    // A reading without a position is skipped, not retried. Retrying would let
    // a bad sky stretch the cycle indefinitely; averaging two good samples
    // instead of three costs almost nothing.
    if (!sample.hasFix() || !sample.position.valid) {
      ESP_LOGW(TAG, "Burst read %u/%u has no fix - skipping it.",
               (unsigned)(i + 1), (unsigned)config::kFixAverageSampleCount);
      continue;
    }

    if (accepted > 0 && sameInstant(sample.time, last.time)) {
      ESP_LOGI(TAG, "Burst read %u/%u repeats the previous solution - skipping.",
               (unsigned)(i + 1), (unsigned)config::kFixAverageSampleCount);
      continue;
    }

    if (accepted == 0) {
      referenceLat = sample.position.latitudeDeg;
      referenceLon = sample.position.longitudeDeg;
    }
    latDeltaSum += wrapDegrees(sample.position.latitudeDeg - referenceLat);
    lonDeltaSum += wrapDegrees(sample.position.longitudeDeg - referenceLon);
    altitudeSum += sample.position.altitudeMeters;
    speedSum += sample.speedKmph;

    last = sample;
    ++accepted;
  }

  if (accepted == 0) {
    return 0;  // `out` keeps the acquisition fix.
  }

  // Start from the freshest sample, so everything we do NOT average - the UTC
  // timestamp, course, the DOP figures, satellite counts, engine/fix status -
  // comes from one consistent reading rather than being stitched together.
  //
  // The timestamp choice matters beyond tidiness: the API dedupes on
  // (device, fix time) at second precision, so the published fix has to carry a
  // real, advancing UTC. The last sample's is the freshest one available and
  // can never collide with the previous cycle's.
  //
  // Course is deliberately taken rather than averaged. It is a circular
  // quantity - the naive mean of 359° and 1° is 180°, the exact opposite of the
  // truth - and it is not published (TelemetryPublisher sends lat/lon/alt/speed
  // /time only). If it ever goes on the wire, average it as a unit vector:
  // atan2(Σsin, Σcos).
  out = last;

  const double n = static_cast<double>(accepted);
  out.position.latitudeDeg  = referenceLat + latDeltaSum / n;
  out.position.longitudeDeg = wrapDegrees(referenceLon + lonDeltaSum / n);
  out.position.altitudeMeters = altitudeSum / n;
  out.speedKmph               = speedSum / n;

  return accepted;
}
