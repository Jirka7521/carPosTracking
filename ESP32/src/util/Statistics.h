#pragma once

// =============================================================================
//  Statistics.h  -  The one median this firmware computes.
// -----------------------------------------------------------------------------
//  The battery path (raw ADC counts, via BatteryMethods) reduces a window of
//  readings to a single published number. The reduction lives here, apart from
//  the battery code, so any future window sampler reuses it rather than writing
//  a second copy that drifts.
//
//  Insertion sort on purpose: these windows are a few hundred entries at most,
//  are already nearly sorted by the time anything trims them, and the algorithm
//  beats anything cleverer at that size with none of the code.
//
//  Header-only and templated so the element type is the caller's choice -
//  uint32_t counts today - rather than forcing everything through a double.
// =============================================================================

#include <cstddef>

namespace statistics {

// Sort `values` ascending, in place.
template <typename T>
inline void sortInPlace(T* values, std::size_t n) {
  for (std::size_t i = 1; i < n; ++i) {
    const T     key = values[i];
    std::size_t j   = i;
    while (j > 0 && values[j - 1] > key) {
      values[j] = values[j - 1];
      --j;
    }
    values[j] = key;
  }
}

// Median of `n` values. Sorts `values` in place as a side effect.
//
// For an even count this averages the two middle entries, which for the integer
// instantiation truncates - deliberately, since the callers are counting ADC
// counts and millivolts where a half is below the noise floor anyway.
//
// `n` must be at least 1; the callers all check emptiness before they get here,
// because "no reading" is a state they have to report rather than a zero.
template <typename T>
inline T medianOf(T* values, std::size_t n) {
  sortInPlace(values, n);
  return (n % 2) ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
}

}  // namespace statistics
