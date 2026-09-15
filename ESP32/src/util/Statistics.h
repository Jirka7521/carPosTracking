#pragma once

// =============================================================================
//  Statistics.h  -  The one median this firmware computes.
// -----------------------------------------------------------------------------
//  Two subsystems reduce a window of readings to a single published number: the
//  battery path (raw ADC counts, via BatteryMethods) and the ambient path
//  (DHT22 temperature and humidity, via AmbientWindowSampler). They aggregate
//  different types over different cadences, but the reduction is the same one,
//  so it lives here once rather than being written twice and drifting.
//
//  Insertion sort on purpose: these windows are a few hundred entries at most,
//  are already nearly sorted by the time anything trims them, and the algorithm
//  beats anything cleverer at that size with none of the code.
//
//  Header-only and templated because the two callers differ only in element
//  type - uint32_t counts on one side, float degrees on the other - and a
//  template is cheaper than either duplicating the body or forcing everything
//  through a double.
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
