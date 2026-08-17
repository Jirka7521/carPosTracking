#pragma once

// =============================================================================
//  QueueIndex  -  Where a persistent queue's live region starts, on the card.
// -----------------------------------------------------------------------------
//  Responsibility (single!): persist and recover the two numbers that describe
//  a FixQueue's state - the byte offset of the oldest live entry ("head") and
//  how many entries are live from there. It owns the tiny sidecar file those
//  numbers live in, and nothing else; FixQueue owns the entries themselves.
//
//  Why it exists: without it, dropping the oldest entries means rewriting the
//  entire queue file (SdCard::dropFirstLines), which is O(file size) per pop.
//  On a backlog of hundreds of megabytes that never converges. With a head
//  offset, the data file is strictly append-only and a pop is a number moving
//  forward - the whole reason a week of 1 Hz debug samples can be queued and
//  then actually drained.
//
//  Wire format - one line, so a human staring at a card can read it:
//      head=<byte offset> count=<live entries>
//
//  Durability: the record is one short line, so it is written straight over the
//  old one (SdCard::writeFileDirect) rather than staged through a temp file. A
//  single-sector overwrite has no window in which the file is missing, which is
//  the failure that would actually hurt - see load() for what a damaged record
//  costs.
// =============================================================================

#include <cstddef>

#include "sdcard/SdCard.h"

class QueueIndex {
 public:
  // Borrows `card` and `filePath` (both must outlive this object). `filePath`
  // is the sidecar's own path, not the queue file's.
  QueueIndex(SdCard& card, const char* filePath);

  // Recover the stored position. Returns false when there is no sidecar or it
  // does not parse - which is not a failure so much as "start from the top":
  // the caller falls back to head 0 and a full recount.
  //
  // That fallback is safe rather than merely tolerable. Re-reading from the top
  // re-delivers entries the API has already stored, and the API dedupes on
  // (device, fix time), so the cost is duplicate *deliveries*, never duplicate
  // rows. Losing the sidecar therefore costs airtime, not data - which is why
  // store() may fail loudly and the queue still keeps working.
  bool load(long& headOut, std::size_t& countOut) const;

  // Write the current position. Returns false on IO error; the caller should
  // log and carry on, since an unwritten index only means the next boot starts
  // further back than it had to.
  bool store(long head, std::size_t count);

  // Remove the sidecar (the queue file it described is going away too).
  bool clear();

 private:
  SdCard&     card_;
  const char* filePath_;
};
