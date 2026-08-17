#include "sdcard/RetryQueue.h"

#include <cstdio>

#include "cJSON.h"
#include "esp_log.h"
#include "util/ScopedLock.h"

static const char* TAG = "RetryQueue";

namespace {

  constexpr int64_t kSecondsPerHour = 3600;

  // Days since 1970-01-01 for a civil date (Howard Hinnant's algorithm).
  //
  // Hand-rolled rather than using timegm(): that function's availability varies
  // across newlib configurations, and mktime() would drag in the local timezone,
  // which on a device with no zone data is a trap. This is branch-free, exact for
  // every date we will ever see, and has no libc dependency at all.
  int64_t daysFromCivil(int64_t year, unsigned month, unsigned day) {
    year -= month <= 2;
    const int64_t  era = (year >= 0 ? year : year - 399) / 400;
    const unsigned yoe = static_cast<unsigned>(year - era * 400);
    const unsigned doy =
        (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
    const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097LL + static_cast<int64_t>(doe) - 719468;
  }

  // Inverse of daysFromCivil.
  void civilFromDays(int64_t days, int& yearOut, unsigned& monthOut,
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

  // Parse "YYYY-MM-DDTHH:MM:SSZ" into seconds since the Unix epoch. Returns
  // false for anything that is not exactly that shape - the format is produced
  // by us and by the API, so a deviation is corruption, not a variant.
  bool parseIso(const std::string& text, int64_t& epochOut) {
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

    epochOut = daysFromCivil(year, month, day) * 86400LL +
               static_cast<int64_t>(hour) * 3600 +
               static_cast<int64_t>(minute) * 60 + second;
    return true;
  }

  // Render seconds since the Unix epoch back to "YYYY-MM-DDTHH:MM:SSZ".
  std::string formatIso(int64_t epoch) {
    int64_t days      = epoch / 86400;
    int64_t remainder = epoch % 86400;
    if (remainder < 0) {  // floor division, so a pre-epoch value still works
      remainder += 86400;
      days -= 1;
    }

    int      year  = 0;
    unsigned month = 0;
    unsigned day   = 0;
    civilFromDays(days, year, month, day);

    char buffer[32];
    std::snprintf(buffer, sizeof(buffer), "%04d-%02u-%02uT%02u:%02u:%02uZ",
                  year, month, day,
                  static_cast<unsigned>(remainder / 3600),
                  static_cast<unsigned>((remainder / 60) % 60),
                  static_cast<unsigned>(remainder % 60));
    return std::string(buffer);
  }

}  // namespace

RetryQueue::RetryQueue(SdCard& card, const char* filePath,
                       std::size_t maxEntries, uint32_t retryIntervalHours,
                       uint32_t maxAgeHours)
    : card_(card),
      filePath_(filePath),
      maxEntries_(maxEntries),
      retryIntervalHours_(retryIntervalHours),
      maxAgeHours_(maxAgeHours),
      count_(0),
      lock_(xSemaphoreCreateMutex()) {}

bool RetryQueue::begin() {
  if (!card_.isMounted()) {
    return false;
  }
  count_ = card_.countLines(filePath_);
  if (count_ > 0) {
    ESP_LOGI(TAG, "%u rejected fix(es) awaiting retry.", (unsigned)count_);
  }
  return true;
}

std::string RetryQueue::encodeEntry(const Entry& entry) {
  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    return std::string();
  }

  std::string line;

  // The envelope is nested as a real object rather than an escaped string, so
  // the file stays readable when inspecting the card by hand.
  cJSON* envelope = cJSON_Parse(entry.envelope.c_str());
  if (envelope != nullptr) {
    cJSON_AddItemToObject(root, "env", envelope);  // root takes ownership
    cJSON_AddNumberToObject(root, "attempts",
                            static_cast<double>(entry.attempts));
    cJSON_AddStringToObject(root, "first", entry.firstUtc.c_str());
    cJSON_AddStringToObject(root, "next", entry.nextUtc.c_str());

    char* printed = cJSON_PrintUnformatted(root);
    if (printed != nullptr) {
      line.assign(printed);
      cJSON_free(printed);
    }
  }

  cJSON_Delete(root);
  return line;
}

bool RetryQueue::decodeEntry(const std::string& line, Entry& entryOut) {
  cJSON* root = cJSON_Parse(line.c_str());
  if (root == nullptr) {
    return false;
  }

  bool ok = false;
  do {
    const cJSON* envelope = cJSON_GetObjectItemCaseSensitive(root, "env");
    const cJSON* attempts = cJSON_GetObjectItemCaseSensitive(root, "attempts");
    const cJSON* first    = cJSON_GetObjectItemCaseSensitive(root, "first");
    const cJSON* next     = cJSON_GetObjectItemCaseSensitive(root, "next");
    if (envelope == nullptr || !cJSON_IsString(first) || !cJSON_IsString(next)) {
      break;
    }

    char* printed = cJSON_PrintUnformatted(envelope);
    if (printed == nullptr) {
      break;
    }
    entryOut.envelope.assign(printed);
    cJSON_free(printed);

    entryOut.firstUtc = first->valuestring;
    entryOut.nextUtc  = next->valuestring;
    entryOut.attempts =
        cJSON_IsNumber(attempts) ? static_cast<uint32_t>(attempts->valuedouble)
                                 : 0;
    ok = true;
  } while (false);

  cJSON_Delete(root);
  return ok;
}

RetryQueue::Disposition RetryQueue::classify(const std::string& line,
                                             int64_t     nowEpoch,
                                             std::size_t maxCount,
                                             std::size_t& takenSoFar,
                                             Entry&       entryOut) const {
  Entry entry;
  if (!decodeEntry(line, entry)) {
    // Not necessarily corrupt - see the header. Left where it is.
    return Disposition::Undecodable;
  }

  // Give up on anything that has been failing for longer than the cap. This is
  // the only path that discards data, so the caller logs it at error level.
  int64_t firstEpoch = 0;
  if (maxAgeHours_ > 0 && parseIso(entry.firstUtc, firstEpoch) &&
      nowEpoch - firstEpoch > static_cast<int64_t>(maxAgeHours_) * kSecondsPerHour) {
    return Disposition::Abandon;
  }

  int64_t nextEpoch = 0;
  const bool due = parseIso(entry.nextUtc, nextEpoch) && nowEpoch >= nextEpoch;
  if (!due || takenSoFar >= maxCount) {
    return Disposition::Keep;
  }

  // Handed to the caller, so it leaves the card. If it is rejected again the
  // caller adds it back with a fresh schedule.
  entry.attempts++;
  entryOut = entry;
  takenSoFar++;
  return Disposition::Take;
}

bool RetryQueue::add(const std::string& envelope, const std::string& nowUtc,
                     const char* reason, const std::string& firstUtc,
                     uint32_t priorAttempts) {
  ScopedLock guard(lock_);
  int64_t    nowEpoch = 0;
  if (!parseIso(nowUtc, nowEpoch)) {
    // No usable clock: we cannot say when to try again, and guessing would
    // either hammer the API or abandon the fix. Better to report the failure and
    // let the caller leave it in the live queue.
    ESP_LOGW(TAG, "cannot schedule a retry without a GNSS time - keeping the fix queued");
    return false;
  }

  Entry entry;
  entry.envelope = envelope;
  // Keep the original rejection time when this is a repeat, so maxAgeHours_
  // measures the whole ordeal instead of restarting on every attempt.
  entry.firstUtc = firstUtc.empty() ? nowUtc : firstUtc;
  entry.nextUtc =
      formatIso(nowEpoch + static_cast<int64_t>(retryIntervalHours_) * kSecondsPerHour);
  entry.attempts = priorAttempts + 1;

  const std::string line = encodeEntry(entry);
  if (line.empty()) {
    ESP_LOGE(TAG, "could not encode a rejected fix");
    return false;
  }

  // Trim the oldest when capped, then append - the same streaming shape as
  // FixQueue::enqueue(). Reading the file in to rewrite it, as this used to do,
  // meant the *fullest* the queue is ever allowed to get was also the moment it
  // demanded the most heap: a guaranteed abort at the cap rather than a trim.
  if (maxEntries_ > 0 && count_ >= maxEntries_) {
    const std::size_t toDrop = count_ - maxEntries_ + 1;
    if (card_.dropFirstLines(filePath_, toDrop)) {
      count_ -= toDrop;
      ESP_LOGW(TAG, "retry file full (%u) - dropped %u oldest entr(ies)",
               (unsigned)maxEntries_, (unsigned)toDrop);
    }
  }

  if (!card_.appendLine(filePath_, line)) {
    ESP_LOGE(TAG, "could not store a rejected fix");
    return false;
  }
  count_++;
  ESP_LOGW(TAG,
           "API rejected a fix (%s) - stored for retry in %uh (%u waiting).",
           reason == nullptr ? "unspecified" : reason,
           (unsigned)retryIntervalHours_, (unsigned)count_);
  return true;
}

bool RetryQueue::takeDue(const std::string& nowUtc, std::size_t maxCount,
                         std::vector<Entry>& dueOut) {
  ScopedLock guard(lock_);
  dueOut.clear();
  if (count_ == 0) {
    return true;
  }

  int64_t nowEpoch = 0;
  if (!parseIso(nowUtc, nowEpoch)) {
    // No clock this cycle: nothing is due. Entries simply wait, which is the
    // safe direction to fail.
    return true;
  }

  // -- Pass 1: decide, without touching the card. -----------------------------
  // A read-only stream: one line resident at a time, and at most `maxCount`
  // entries collected. Nothing here grows with the size of the file.
  std::size_t taken       = 0;
  std::size_t abandoned   = 0;
  std::size_t undecodable = 0;
  dueOut.reserve(maxCount);

  const bool walked = card_.forEachLine(
      filePath_, [&](const std::string& line) {
        Entry entry;
        switch (classify(line, nowEpoch, maxCount, taken, entry)) {
          case Disposition::Take:
            dueOut.push_back(entry);
            break;
          case Disposition::Abandon:
            abandoned++;
            break;
          case Disposition::Undecodable:
            undecodable++;
            break;
          case Disposition::Keep:
            break;
        }
      });
  if (!walked) {
    dueOut.clear();
    return false;
  }

  if (undecodable > 0) {
    // Kept on the card deliberately - this is a warning, not a deletion.
    ESP_LOGW(TAG, "%u unreadable retry line(s) left in place",
             (unsigned)undecodable);
  }
  if (abandoned > 0) {
    ESP_LOGE(TAG,
             "giving up on %u fix(es) the API kept rejecting for over %uh.",
             (unsigned)abandoned, (unsigned)maxAgeHours_);
  }

  // Only rewrite when something actually moved - the common cycle has nothing
  // due and should not touch the card at all.
  if (dueOut.empty() && abandoned == 0) {
    return true;
  }

  // -- Pass 2: rewrite, dropping exactly what pass 1 claimed. -----------------
  // The same classifier over the same file with the same clock and a fresh
  // counter reaches the same verdicts line for line, so nothing has to be
  // remembered between the passes. Nothing else writes this file while we are in
  // here - the whole flow is single-threaded through FixForwarder.
  std::size_t retaken   = 0;
  std::size_t survivors = 0;
  const bool  rewritten = card_.rewriteLines(
      filePath_,
      [&](const std::string& line) {
        Entry entry;
        const Disposition verdict =
            classify(line, nowEpoch, maxCount, retaken, entry);
        // Undecodable lines survive; only what was claimed or aged out goes.
        return verdict == Disposition::Keep ||
               verdict == Disposition::Undecodable;
      },
      survivors);
  if (!rewritten) {
    // The entries are still on the card (rewriteLines leaves the original in
    // place on failure), so we must not also hand them to the caller - that
    // would publish them twice.
    dueOut.clear();
    return false;
  }
  count_ = survivors;

  if (!dueOut.empty()) {
    ESP_LOGI(TAG, "retrying %u previously rejected fix(es).",
             (unsigned)dueOut.size());
  }
  return true;
}

bool RetryQueue::clear() {
  ScopedLock guard(lock_);
  count_ = 0;
  return card_.removeFile(filePath_);
}
