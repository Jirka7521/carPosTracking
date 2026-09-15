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

Requirement: the values must work **regardless of how the device and the sensor are
mounted**. All threshold results below are therefore computed on the orientation-free
vector magnitude, and the per-axis hardware trigger was checked against 300 random
mounting rotations.

## Recommended values

Priority set by the owner: **a missed trip is worse than a false wake.** The values
below are the most sensitive setting the data supports; the cost is about one extra
false wake per day compared with the balanced setting.

| Parameter | Value | Acceptable range |
|---|---|---|
| Wake threshold — ADXL345 `THRESH_ACT`, AC-coupled, X+Y+Z | **0.0625 g** (register value **1**, 62.5 mg/LSB) | 1–2 |
| Software confirm after the interrupt | **none** — every interrupt starts GNSS acquisition | — |
| False-wake timeout — no GNSS speed > 5 km/h after a wake | **240 s** | 180–300 s |
| Stop timeout — back to slow/sleep after speed ≤ 5 km/h for | **600 s** | 600–900 s |
| Fast-mode report interval | **5–10 s** | — |
| Safety net — slow-mode timer wake with a GNSS speed check | **existing send interval** (≤ 15 min recommended) | — |

What register 1 buys and costs, from the data:

| | register 1 (0.0625 g) | register 2 (0.125 g) | register 3 (0.1875 g) |
|---|---|---|---|
| starts caught in the first minute | **61 %** | 58 % | 52 % |
| starts caught within 2 min | **84 %** | 84 % | 84 % |
| never caught during the trip | 16 % | 16 % | 16 % |
| false-wake events per day (parked) | 3.1 | 2.2 | 2.1 |
| margin above 100 Hz sensor-noise peak (~0.017 g) | 3.6× | 7× | 11× |
| margin above worst-case thermal offset drift (~0.05 g on Z for a 40 °C swing) | 1.3× | 2.5× | 3.8× |

Registers 1 and 2 catch exactly the same trips after two minutes; register 1 only
reacts a little earlier. The 16 % that no threshold catches are short parking-lot
moves that never exceed even 0.06 g in the stored 2 Hz peaks; at the real 100 Hz rate
the sensor sees far more of the vibration, so most of those will in practice fire too.

Tuning rule: if false wakes become a nuisance (a hot afternoon can produce one on its
own at register 1 because of thermal offset drift), go to register 2 — it costs nothing
in trips caught. Never go to register 3 or above under this priority, and never use
`THRESH_ACT = 0` (the datasheet warns it misbehaves).

Why the other numbers moved: the false-wake timeout is 240 s rather than 180 s so that
a driver who gets in, wakes the tracker and pulls away three or four minutes later is
still caught in the same wake (a shorter timeout would only cost one extra wake cycle,
never a miss). The stop timeout stays at 600 s; 900 s would trade 5 extra fast-mode
minutes per day for 0.3 fewer sleep transitions, which is the only place a trip can be
missed, so it is an acceptable alternative. The timer wake that already exists in slow
mode is the last line of defence: with the send interval at 15 min or less, a trip that
somehow never triggers the interrupt is still picked up within one interval.

## Orientation independence

Two things depend on the mount and both are handled:

- **Gravity** lands on whichever axis points down. AC-coupled activity detection
  subtracts a reference sample taken at arm time, so gravity and any parking slope
  cancel out on every axis. Fully orientation-free in hardware.
- **The hardware compares per axis, not the magnitude.** An acceleration along the
  diagonal of two or three axes shows only 71 % or 58 % of its size on each one, so the
  effective per-axis threshold varies with the mount by up to a factor of 1.73. At
  register 1 the effective threshold is therefore 0.0625–0.108 g depending on the mount,
  which in every case is below register 2's best case, so the rotation simulation's
  register-2 row (83.7 % caught within 3 min in every one of 300 mounts) is a lower
  bound for register 1. Register 4 (0.25 g) is the first value where the worst mount
  drops below the 80 % floor.
- A **software confirm** step (re-check the vector norm of a − a_rest at 100 Hz before
  powering the modem) would make behaviour identical in every frame, but it can only
  reject wakes, never add them. Under the "never miss" priority it is deliberately left
  out; it is the first thing to add if false wakes ever need reducing without touching
  the register.
- A device that shifts while asleep wakes once (the shift itself exceeds the
  threshold); the re-arm before the next sleep takes the new orientation as reference.
