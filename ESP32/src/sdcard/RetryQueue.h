#pragma once

// =============================================================================
//  RetryQueue  -  Hold fixes the API rejected, and re-offer them on a schedule.
// -----------------------------------------------------------------------------
//  Responsibility (single!): keep the envelopes the API explicitly refused, each
//  with when to try it again and how many times we already have, in one
//  line-delimited file on the card. It answers "what is due now?" and "what has
//  been hopeless for long enough to give up on?". Raw file IO goes to SdCard.
//
//  Why not just drop a rejected fix, or keep retrying it forever?
//    Neither is right. Several reject reasons are SERVER-side and fixable -
//    UnknownDevice and StorageRejected both clear the moment the device row is
//    provisioned or reactivated, and DecryptFailed clears when the key is fixed.
//    Discarding those loses good data for a transient server problem. But
//    retrying every few seconds forever would wedge the live queue behind a
//    permanently poisonous fix, so entries wait out a long interval between
//    attempts and are eventually abandoned.
//
//  Distinct from FixQueue on purpose. FixQueue is a plain FIFO drained as fast
//  as the link allows; this is a scheduled set, walked by due time, and a
//  rejected fix must not sit at the head of the live queue blocking fresh ones.
//
//  Line format (one JSON object per entry, envelope stored verbatim inside):
//    {"env":{...encrypted envelope...},"attempts":2,
//     "first":"2026-08-16T09:12:44Z","next":"2026-08-17T09:12:44Z"}
//
//  Memory: every path here STREAMS the file through SdCard - one line resident
//  at a time - and never holds more than one burst (`maxCount`) of entries. This
//  is not a micro-optimisation. An envelope is several hundred bytes, the file
//  may hold thousands, and the internal heap is well under 200 KB once WiFi and
//  mbedTLS are up; an earlier version that read and rewrote the file as one
//  std::string aborted the whole device on a failed allocation, and did it right
//  after a long backlog drain had fragmented the heap - exactly when the retry
//  queue is most likely to have work.
//
//  Undecodable lines are KEPT, counted and logged - never dropped. cJSON cannot
//  distinguish "malformed" from "could not allocate", so a line that fails to
//  parse under memory pressure is very probably a perfectly good fix. Discarding
//  it would be silent data loss, and a stale line costs a few hundred bytes on a
//  card with gigabytes. The file cap in add() is what eventually clears them.
//
//  Clock: the GNSS UTC time of the current fix, passed in by the caller. That is
//  the only trustworthy wall clock on this device - esp_timer restarts across
//  the deep-sleep reboot, and there is no RTC battery. When no GNSS time is
//  available the caller passes an empty string and nothing is treated as due,
//  which is the safe direction to fail: entries wait rather than being
//  abandoned or hammered.
// =============================================================================

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "sdcard/SdCard.h"

class RetryQueue {
 public:
  // One entry as read back from the card.
  struct Entry {
    std::string envelope;   // the original sealed envelope, verbatim
    std::string firstUtc;   // when it was first rejected (ISO-8601 Z)
    std::string nextUtc;    // earliest time to try it again (ISO-8601 Z)
    uint32_t    attempts;   // how many times it has been offered so far
  };

  // Borrows `card` and `filePath` (both must outlive this object).
  //   maxEntries     : cap; oldest are dropped once reached (0 means no cap)
  //   retryIntervalH : hours to wait between attempts
  //   maxAgeH        : give up on an entry older than this many hours
  RetryQueue(SdCard& card, const char* filePath, std::size_t maxEntries,
             uint32_t retryIntervalHours, uint32_t maxAgeHours);

  // Seed the cached count from the card. Call once after the card is mounted.
  // Returns false if the card is not usable.
  bool begin();

  bool        isEmpty() const { return count_ == 0; }
  std::size_t size() const { return count_; }

  // Retune the schedule at runtime (both are remote settings - see
  // SettingsApplier). Plain setters, because add() and takeDue() re-read these
  // members on every call and nothing on the card needs rewriting:
  //   * the interval is applied when an entry is (re-)scheduled, so entries
  //     already waiting keep the "next" time they were given and pick the new
  //     pacing up on their following attempt;
  //   * the max age is measured live from each entry's "first" timestamp, so a
  //     shortened one takes effect on the very next walk.
  // The asymmetry is deliberate: shortening the give-up age is how an operator
  // stops a backlog they have decided is worthless, and that should not have to
  // wait a day.
  void setRetryIntervalHours(uint32_t hours) { retryIntervalHours_ = hours; }
  void setMaxAgeHours(uint32_t hours) { maxAgeHours_ = hours; }

  // Record a rejected envelope, scheduling its next attempt one interval after
  // `nowUtc`. `reason` is logged, not stored - it is the API's verdict for this
  // attempt and may well differ on the next one. Returns false on IO error or
  // when `nowUtc` is not a usable timestamp (no clock, so no schedule can be
  // computed) - in which case the caller should keep the fix where it is.
  //
  // `firstUtc` and `priorAttempts` carry an entry's history when it is being
  // re-added after a retry also failed. Leaving them at their defaults marks a
  // first rejection. Preserving `firstUtc` is what makes the give-up age measure
  // the whole ordeal rather than resetting on every attempt.
  bool add(const std::string& envelope, const std::string& nowUtc,
           const char* reason, const std::string& firstUtc = std::string(),
           uint32_t priorAttempts = 0);

  // Collect up to `maxCount` entries whose next-attempt time has passed,
  // rewriting the file with those entries removed and any that have aged out
  // dropped. Entries handed back are no longer on the card: the caller owns
  // them, and must add() them again if they are rejected once more.
  //
  // Returns false on IO error. An empty `dueOut` is the normal case and not a
  // failure. With an empty `nowUtc` nothing is ever due.
  bool takeDue(const std::string& nowUtc, std::size_t maxCount,
               std::vector<Entry>& dueOut);

  // Drop the whole retry file.
  bool clear();

 private:
  // What a single walk of the file decides about one of its lines.
  enum class Disposition {
    Keep,        // not due yet - leave it on the card untouched
    Take,        // due, and there is room in this burst - hand it to the caller
    Abandon,     // past the give-up age - the one path that discards data
    Undecodable  // could not be parsed - kept anyway, see the header note
  };

  // Decide what happens to one raw line. `takenSoFar` counts the entries already
  // claimed by this walk and is incremented on Take; passing the same file, the
  // same `nowEpoch` and a fresh counter therefore reproduces the same verdicts,
  // which is what lets takeDue() decide in one pass and rewrite in a second
  // without remembering anything in between. `entryOut` is only filled in for
  // Take.
  Disposition classify(const std::string& line, int64_t nowEpoch,
                       std::size_t maxCount, std::size_t& takenSoFar,
                       Entry& entryOut) const;

  // Serialise one entry to its JSON line.
  static std::string encodeEntry(const Entry& entry);

  // Parse one JSON line. Returns false when it is not a usable entry.
  static bool decodeEntry(const std::string& line, Entry& entryOut);

  SdCard&     card_;
  const char* filePath_;
  std::size_t maxEntries_;
  uint32_t    retryIntervalHours_;
  uint32_t    maxAgeHours_;
  std::size_t count_;  // cached number of stored entries
};
