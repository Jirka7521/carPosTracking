# Motion wake: accelerometer threshold and mode timings

Analysis date: 2026-09-15. Source: the `positions` table of the production database,
read-only, one device (`35a29c48-…`), 29 101 fixes with accelerometer data between
2026-07-23 and 2026-09-15.

## Context

The planned power scheme: the tracker deep-sleeps and reports slowly; the ADXL345
activity interrupt wakes it when the car starts moving; it then reports at a fast
interval; once the car has been standing still long enough it goes back to sleep.
Three numbers were needed and are derived here from the recorded data:

1. the acceleration threshold that means "the car started driving",
2. how long after a wake with no movement to give up (false wake),
3. how long the car must stand still before dropping back to slow mode.

## Recommended values

| Parameter | Value | Acceptable range |
|---|---|---|
| Target wake threshold (orientation-free, vector magnitude) | **0.19 g** | 0.125–0.25 g |
| Hardware trigger — ADXL345 `THRESH_ACT`, AC-coupled, X+Y+Z | **0.125 g** (register value **2**, 62.5 mg/LSB) | 2–3 |
| Software confirm after the interrupt — \|a − a_rest\| over 100 Hz samples | **0.19 g** in ≥ 5 samples within 2 s | 0.125–0.25 g |
| False-wake timeout — no GNSS speed > 5 km/h after a wake | **180 s** | 120–240 s |
| Stop timeout — back to slow/sleep after speed ≤ 5 km/h for | **600 s** | 300–900 s |
| Fast-mode report interval | **5–10 s** | — |

Tuning rule: more than a handful of false wakes per day → raise the software confirm to
0.25 g (keep the register at 2). Trips being missed → lower the confirm to 0.125 g. Do
not lower the register below 2: real disturbances, not sensor noise, cause the false
wakes, so a lower hardware trigger only adds wakes without catching more trips.

### Why two stages: orientation independence

The requirement is that the numbers work whichever way the device and sensor are
mounted. Two things stand in the way and the two stages address them:

- **Gravity** lands on whichever axis points down. AC-coupled activity detection
  subtracts a reference sample taken at arm time, so gravity and any parking slope
  cancel out on every axis. This part is fully orientation-free in hardware.
- **The hardware compares per axis, not the magnitude.** An acceleration along the
  diagonal of two or three axes shows only 71 % or 58 % of its size on each one, so a
  single-stage per-axis threshold of 0.19 g would really mean 0.19 g in the best mount and
  0.32 g in the worst. Setting the register to **2 (0.125 g)** bounds the effective
  hardware threshold to **0.125–0.217 g for every orientation**, which is inside the
  acceptable band, so the interrupt alone is already good enough. The software confirm
  then applies the exact 0.19 g on the vector norm, which is the same number in every
  orientation, and rejects the extra interrupts the lower register value lets through.
  A rejected wake costs only an ESP32 boot of a few hundred milliseconds; the modem and
  GNSS are never powered.

Measured cost of register 2 versus 3: 72 versus 60 parked rows over the threshold in
30 days, the same disturbance bursts either way; trips caught in the first minute rise
from 49 % to 55 %.

## What the stored accelerometer values are

This matters for anyone redoing the analysis.

- **From 2026-08-17** the firmware runs peak-hold mode (`kAccelPeakEnabled`,
  `AccelPeakTracker`): the sensor is sampled every 500 ms and each axis keeps the
  **signed sample with the largest absolute value** over the ~65 s report window. Each
  row is therefore a per-axis worst case for the previous minute, the three axes may
  come from different moments, and values clip at ±1.996 g (±2 g range).
- **Before 2026-08-17** each row is one instantaneous sample. Those rows were used only
  for the trip-timing analysis (speed is valid throughout), not for the threshold.
- Gravity is included. Until 2026-08-02 it sat on the Y axis; from then on it sits on Z
  (≈ +1.0 g at rest). On 2026-09-08 19:56 UTC the orientation flipped mid-parking
  (Z −0.97 → +1.02), so any baseline must be estimated locally, not globally.
  2026-08-23 the device was clearly handled (average |Z| ≈ 0.4) and was excluded.
