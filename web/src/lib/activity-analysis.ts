import type { ActivityLap, ActivityStreams } from './activity.ts'

// Fixed rules for one running activity, computed from the streams and laps the activity page already has.
// Heart rate is only present for the owner (with consent), so the drift is never shown to anyone else.

// Minetti's energy cost of running relative to flat ground, as TrainingLoad.GradeFactor in the API.
export function gradeFactor(grade: number) {
  const i = Math.min(0.45, Math.max(-0.45, grade))
  return ((((155.4 * i - 30.4) * i - 43.3) * i + 46.3) * i + 19.5) * (i / 3.6) + 1
}

// Shared limits: samples further apart than the watch's auto-pause break the stream, and slower than a
// brisk walk counts as standing still.
const gapS = 10
const windowS = 30
const movingSpeed = 1.5

type Window = { durationS: number; distanceM: number; adjustedM: number; hrS: number | null }

// ~30 s windows of continuous movement with distance, grade-adjusted distance and heart rate × time.
// Corrupt values and reset time or distance break the stream, as in the record engine.
function movingWindows(streams: ActivityStreams, fromS: number): Window[] {
  const { t, dist, alt, hr } = streams
  if (!dist || dist.length !== t.length) return []
  const windows: Window[] = []
  let start = -1
  let previous = -1
  let hrS = 0
  let hrMissing = false
  const close = (end: number) => {
    if (start < 0 || end <= start) return
    const durationS = t[end] - t[start]
    const distanceM = dist[end]! - dist[start]!
    if (durationS <= 0 || distanceM / durationS < movingSpeed) return
    const from = alt?.[start]
    const to = alt?.[end]
    const adjustedM =
      from != null && to != null && Number.isFinite(from) && Number.isFinite(to)
        ? distanceM * gradeFactor((to - from) / distanceM)
        : distanceM
    windows.push({ durationS, distanceM, adjustedM, hrS: hrMissing ? null : hrS })
  }
  for (let i = 0; i < t.length; i++) {
    const d = dist[i]
    const valid = t[i] >= fromS && d !== null && Number.isFinite(d) && d >= 0
    const broken =
      !valid ||
      (previous >= 0 && (t[i] <= t[previous] || d! < dist[previous]! || t[i] - t[previous] > gapS))
    if (broken) {
      close(previous)
      start = previous = -1
      if (!valid) continue
    }
    if (start < 0) {
      start = previous = i
      hrS = 0
      hrMissing = false
      continue
    }
    const h = hr?.[i]
    if (h == null || !Number.isFinite(h) || h <= 0) hrMissing = true
    else hrS += h * (t[i] - t[previous])
    previous = i
    if (t[i] - t[start] >= windowS) {
      close(i)
      start = i
      hrS = 0
      hrMissing = false
    }
  }
  close(previous)
  return windows
}

export const driftRules = {
  warmUpS: 600,
  minimumS: 20 * 60,
  // Coefficient of variation of the grade-adjusted speed; intervals and fartlek are well above it.
  maxVariation: 0.15,
  stable: 5,
  high: 10,
} as const

export type HeartRateDrift = {
  // Grade-adjusted meters per heartbeat in each half; the drift is how much it fell, in percent.
  firstEfficiency: number
  secondEfficiency: number
  percent: number
  level: 'stable' | 'drifted' | 'high'
}

