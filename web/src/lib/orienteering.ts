import type { ActivityLap, ActivityStreams } from './activity.ts'

// Orienteering legs from the watch's laps: one lap per control. Without the map, a probable miss shows as a leg
// that took much longer per straight-line km than the activity's other legs.

export type Leg = {
  number: number
  durationS: number
  distanceM: number
  // Standing still according to the watch (null without moving time)
  stillS: number | null
  // Straight line from the lap's start to its end; null without GPS (only the owner gets GPS)
  straightM: number | null
  miss: boolean
}

// shortcut: initial values, not calibrated against real orienteering courses. Change them here; nothing is stored.
// Legs shorter than this in a straight line are too noisy to judge (a control next to the previous one).
const minStraightM = 50
// A leg is a probable miss when its time per straight-line km exceeds the median leg's by this factor.
const missFactor = 1.5
// Samples further than this from the lap boundary are not used as its position.
const maxGapS = 10

function distanceM(lat1: number, lon1: number, lat2: number, lon2: number) {
  const rad = Math.PI / 180
  const h =
    Math.sin(((lat2 - lat1) * rad) / 2) ** 2 +
    Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(((lon2 - lon1) * rad) / 2) ** 2
  return 2 * 6371000 * Math.asin(Math.sqrt(h))
}

// The position at the sample closest in time to s, if one is close enough
function positionAt(streams: ActivityStreams, s: number): [number, number] | null {
  const { t, lat, lon } = streams
  let best = -1
  for (let i = 0; i < t.length; i++)
    if (
      lat?.[i] != null &&
      lon?.[i] != null &&
      Math.abs(t[i] - s) <= maxGapS &&
      (best < 0 || Math.abs(t[i] - s) < Math.abs(t[best] - s))
    )
      best = i
  return best < 0 ? null : [lat![best]!, lon![best]!]
}

function median(values: number[]) {
  const sorted = [...values].sort((a, b) => a - b)
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

export function orienteeringLegs(laps: ActivityLap[], streams: ActivityStreams | null): Leg[] {
  const legs = laps.map((lap, i) => {
    const from = streams?.lat ? positionAt(streams, lap.startS) : null
    const to = streams?.lat ? positionAt(streams, lap.startS + lap.durationS) : null
    return {
      number: i + 1,
      durationS: lap.durationS,
      distanceM: lap.distanceM,
      stillS: lap.movingS === null ? null : Math.max(0, lap.durationS - lap.movingS),
      straightM: from && to ? distanceM(from[0], from[1], to[0], to[1]) : null,
      miss: false,
    }
  })
  const judged = legs.filter((l) => l.straightM !== null && l.straightM >= minStraightM)
  if (judged.length < 3) return legs
  const typical = median(judged.map((l) => l.durationS / l.straightM!))
  for (const leg of judged) leg.miss = leg.durationS / leg.straightM! > typical * missFactor
  return legs
}

// Km time against the course length (the orienteering measure) and how much further than the course was run
export function courseStats(durationS: number, distanceM: number, courseLengthM: number) {
  return {
    kmTimeS: durationS / (courseLengthM / 1000),
    overdistance: distanceM / courseLengthM - 1,
  }
}

// Metres to send for the course length field (in km); only orienteering keeps a course length.
export function courseLengthM(sport: string, km: string) {
  return sport === 'orienteering' && km !== '' ? Number(km) * 1000 : null
}
