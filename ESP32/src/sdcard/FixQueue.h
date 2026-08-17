#pragma once

// =============================================================================
//  FixQueue  -  A persistent FIFO of encrypted fix envelopes on the microSD.
// -----------------------------------------------------------------------------
//  Responsibility (single!): model an append-only queue of *already-encrypted*
//  payload envelopes, backed by one line-delimited file on the card. It knows
//  what a queued entry means (one sealed fix) and enforces the size cap; it
//  delegates all raw file IO to SdCard and its position bookkeeping to
//  QueueIndex.
//
//  Flow it supports (see FixForwarder):
//      enqueue()   - a fix that could not be delivered is stored (one line).
//      peekBatch() - read the oldest N envelopes to build a burst.
//      popFront()  - drop those N once the broker has acked the burst.
//
//  Storage model - why the head is an offset and not the start of the file:
//
//      queue.jsonl      [xxxx consumed xxxx][ live entries .............. ]
//                                           ^ head_
//      queue.jsonl.idx  head=12800000 count=421337
//
//  The data file is strictly append-only; popping moves `head_` forward and
//  rewrites nothing. The obvious alternative - rewriting the file to drop its
//  first N lines - costs O(file size) *per pop*, which on the backlog this
//  queue is now sized for (a week of 1 Hz debug samples, ~484 MB) never
//  finishes. Reading a burst is O(burst); popping it is O(burst).
//
//  The dead prefix is reclaimed in two ways:
//    * the queue draining completely deletes the file outright and resets the
//      head to 0. This is the normal end of an outage and costs nothing.
//    * otherwise, compaction runs only once the dead prefix is both larger than
//      kCompactMinDeadBytes *and* more than half the file. Halving-based
//      triggers make the total copying across a whole drain O(n) overall rather
//      than O(n) per pop, so the worst case stays bounded without paying on the
//      common path.
//
//  The live count is cached in memory (seeded by begin() from the sidecar) so
//  the common outage path never rescans the file just to know how big it is.
//
//  Thread safety: every public method that touches the card is serialised. Two
//  tasks reach this queue - the main loop (via FixForwarder, and via
//  SettingsApplier changing the cap) and AccelDebugStream (via FixForwarder) -
//  and an interleaved pop and enqueue would corrupt both the file and the cached
//  position. Two deliberate exceptions:
//    * begin() is not locked - it runs once at start-up, before any other task
//      exists.
//    * size()/isEmpty() are not locked - they are single-word reads whose answer
//      is advisory anyway ("is there a backlog worth draining?"), and locking
//      them would cost a context switch on a call FixForwarder makes constantly.
// =============================================================================

#include <cstddef>
#include <string>
#include <vector>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "sdcard/QueueIndex.h"
#include "sdcard/SdCard.h"

class FixQueue {
 public:
  // Borrows `card` and `filePath` (both must outlive this object). `maxEntries`
  // caps the queue: once reached, the oldest entries are dropped to make room
  // (0 means "no cap"). The sidecar index lives beside `filePath`.
  FixQueue(SdCard& card, const char* filePath, std::size_t maxEntries);

  // Recover the head offset and live count from the sidecar. Call once after
  // the card is mounted. Returns false if the card is not usable.
  //
  // With no usable sidecar this falls back to head 0 plus a full line count -
  // slow, but only on that path, and safe: see QueueIndex::load().
  bool begin();

  // Advisory and unlocked - see the thread-safety note in the banner.
  bool        isEmpty() const { return count_ == 0; }
  std::size_t size() const { return count_; }

  // Change the cap at runtime (it is a remote setting - see SettingsApplier).
  // A lower cap takes effect immediately: the excess oldest entries are trimmed
  // right away rather than lingering until the next enqueue, so shrinking the
  // queue from the dashboard actually frees the card. Returns false if that trim
  // hit an IO error; the new cap is adopted either way.
  bool setMaxEntries(std::size_t maxEntries);

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
  // The private helpers below assume the caller already holds `lock_` - they are
  // only ever reached from the locked public methods, and the mutex is not
  // recursive, so they must never take it themselves.

  // Move `head_` forward past `n` live entries and persist the new position.
  // Costs one batch's worth of reading - no rewrite. `count_` is reduced by
  // what was actually found, so a file truncated behind our back self-corrects
  // instead of leaving the count permanently ahead of reality.
  bool advanceHead(std::size_t n);

  // Reclaim the dead prefix, but only when it has grown enough to be worth the
  // copy. See the storage-model note in the banner for why this rule and not a
  // simpler one.
  bool compactIfNeeded();

  // Delete the data file and the sidecar and start over at offset 0. Used when
  // the queue empties - the cheapest possible compaction.
  bool reset();

  SdCard&     card_;
  const char* filePath_;
  // Owned, because QueueIndex only borrows the path it is given and the config
  // supplies no name for the sidecar. Declared before index_ so it is built
  // first - member initialisation follows declaration order.
  std::string indexPath_;
  QueueIndex  index_;
  std::size_t maxEntries_;
  std::size_t count_;  // cached number of live envelopes
  long        head_;   // byte offset of the oldest live envelope

  // Serialises the card-touching methods; created by the constructor so it is
  // armed even if begin() never runs.
  SemaphoreHandle_t lock_;
};
