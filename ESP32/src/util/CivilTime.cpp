#include "util/CivilTime.h"

#include <cstdio>

int64_t CivilTime::daysFromCivil(int64_t year, unsigned month, unsigned day) {
  year -= month <= 2;
  const int64_t  era = (year >= 0 ? year : year - 399) / 400;
  const unsigned yoe = static_cast<unsigned>(year - era * 400);
  const unsigned doy =
      (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
  const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097LL + static_cast<int64_t>(doe) - 719468;
}

void CivilTime::civilFromDays(int64_t days, int& yearOut, unsigned& monthOut,
                              unsigned& dayOut) {
  days += 719468;
  const int64_t  era = (days >= 0 ? days : days - 146096) / 146097;
  const unsigned doe = static_cast<unsigned>(days - era * 146097);
  const unsigned yoe =
      (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
  const int64_t  year = static_cast<int64_t>(yoe) + era * 400;
  const unsigned doy  = doe - (365 * yoe + yoe / 4 - yoe / 100);
  const unsigned mp   = (5 * doy + 2) / 153;
  dayOut              = doy - (153 * mp + 2) / 5 + 1;
  monthOut            = mp + (mp < 10 ? 3 : -9);
  yearOut             = static_cast<int>(year + (monthOut <= 2));
}

bool CivilTime::parseIso(const std::string& text, int64_t& epochOut) {
  int      year   = 0;
  unsigned month  = 0;
  unsigned day    = 0;
  unsigned hour   = 0;
  unsigned minute = 0;
  unsigned second = 0;
  if (text.size() != 20 ||
      std::sscanf(text.c_str(), "%4d-%2u-%2uT%2u:%2u:%2uZ", &year, &month,
                  &day, &hour, &minute, &second) != 6) {
    return false;
  }
  if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 ||
      minute > 59 || second > 59) {
    return false;
  }

  epochOut = daysFromCivil(year, month, day) * kSecondsPerDay +
             static_cast<int64_t>(hour) * kSecondsPerHour +
             static_cast<int64_t>(minute) * kSecondsPerMinute + second;
  return true;
}

std::string CivilTime::formatIso(int64_t epoch) {
  // Floor division, so a pre-epoch value still renders correctly rather than
  // truncating towards zero and landing a day late.
  const int64_t days      = (epoch - floorMod(epoch, kSecondsPerDay)) / kSecondsPerDay;
  const int64_t remainder = floorMod(epoch, kSecondsPerDay);

  int      year  = 0;
  unsigned month = 0;
  unsigned day   = 0;
  civilFromDays(days, year, month, day);

  char buffer[32];
  std::snprintf(buffer, sizeof(buffer), "%04d-%02u-%02uT%02u:%02u:%02uZ",
                year, month, day,
                static_cast<unsigned>(remainder / kSecondsPerHour),
                static_cast<unsigned>((remainder / kSecondsPerMinute) % 60),
                static_cast<unsigned>(remainder % kSecondsPerMinute));
  return std::string(buffer);
}

int CivilTime::weekdayFromEpoch(int64_t epoch) {
  // Floor division again: a pre-epoch instant must land on the day it belongs
  // to, not the one after.
  const int64_t days = (epoch - floorMod(epoch, kSecondsPerDay)) / kSecondsPerDay;
  return static_cast<int>(floorMod(days + 4, 7));
}

int64_t CivilTime::floorMod(int64_t value, int64_t modulus) {
  const int64_t remainder = value % modulus;
  return remainder < 0 ? remainder + modulus : remainder;
}