- **DC-coupled mode would break this**: it compares the raw reading including gravity.
  Keep bit 7 of `ACT_INACT_CTL` set.

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
- Because the stored triple is assembled per axis from possibly different moments, its
  vector norm is an **upper bound** on the true simultaneous magnitude. Measured ratio
  of norm to largest-axis deviation on the same rows: median 1.00, p90 1.41, max 1.73.

## Threshold analysis

Metric: for each row the deviation vector d = (x − bx, y − by, z − bz) from the local
rest baseline (rolling median of the surrounding stationary rows within the same
parking segment) and its Euclidean norm |d|, which is orientation-free.

Rows analysed: 26 143 (2026-08-17 → 2026-09-15, 30 days), 21 726 stationary rows
(speed ≤ 2 km/h for this row and the 3 rows either side), 196 trip starts, 2 786 moving
rows.

### Parked noise floor, |d|

| Set | p50 | p90 | p99 | p99.9 | max |
|---|---|---|---|---|---|
| parked | 0.004 g | 0.006 g | 0.018 g | 1.23 g | 2.97 g |
| parked ≥ 10 min both sides | 0.004 g | 0.006 g | 0.012 g | 1.05 g | 2.97 g |

The floor is one LSB. The expected 100 Hz sensor-noise peak over a 60 s window
(≈ 4 σ of 6 000 samples, 1.1 LSB rms on Z) is about 0.017 g, so every candidate
threshold from 0.0625 g upward is safe against sensor noise. Every parked exceedance is
a real disturbance — doors, loading, someone leaning on the car — arriving in bursts of
roughly five minutes, about two bursts per day. Each burst is one wake event whatever
the threshold.

### Detection and false wakes by |d| threshold

Baseline frozen at the row before the start, search continues through the trip.
A false-wake *event* groups parked exceedances less than 10 min apart.

| Threshold \|d\| | first minute | ≤ 2 min | ≤ 3 min | never in trip | parked rows > thr | false-wake events / day | moving > 20 km/h rows > thr |
|---|---|---|---|---|---|---|---|
| 0.0625 g | 61 % | 84 % | 84 % | 16 % | 108 | 3.1 | 99.5 % |
| 0.125 g | 58 % | 84 % | 84 % | 16 % | 76 | 2.2 | 95 % |
| **0.1875 g** | **52 %** | **84 %** | **84 %** | **16 %** | **68** | **2.1** | **84 %** |
| 0.25 g | 46 % | 81 % | 82 % | 18 % | 58 | 1.9 | 71 % |
| 0.3125 g | 36 % | 78 % | 81 % | 18 % | 55 | 1.8 | 60 % |
| 0.375 g | 29 % | 69 % | 77 % | 19 % | 50 | 1.5 | 49 % |
| 0.5 g | 17 % | 41 % | 50 % | 28 % | 43 | 1.3 | 32 % |

The ~16 % of trips that never exceed even 0.0625 g are short parking-lot moves that no
threshold catches. The share of cruising minutes above the threshold falls off with a
frozen reference, which is why the accelerometer should only be used to **wake**, and
GNSS speed to decide when to go back to sleep.

### Mounting-rotation simulation of the per-axis hardware trigger

The deviation vector of every row was rotated by 300 uniformly random rotations and
the ADXL345 rule applied: fire when any axis exceeds `THRESH_ACT`. Reported as
min / median / max over the 300 mounts.

| `THRESH_ACT` | starts detected ≤ 3 min | never detected in trip | false-wake events / day |
|---|---|---|---|
| 0.125 g (2) | 83.7 / 83.7 / 83.7 % | 16.3 / 16.3 / 16.3 % | 2.1 / 2.2 / 2.2 |
| **0.1875 g (3)** | **81.6 / 82.7 / 83.7 %** | **16.3 / 17.3 / 18.4 %** | **1.9 / 2.0 / 2.1** |
| 0.25 g (4) | 79.1 / 80.9 / 81.6 % | 18.4 / 18.4 / 19.4 % | 1.6 / 1.8 / 1.9 |

Register 2 gives the same result in every orientation, and register 1 (effective
0.0625–0.108 g per axis in any mount) can only do better, so both are orientation-safe.
Register 3 still keeps detection above 80 % everywhere; register 4 dips below the
detection floor in its worst mount.

## Timing analysis

Whole date range, 40 calendar days with data, 433 data outages > 5 min (treated as
unknown, never as "parked"). Moving = speed > 5 km/h; using 3 km/h changes nothing
material. Median report gap 66 s. This part uses GNSS speed only, so it does not depend
on the mount.

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

