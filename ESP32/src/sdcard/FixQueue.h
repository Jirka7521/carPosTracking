#pragma once

// =============================================================================
//  FixQueue  -  A persistent FIFO of encrypted fix envelopes on the microSD.
// -----------------------------------------------------------------------------
//  Responsibility (single!): model an append-only queue of *already-encrypted*
//  payload envelopes, backed by one line-delimited file on the card. It knows
//  what a queued entry means (one sealed fix) and enforces the size cap; it
//  delegates all raw file IO to SdCard.
//
//  Flow it supports (see FixForwarder):
//      enqueue()   - a fix that could not be delivered is stored (one line).
//      peekBatch() - read the oldest N envelopes to build a burst.
//      popFront()  - drop those N once the broker has acked the burst.
//
//  The live count is cached in memory (seeded once by begin()) so the common
//  outage path never rescans the whole file just to know how big it is.
// =============================================================================

#include <cstddef>
#include <string>
#include <vector>

#include "sdcard/SdCard.h"

class FixQueue {
 public:
  // Borrows `card` and `filePath` (both must outlive this object). `maxEntries`
  // caps the queue: once reached, the oldest entries are dropped to make room
  // (0 means "no cap").
  FixQueue(SdCard& card, const char* filePath, std::size_t maxEntries);

  // Seed the cached count from whatever is already on the card. Call once after
  // the card is mounted. Returns false if the card is not usable.
  bool begin();

  bool        isEmpty() const { return count_ == 0; }
  std::size_t size() const { return count_; }

  // Append one sealed envelope. If the queue is at its cap the oldest entries
  // are trimmed first, so a long outage can never overflow the card. Returns
  // false on IO error.
  bool enqueue(const std::string& envelope);

  // Copy the oldest up-to-`maxCount` envelopes into `out` (does not remove
  // them). Returns false on IO error.
  bool peekBatch(std::size_t maxCount, std::vector<std::string>& out) const;

  // Remove the oldest `count` envelopes (call after they have been delivered
  // and acked). Returns false on IO error.
  bool popFront(std::size_t count);

  // Drop the whole queue.
  bool clear();

 private:
  SdCard&     card_;
  const char* filePath_;
  std::size_t maxEntries_;
  std::size_t count_;  // cached number of queued envelopes
};
