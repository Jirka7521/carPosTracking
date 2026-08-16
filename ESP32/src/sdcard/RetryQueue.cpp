#include "sdcard/RetryQueue.h"

#include <cstdio>

#include "cJSON.h"
#include "esp_log.h"

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
      count_(0) {}

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

bool RetryQueue::readAll(std::vector<Entry>& entriesOut,
                         std::size_t& skipped) const {
  entriesOut.clear();
  skipped = 0;

  std::vector<std::string> lines;
  if (!card_.readLines(filePath_, /*maxLines=*/0, lines)) {
    return false;
  }

  entriesOut.reserve(lines.size());
  for (const std::string& line : lines) {
    Entry entry;
    if (decodeEntry(line, entry)) {
      entriesOut.push_back(entry);
    } else {
      // One corrupt line must not strand the rest of the backlog.
      skipped++;
    }
  }
  return true;
}

bool RetryQueue::writeAll(const std::vector<Entry>& entries) {
  if (entries.empty()) {
    count_ = 0;
    return card_.removeFile(filePath_);
  }

  std::string content;
  for (std::size_t i = 0; i < entries.size(); ++i) {
    const std::string line = encodeEntry(entries[i]);
    if (line.empty()) {
      continue;  // unencodable entry - dropping it beats corrupting the file
    }
    if (!content.empty()) {
      content.push_back('\n');
    }
    content.append(line);
  }

  if (!card_.writeFile(filePath_, content)) {
    return false;
  }
  count_ = entries.size();
  return true;
}

bool RetryQueue::add(const std::string& envelope, const std::string& nowUtc,
                     const char* reason, const std::string& firstUtc,
                     uint32_t priorAttempts) {
  int64_t nowEpoch = 0;
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

  // Trim the oldest when capped. Rewriting the whole file is acceptable here:
  // rejections are rare compared with the live path, so this is not a hot loop.
  if (maxEntries_ > 0 && count_ >= maxEntries_) {
    std::vector<Entry> entries;
    std::size_t        skipped = 0;
    if (readAll(entries, skipped)) {
      const std::size_t excess = entries.size() + 1 > maxEntries_
                                     ? entries.size() + 1 - maxEntries_
                                     : 0;
      if (excess > 0 && excess <= entries.size()) {
        ESP_LOGW(TAG, "retry file full - dropping %u oldest entr(ies)",
                 (unsigned)excess);
        entries.erase(entries.begin(), entries.begin() + excess);
      }
      entries.push_back(entry);
      return writeAll(entries);
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

  std::vector<Entry> entries;
  std::size_t        skipped = 0;
  if (!readAll(entries, skipped)) {
    return false;
  }
  if (skipped > 0) {
    ESP_LOGW(TAG, "skipped %u unreadable retry line(s)", (unsigned)skipped);
  }

  const int64_t maxAgeSeconds =
      static_cast<int64_t>(maxAgeHours_) * kSecondsPerHour;

  std::vector<Entry> remaining;
  remaining.reserve(entries.size());
  std::size_t abandoned = 0;

  for (Entry& entry : entries) {
    // Give up on anything that has been failing for longer than the cap. This is
    // the only path that discards data, so it is logged at error level.
    int64_t firstEpoch = 0;
    if (maxAgeHours_ > 0 && parseIso(entry.firstUtc, firstEpoch) &&
        nowEpoch - firstEpoch > maxAgeSeconds) {
      abandoned++;
      continue;
    }

    int64_t nextEpoch = 0;
    const bool due = parseIso(entry.nextUtc, nextEpoch) && nowEpoch >= nextEpoch;
    if (due && dueOut.size() < maxCount) {
      // Handed to the caller, so it leaves the card. If it is rejected again the
      // caller adds it back with a fresh schedule.
      entry.attempts++;
      dueOut.push_back(entry);
    } else {
      remaining.push_back(entry);
    }
  }

  if (abandoned > 0) {
    ESP_LOGE(TAG,
             "giving up on %u fix(es) the API kept rejecting for over %uh.",
             (unsigned)abandoned, (unsigned)maxAgeHours_);
  }

  // Only rewrite when something actually moved - the common cycle has nothing
  // due and should not touch the card at all.
  if (dueOut.empty() && abandoned == 0 && skipped == 0) {
    return true;
  }
  if (!writeAll(remaining)) {
    return false;
  }

  if (!dueOut.empty()) {
    ESP_LOGI(TAG, "retrying %u previously rejected fix(es).",
             (unsigned)dueOut.size());
  }
  return true;
}

bool RetryQueue::clear() {
  count_ = 0;
  return card_.removeFile(filePath_);
}