143 trips, about 3.6 per day (median 5, max 15), roughly 1.6 h of driving per day.
Trip duration p50 = 17 min, p90 = 58 min, p99 = 145 min. Starts peak 14:00–16:00
local, with a smaller morning peak 07:00–10:00.

### Start-up dynamics

From the first row with speed > 0 to the first row > 5 km/h: p50 = 0 min, p90 = 1 min.
To > 20 km/h: p90 = 2 min, p95 = 3 min. This is why 180 s is enough to declare a false
wake, and why a 5–10 s fast interval is enough to follow the ramp.

## Last month only (2026-08-15 → 2026-09-15)

Checked whether restricting the data to the most recent 31 days changes anything.
The threshold analysis is already limited to this window (peak-hold data exists only
from 2026-08-17), so only the timing analysis was rerun.

| | Full range (54 days) | Last month (31 days) |
|---|---|---|
| days with data | 40 | 31 |
| outages > 5 min | 433, 800 h | 396, 265 h |
| stops ≤ 5 min / ≤ 10 min / ≤ 2 h | 50 % / 68 % / 89 % | 53 % / 69 % / 88 % |
| parks ≥ 2 h | 26 | 26 (all of them fall in the last month) |
| wake events per day at 300 / 600 / 900 s stop timeout | 4.6 / 3.6 / 3.3 | 4.7 / 3.6 / 3.3 |
| wasted fast-mode min per day at 300 / 600 / 900 s | 14 / 19 / 24 | 16 / 22 / 28 |
| trips per day (600 s merge) | 3.6 | 3.6 |
| trip duration p50 / p90 | 17 / 58 min | 17 / 92 min |
| time to > 5 km/h, p95 | 1 min | 1 min |
| time to > 20 km/h, p95 | 3 min | 6 min |

The stop pattern and the wake/waste curves overlay the full-range ones; the elbow stays
at 600–900 s and the false-wake timeout keeps a threefold margin over the time to
5 km/h. The higher wasted minutes in the last month are a denominator effect (every
one of the 31 days has data). The only real difference is driving style: fewer but
longer trips recently, with a slower climb to 20 km/h. That touches the fast-mode
reporting cadence, not the sleep timings. **No recommendation changes.**

Reproduce: `node analyze.js accel_speed.csv --from 2026-08-15T00:00:00Z --tripM 600`.

## Implementation notes

- The wake path already exists in hardware and config: ADXL345 INT1 is on **GPIO32**
  (RTC-capable, free) and `kWakeGpioPin` / `kWakeGpioLevel` in `Config.h` arm an ext0
  wake. Interrupt pins are active high by default (`DATA_FORMAT` bit 5 clear), which
  matches `kWakeGpioLevel = 1`.
- ADXL345 setup for the wake source (register addresses from the datasheet, Rev. G):
  - `THRESH_ACT` (0x24) = 1 (fallback 2 if false wakes annoy).
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
- State machine sketch: **sleep** (RTC timer at the slow send interval + INT1 ext0)
  → wake by either source → **acquiring**: if no speed > 5 km/h within 240 s → re-arm,
  sleep; else → **fast**: report every 5–10 s; when speed ≤ 5 km/h for 600 s → re-arm
  with the car at rest, sleep. Both wake sources lead to the same acquiring state, so
  the timer wake doubles as the safety net for a missed interrupt.
- Because the AC reference is taken at arm time, always re-arm activity detection as
  the very last step before deep sleep, after the modem is off and the SD card is
  unmounted, so the reference is a quiet sample.
- If the dashboard's accel values should also be orientation-free, report the peak of
  the magnitude deviation |a − a_rest| instead of the three per-axis peaks. That is a
  separate change to `AccelPeakTracker` and the telemetry pipeline.

## Caveats

- One car, one mounting position in the raw data, seven weeks. The rotation simulation
  covers other mounts mathematically, but the stored per-axis peaks do not rotate
  exactly like a real signal would, so treat the simulation as indicative.
- The peak-hold data cannot show true 100 Hz peaks. Expect real-world detection to be
  faster than the tables and the parked-disturbance rate to be as measured.
- Reproduce with the node scripts from the analysis session (`analyze_accel_wake.js`
  sections 7–8 for the orientation-free part, `analyze.js` for timing) over an export of
  `fix_time, speed_kmph, accel_x_g, accel_y_g, accel_z_g`; no location columns are needed.