- The ADXL345 free-runs at 100 Hz, so a 2 Hz sample sees 1 in 50 readings. Both the
  parked noise floor and the driving peaks in the data are **understated**. Real
  detection will be faster and more reliable than the tables below show; real false
  wakes will be about the same, because they come from mechanical events, not noise.

## Threshold analysis

Metric: for each row, the maximum over axes of |value − rest baseline for that axis|,
which is what the ADXL345 evaluates in AC-coupled activity mode (reference sample taken
when detection is armed, any enabled axis exceeding `THRESH_ACT` fires). Baseline: rolling
median of the surrounding stationary rows within the same parking segment.

Rows analysed: 26 143 (2026-08-17 → 2026-09-15, 30 days), 21 726 stationary rows
(speed ≤ 2 km/h for this row and the 3 rows either side), 196 trip starts, 2 786 moving
rows.

### Parked noise floor

| Metric | p50 | p90 | p99 | p99.9 | max |
|---|---|---|---|---|---|
| per-axis deviation, parked | 0.004 g | 0.004 g | 0.016 g | 1.04 g | 2.68 g |
| \|magnitude − 1 g\|, parked | 0.020 g | 0.030 g | 0.042 g | 0.74 g | 1.60 g |

The floor is one LSB. The expected 100 Hz sensor-noise peak over a 60 s window
(≈ 4 σ of 6 000 samples, 1.1 LSB rms on Z) is about 0.017 g, so every candidate
threshold from 0.0625 g upward is safe against sensor noise. Every parked exceedance in
the data is a real disturbance — doors, loading, someone leaning on the car — arriving
in bursts of roughly five minutes, two to three bursts per day. Each burst is one wake
event whatever the threshold.

### Detection: how soon after a trip start the threshold is exceeded

Baseline frozen at the row before the start, search continues through the trip.

| Threshold | first minute | ≤ 2 min | ≤ 3 min | never in trip | parked rows > thr | false wakes / 24 h parked |
|---|---|---|---|---|---|---|
| 0.0625 g (1) | 61 % | 84 % | 84 % | 16 % | 103 | 6.2 rows |
| 0.125 g (2) | 55 % | 82 % | 84 % | 16 % | 72 | 4.3 rows |
| **0.1875 g (3)** | **49 %** | **76 %** | **82 %** | **17 %** | **60** | **3.6 rows** |
| 0.25 g (4) | 36 % | 64 % | 77 % | 18 % | 55 | 3.3 rows |
| 0.3125 g (5) | 26 % | 45 % | 61 % | 20 % | 50 | 3.0 rows |
| 0.375 g (6) | 18 % | 29 % | 40 % | 27 % | 49 | 3.0 rows |
| 0.5 g (8) | 14 % | 18 % | 22 % | 48 % | 43 | 2.6 rows |

"False wakes" counts parked *rows*; because they cluster, the number of actual wake
events is lower (about 2–3 per day at any threshold in the table). The ~16 % of trips
that never exceed even 0.0625 g are short parking-lot moves that no threshold catches.

Once the car is above 20 km/h, 99 % of minutes exceed 0.19 g and 100 % exceed 0.125 g
against a fixed rest baseline. Against a baseline frozen at the trip start the share
drops (73 % at 0.19 g), which is why the accelerometer should only be used to **wake**,
and GNSS speed to decide when to go back to sleep.

## Timing analysis

Whole date range, 40 calendar days with data, 433 data outages > 5 min (treated as
unknown, never as "parked"). Moving = speed > 5 km/h; using 3 km/h changes nothing
material. Median report gap 66 s.

### Stops between moving periods (n = 230)

