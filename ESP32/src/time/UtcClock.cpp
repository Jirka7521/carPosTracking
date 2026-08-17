#include "time/UtcClock.h"

namespace {

constexpr int64_t kSecondsPerDay    = 86400;
constexpr int64_t kSecondsPerHour   = 3600;
constexpr int64_t kSecondsPerMinute = 60;

// Days since 1970-01-01 for a civil date (Howard Hinnant's algorithm). See the
// header for why this is hand-rolled rather than timegm()/mktime().
int64_t daysFromCivil(int64_t year, unsigned month, unsigned day) {
  year -= month <= 2;
  const int64_t  era = (year >= 0 ? year : year - 399) / 400;
  const unsigned yoe = static_cast<unsigned>(year - era * 400);
  const unsigned doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
  const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097LL + static_cast<int64_t>(doe) - 719468;
}

// Inverse of daysFromCivil().
void civilFromDays(int64_t days, int& yearOut, unsigned& monthOut,
                   unsigned& dayOut) {
  days += 719468;
  const int64_t  era = (days >= 0 ? days : days - 146096) / 146097;
  const unsigned doe = static_cast<unsigned>(days - era * 146097);
  const unsigned yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
  const int64_t  year = static_cast<int64_t>(yoe) + era * 400;
  const unsigned doy  = doe - (365 * yoe + yoe / 4 - yoe / 100);
  const unsigned mp   = (5 * doy + 2) / 153;
  dayOut              = doy - (153 * mp + 2) / 5 + 1;
  monthOut            = mp + (mp < 10 ? 3 : -9);
  yearOut             = static_cast<int>(year + (monthOut <= 2));
}

}  // namespace

int64_t UtcClock::toEpoch(const GnssTime& time) {
  const int64_t days = daysFromCivil(time.year, time.month, time.day);
  return days * kSecondsPerDay + time.hour * kSecondsPerHour +
         time.minute * kSecondsPerMinute + time.second;
}

void UtcClock::fromEpoch(int64_t epochSeconds, GnssTime& timeOut) {
  // Floor-divide, so a pre-epoch timestamp (only reachable from a corrupt fix)
  // still lands on the right day rather than truncating toward zero.
  int64_t days      = epochSeconds / kSecondsPerDay;
  int64_t remainder = epochSeconds % kSecondsPerDay;
  if (remainder < 0) {
    remainder += kSecondsPerDay;
    --days;
  }

  int      year  = 0;
  unsigned month = 0;
  unsigned day   = 0;
  civilFromDays(days, year, month, day);

  timeOut.year        = static_cast<uint16_t>(year);
  timeOut.month       = static_cast<uint8_t>(month);
  timeOut.day         = static_cast<uint8_t>(day);
  timeOut.hour        = static_cast<uint8_t>(remainder / kSecondsPerHour);
  timeOut.minute      =
      static_cast<uint8_t>((remainder % kSecondsPerHour) / kSecondsPerMinute);
  timeOut.second      = static_cast<uint8_t>(remainder % kSecondsPerMinute);
  timeOut.millisecond = 0;
  timeOut.valid       = true;
}
