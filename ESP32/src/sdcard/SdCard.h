#pragma once

// =============================================================================
//  SdCard  -  Mount the microSD card and read/append/trim lines of a text file.
// -----------------------------------------------------------------------------
//  Responsibility (single!): own the SPI bus + FAT filesystem on the board's
//  microSD slot and expose a handful of *line-oriented* file primitives. It
//  knows about stdio and the card - nothing about GNSS, envelopes or MQTT. That
//  keeps it a small, reusable storage layer that FixQueue builds the queue on
//  top of.
//
//  Mounting policy: the card is mounted with `format_if_mount_failed = true`, so
//  a blank or corrupt card is formatted exactly once (on first use), while an
//  already-valid filesystem - and any queue file it holds - survives reboots.
//
//  The line helpers stream through the file rather than slurping it whole, so a
//  long backlog never has to fit in this board's modest internal RAM.
// =============================================================================

#include <cstddef>
#include <functional>
#include <string>
#include <vector>

#include "driver/spi_common.h"
#include "sdmmc_cmd.h"

class SdCard {
 public:
  // Borrows `mountPoint` (must outlive this object; with Config.h that is
  // automatic). Pins/host describe the SPI wiring of the card slot.
  SdCard(spi_host_device_t spiHost, int misoPin, int mosiPin, int sclkPin,
         int csPin, const char* mountPoint);
  ~SdCard();

  // Initialise the SPI bus and mount the FAT filesystem (formatting the card if
  // it cannot be mounted). Returns true once the card is ready to use.
  bool begin();

  // Unmount the filesystem and release the SPI bus. Idempotent, and safe to call
  // on a card that never mounted. Call this before deep sleep so the card is not
  // left with a dirty FAT, and so its pins stop being driven; the destructor
  // calls it too.
  void end();

  // True while the filesystem is mounted and file operations are usable.
  bool isMounted() const { return mounted_; }

  // Append one line to `path` (a trailing '\n' is added). Creates the file if it
  // does not exist. Returns false on any IO error.
  bool appendLine(const char* path, const std::string& line);

  // Replace the whole contents of `path` with `content` (a trailing '\n' is
  // added). Used for small documents that are rewritten as a unit - the settings
  // file - rather than appended to like the queue.
  //
  // The write goes to a sibling ".tmp" first and only then displaces the
  // original, so an interrupted write cannot leave a half-rewritten file behind.
  // FAT gives us no atomic replace, so a power cut in the narrow window between
  // the remove and the rename loses the file outright - the caller must treat a
  // missing file as "fall back to defaults", never as an error.
  bool writeFile(const char* path, const std::string& content);

  // Read up to `maxLines` lines from the start of `path` into `linesOut`
  // (newlines stripped, empty lines skipped). A missing file yields zero lines
  // and still returns true. `maxLines == 0` means "no limit".
  bool readLines(const char* path, std::size_t maxLines,
                 std::vector<std::string>& linesOut) const;

  // Number of non-empty lines in `path` (0 if the file does not exist).
  std::size_t countLines(const char* path) const;

  // Hand every non-empty line of `path` to `visit`, in file order, without
  // modifying anything. A missing file is not an error - `visit` is simply never
  // called. The read-only counterpart of rewriteLines(), for callers that need
  // to inspect a whole file but must not pay readLines()'s cost of materialising
  // it: only one line exists in memory at a time.
  bool forEachLine(const char* path,
                   const std::function<void(const std::string&)>& visit) const;

  // Rewrite `path` dropping its first `n` non-empty lines and keeping the rest.
  // If `n` covers the whole file, the file is removed. Used both to pop entries
  // that were just delivered and to trim the oldest when the queue is capped.
  bool dropFirstLines(const char* path, std::size_t n);

  // Rewrite `path` keeping only the lines `keep` approves of, in order, and
  // report how many survived. If none do, the file is removed. A missing file is
  // not an error (zero survivors), and `keep` is never called for it.
  //
  // The generalisation of dropFirstLines() for callers whose "drop this one"
  // decision depends on the line's *contents* rather than its position - the
  // retry queue, which must weigh each entry's schedule. It streams exactly the
  // same way: one line in memory at a time, survivors written to a sibling
  // ".tmp" that is only then swapped in. That is the whole point of it. Deciding
  // in RAM instead means holding the entire file, which on this board's internal
  // heap is a crash waiting for a big enough backlog.
  //
  // `keep` must be a pure decision over the line: it is called once per line, in
  // file order, and must not touch this file itself.
  bool rewriteLines(const char* path,
                    const std::function<bool(const std::string&)>& keep,
                    std::size_t& survivorsOut);

  // Delete `path` entirely. Returns true if it is gone afterwards.
  bool removeFile(const char* path);

 private:
  // Read one line from `f` into `out` (without the newline, with a trailing
  // '\r' stripped). Returns false at end-of-file. Used by the helpers above so
  // arbitrarily long lines never need a fixed-size buffer.
  static bool readLine(std::FILE* f, std::string& out);

  spi_host_device_t spiHost_;
  int               misoPin_;
  int               mosiPin_;
  int               sclkPin_;
  int               csPin_;
  const char*       mountPoint_;

  sdmmc_card_t* card_;     // owned FAT card handle (nullptr until mounted)
  bool          mounted_;  // true between a successful begin() and destruction
};