| ≤ 3 min | ≤ 5 min | ≤ 10 min | ≤ 30 min | ≤ 1 h | ≤ 2 h | ≤ 4 h | ≤ 8 h |
|---|---|---|---|---|---|---|---|
| 32 % | 50 % | 68 % | 79 % | 86 % | 89 % | 93 % | 96 % |

There is no natural gap between "traffic light" and "parked for the night"; the mass is
spread smoothly. Parks ≥ 2 h: 26.

### Stop timeout candidates

| Stop timeout | stops shorter (no sleep) | in-trip stops causing a re-wake | wake events / day | wasted fast-mode min / day |
|---|---|---|---|---|
| 60 s | 0 | 204 | 7.5 | 6 |
| 180 s | 74 | 130 | 5.6 | 12 |
| 300 s | 116 | 88 | 4.6 | 14 |
| **600 s** | **155** | **49** | **3.6** | **19** |
| 900 s | 166 | 38 | 3.3 | 24 |
| 1200 s | 170 | 34 | 3.2 | 30 |
| 1800 s | 181 | 23 | 2.9 | 37 |

The wake count flattens after 600 s while the wasted awake time keeps growing
linearly, so 10 minutes is the elbow.

### Trip statistics (600 s merge window)

143 trips, about 4.5 per day (median 5, max 15), roughly 1.5 h of driving per day.
Trip duration p50 = 11 min, p90 = 48 min, p99 = 110 min. Starts peak 14:00–16:00
local, with a smaller morning peak 07:00–10:00.

### Start-up dynamics

From the first row with speed > 0 to the first row > 5 km/h: p50 = 0 min, p90 = 1 min.
To > 20 km/h: p90 = 2 min, p95 = 3 min. This is why 180 s is enough to declare a false
wake, and why a 5–10 s fast interval is enough to follow the ramp.

## Implementation notes

- The wake path already exists in hardware and config: ADXL345 INT1 is on **GPIO32**
  (RTC-capable, free) and `kWakeGpioPin` / `kWakeGpioLevel` in `Config.h` arm an ext0
  wake. Interrupt pins are active high by default (`DATA_FORMAT` bit 5 clear), which
  matches `kWakeGpioLevel = 1`.
- ADXL345 setup for the wake source (register addresses from the datasheet, Rev. G):
  - `THRESH_ACT` (0x24) = 3.
  - `ACT_INACT_CTL` (0x27) = 0xF0 — AC-coupled activity on X, Y, Z. The AC reference is
    sampled when activity detection is enabled, so re-arm it immediately before deep
    sleep, with the car at rest.
  - `INT_ENABLE` (0x2E) bit 4 set; `INT_MAP` (0x2F) bit 4 clear → Activity on INT1.
  - Read `INT_SOURCE` (0x30) on boot to clear the latched interrupt.
  - Optional: `BW_RATE` (0x2C) with `LOW_POWER` set at 25 Hz drops the sensor from
    ~140 µA to ~40 µA while the ESP32 sleeps; 25 Hz still catches car motion.
- Do not use `TIME_INACT`/`THRESH_INACT` for the stop timeout: it maxes at 255 s and the
  frozen AC reference is unreliable during steady cruising. Keep the 600 s timer in
  firmware, driven by GNSS speed.
- State machine sketch: **sleep** → (INT1) → **acquiring**: if no speed > 5 km/h within
  180 s → re-arm, sleep; else → **fast**: report every 5–10 s; when speed ≤ 5 km/h for
  600 s → re-arm, sleep.

## Caveats

- One car, one mounting position, seven weeks of data. A different mount or a
  different car changes the noise floor; the threshold is coarse (62.5 mg steps) so this
  is unlikely to move the recommendation by more than one step.
- The peak-hold data cannot show true 100 Hz peaks. Expect real-world detection to be
  faster than the tables and the parked-disturbance rate to be as measured.
- Reproduce with the node scripts from the analysis session (`analyze_accel_wake.js`,
  `analyze.js`) over an export of `fix_time, speed_kmph, accel_x_g, accel_y_g, accel_z_g`;
  no location columns are needed.