// Aerobic decoupling: grade-adjusted speed per heartbeat in the first half of the moving time against the
// second. Only for steady activities of at least 20 minutes after a 10-minute warm-up.
export function heartRateDrift(streams: ActivityStreams): HeartRateDrift | null {
  if (!streams.hr) return null
  const windows = movingWindows(streams, (streams.t[0] ?? 0) + driftRules.warmUpS).filter(
    (w) => w.hrS !== null && w.hrS > 0,
  )
  const total = windows.reduce((sum, w) => sum + w.durationS, 0)
  if (total < driftRules.minimumS) return null
  const speeds = windows.map((w) => w.adjustedM / w.durationS)
  const mean = windows.reduce((sum, w) => sum + w.adjustedM, 0) / total
  const variance =
    windows.reduce((sum, w, i) => sum + w.durationS * (speeds[i] - mean) ** 2, 0) / total
  if (Math.sqrt(variance) / mean > driftRules.maxVariation) return null
  const halves = [
    { m: 0, beats: 0 },
    { m: 0, beats: 0 },
  ]
  let elapsed = 0
  for (const w of windows) {
    const half = halves[elapsed + w.durationS / 2 <= total / 2 ? 0 : 1]
    half.m += w.adjustedM
    // hr × seconds / 60 = heartbeats
    half.beats += w.hrS! / 60
    elapsed += w.durationS
  }
  if (halves.some((h) => h.beats <= 0)) return null
  const firstEfficiency = halves[0].m / halves[0].beats
  const secondEfficiency = halves[1].m / halves[1].beats
  const percent = ((firstEfficiency - secondEfficiency) / firstEfficiency) * 100
  return {
    firstEfficiency,
    secondEfficiency,
    percent,
    level: percent < driftRules.stable ? 'stable' : percent < driftRules.high ? 'drifted' : 'high',
  }
}

export const pacingRules = { minimumM: 3000, even: 2, finishShare: 0.2 } as const

export type Pacing = {
  // Moving time for each half of the distance
  firstHalfS: number
  secondHalfS: number
  // Positive = the second half was slower
  percent: number
  split: 'negative' | 'even' | 'positive'
  // Pace over the last fifth of the distance against the rest; positive = slower at the end
  finishPercent: number
}

// Moving time at each distance point: auto-pauses, gaps and standing still don't count.
function movingClock(streams: ActivityStreams) {
  const { t, dist } = streams
  if (!dist || dist.length !== t.length) return null
  const points: { d: number; s: number }[] = []
  let clock = 0
  let previous = -1
  for (let i = 0; i < t.length; i++) {
    const d = dist[i]
    if (d === null || !Number.isFinite(d) || d < 0) continue
    if (previous >= 0) {
      const dt = t[i] - t[previous]
      const dd = d - dist[previous]!
      // A reset distance or time can't be ordered on a moving clock
      if (dt <= 0 || dd < 0) return null
      if (dt <= gapS && dd / dt >= 0.5) clock += dt
    }
    points.push({ d, s: clock })
    previous = i
  }
  return points.length > 1 ? points : null
}

function timeAt(points: { d: number; s: number }[], distance: number) {
  let i = points.findIndex((p) => p.d >= distance)
  if (i < 0) return points[points.length - 1].s
  if (i === 0) return points[0].s
  const a = points[i - 1]
  const b = points[i]
  return b.d === a.d ? b.s : a.s + ((distance - a.d) / (b.d - a.d)) * (b.s - a.s)
}

// Split and finish from the distance stream. Within ±2 % counts as even.
export function pacing(streams: ActivityStreams): Pacing | null {
  const points = movingClock(streams)
  if (!points) return null
  const from = points[0]
  const to = points[points.length - 1]
  const distance = to.d - from.d
  if (distance < pacingRules.minimumM || to.s - from.s <= 0) return null
  const half = timeAt(points, from.d + distance / 2) - from.s
  const firstHalfS = half
  const secondHalfS = to.s - from.s - half
  if (firstHalfS <= 0 || secondHalfS <= 0) return null
  const percent = ((secondHalfS - firstHalfS) / firstHalfS) * 100
  const finishStart = timeAt(points, to.d - distance * pacingRules.finishShare)
  const restPace = (finishStart - from.s) / (distance * (1 - pacingRules.finishShare))
  const finishPace = (to.s - finishStart) / (distance * pacingRules.finishShare)
  return {
    firstHalfS,
    secondHalfS,
    percent,
    split: Math.abs(percent) <= pacingRules.even ? 'even' : percent < 0 ? 'negative' : 'positive',
    finishPercent: restPace > 0 ? ((finishPace - restPace) / restPace) * 100 : 0,
  }
}

