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

  // True while the filesystem is mounted and file operations are usable.
  bool isMounted() const { return mounted_; }

  // Append one line to `path` (a trailing '\n' is added). Creates the file if it
  // does not exist. Returns false on any IO error.
  bool appendLine(const char* path, const std::string& line);

  // Read up to `maxLines` lines from the start of `path` into `linesOut`
  // (newlines stripped, empty lines skipped). A missing file yields zero lines
  // and still returns true. `maxLines == 0` means "no limit".
  bool readLines(const char* path, std::size_t maxLines,
                 std::vector<std::string>& linesOut) const;

  // Number of non-empty lines in `path` (0 if the file does not exist).
  std::size_t countLines(const char* path) const;

  // Rewrite `path` dropping its first `n` non-empty lines and keeping the rest.
  // If `n` covers the whole file, the file is removed. Used both to pop entries
  // that were just delivered and to trim the oldest when the queue is capped.
  bool dropFirstLines(const char* path, std::size_t n);

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