export const intervalRules = {
  // The fast laps must be at least this much faster than the slow ones on average
  minimumRatio: 1.2,
  minimumReps: 2,
  // Shorter laps are an accidental button press
  minimumLapS: 10,
  minimumLapM: 50,
} as const

export type IntervalRep = {
  number: number
  distanceM: number
  durationS: number
  avgHr: number | null
  // Slow time after this rep until the next one; null after the last
  recoveryS: number | null
}

export type Intervals = {
  reps: IntervalRep[]
  // Fastest and slowest rep pace (s/km) and the last rep against the average of the others (positive = slower)
  fastestPace: number
  slowestPace: number
  averagePace: number
  lastPercent: number
}

const lapTime = (l: ActivityLap) => l.movingS ?? l.durationS

// Work and recovery from the watch's laps: the speeds are split into a fast and a slow group where the
// variance between the groups is largest, and consecutive fast laps become one rep (autolap inside a rep).
// Steady runs with autolap don't have a clear enough difference to become intervals.
export function intervals(laps: ActivityLap[]): Intervals | null {
  const valid = laps.filter(
    (l) => lapTime(l) >= intervalRules.minimumLapS && l.distanceM >= intervalRules.minimumLapM,
  )
  if (valid.length < 3) return null
  const speeds = valid.map((l) => l.distanceM / lapTime(l))
  const sorted = [...speeds].sort((a, b) => a - b)
  let best = { score: -1, threshold: 0, slow: 0, fast: 0 }
  for (let i = 1; i < sorted.length; i++) {
    const slow = sorted.slice(0, i)
    const fast = sorted.slice(i)
    const slowMean = slow.reduce((a, b) => a + b, 0) / slow.length
    const fastMean = fast.reduce((a, b) => a + b, 0) / fast.length
    const score = slow.length * fast.length * (fastMean - slowMean) ** 2
    if (score > best.score)
      best = { score, threshold: (sorted[i - 1] + sorted[i]) / 2, slow: slowMean, fast: fastMean }
  }
  if (best.fast < best.slow * intervalRules.minimumRatio) return null

  const reps: (IntervalRep & { hrS: number; hrTime: number })[] = []
  let recovery = 0
  let inRep = false
  valid.forEach((lap, i) => {
    const fast = speeds[i] > best.threshold
    if (!fast) {
      if (reps.length) recovery += lapTime(lap)
      inRep = false
      return
    }
    if (!inRep) {
      if (reps.length) reps[reps.length - 1].recoveryS = recovery
      recovery = 0
      reps.push({
        number: reps.length + 1,
        distanceM: 0,
        durationS: 0,
        avgHr: null,
        recoveryS: null,
        hrS: 0,
        hrTime: 0,
      })
      inRep = true
    }
    const rep = reps[reps.length - 1]
    rep.distanceM += lap.distanceM
    rep.durationS += lapTime(lap)
    if (lap.avgHr !== null) {
      rep.hrS += lap.avgHr * lapTime(lap)
      rep.hrTime += lapTime(lap)
    }
  })
  if (reps.length < intervalRules.minimumReps) return null
  const paces = reps.map((r) => (r.durationS / r.distanceM) * 1000)
  const others = paces.slice(0, -1)
  const othersPace = others.reduce((a, b) => a + b, 0) / others.length
  return {
    reps: reps.map(({ hrS, hrTime, ...rep }) => ({
      ...rep,
      avgHr: hrTime > 0 ? Math.round(hrS / hrTime) : null,
    })),
    fastestPace: Math.min(...paces),
    slowestPace: Math.max(...paces),
    averagePace:
      (reps.reduce((sum, r) => sum + r.durationS, 0) /
        reps.reduce((sum, r) => sum + r.distanceM, 0)) *
      1000,
    lastPercent: ((paces[paces.length - 1] - othersPace) / othersPace) * 100,
  }
}
