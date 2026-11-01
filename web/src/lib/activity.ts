import type { ActivityPart } from './multisport.ts'
import type { TFunction } from 'i18next'
import sv from '../locales/sv.json' with { type: 'json' }
import { knownSport, isCyclingSport, sports, type Sport } from './sports.ts'

export type ActivityTag = keyof typeof import('@/locales/sv.json').tagNames

// Keep gaps as separate lines: joining visible points could draw through a hidden zone.
export function routeSegments(streams: ActivityStreams): [number, number][][] {
  const segments: [number, number][][] = []
  let segment: [number, number][] = []
  for (let i = 0; i < streams.t.length; i++) {
    const lat = streams.lat?.[i]
    const lon = streams.lon?.[i]
    if (lat != null && lon != null && Number.isFinite(lat) && Number.isFinite(lon)) {
      segment.push([lon, lat])
    } else if (segment.length) {
      segments.push(segment)
      segment = []
    }
  }
  if (segment.length) segments.push(segment)
  return segments
}

// Like the API: SI units (meters, seconds, m/s). Metric here; imperial becomes a per-user choice later.
export type Activity = {
  id: string
  sport: string
  title: string | null
  startedAt: string
  durationS: number
  distanceM: number
  avgHr: number | null
  equipmentIds: string[]
  tags: ActivityTag[]
  // "Garmin [watch model]" for activities from the Garmin API; Garmin requires it to be shown below the title
  attribution: string | null
  // Imported from Garmin and not edited yet (only in the own activity list)
  unreviewed?: boolean
}

export function activitySearch(search: Record<string, unknown>): {
  year?: number
  tag?: ActivityTag
  sport?: Sport
} {
  const value = search.year
  const year = typeof value === 'number' || typeof value === 'string' ? Number(value) : NaN
  const filters: { year?: number; tag?: ActivityTag; sport?: Sport } = {}
  if (Number.isInteger(year) && year >= 1 && year <= 9998) filters.year = year
  if (typeof search.tag === 'string' && Object.hasOwn(sv.tagNames, search.tag))
    filters.tag = search.tag as ActivityTag
  if (typeof search.sport === 'string' && (sports as readonly string[]).includes(search.sport))
    filters.sport = search.sport as Sport
  return filters
}

export type ActivityListItem = Activity & {
  showMonth: boolean
  showWeek: boolean
}
export type ActivityCursor = { startedAt: string; id: string }
export type ActivityPage = { items: ActivityListItem[]; nextCursor: ActivityCursor | null }

// ISO weeks start on Monday; the Thursday decides which week-year the week belongs to.
export function isoWeek(date: Date) {
  const thursday = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  thursday.setUTCDate(thursday.getUTCDate() + 4 - (thursday.getUTCDay() || 7))
  const yearStart = Date.UTC(thursday.getUTCFullYear(), 0, 1)
  return Math.ceil(((thursday.getTime() - yearStart) / 86400000 + 1) / 7)
}

type ActivityPeriod = {
  year: number | null
  month: Date | null
  week: number | null
  activities: Activity[]
}

export function groupActivities(activities: ActivityListItem[]): ActivityPeriod[] {
  const sorted = [...activities].sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt))
  const dated = sorted.map((activity) => {
    const date = new Date(activity.startedAt)
    const year = date.getFullYear()
    const month = `${year}-${date.getMonth()}`
    return { activity, date, year, month, week: isoWeek(date) }
  })
  const periods: ActivityPeriod[] = []
  for (const [index, item] of dated.entries()) {
    const previous = dated[index - 1]
    const newYear = item.year !== previous?.year
    const newMonth = item.month !== previous?.month
    const { showMonth, showWeek } = item.activity
    if (newMonth || (showWeek && item.week !== previous?.week)) {
      periods.push({
        year: newYear ? item.year : null,
        month: showMonth && newMonth ? item.date : null,
        week: showWeek ? item.week : null,
        activities: [],
      })
    }
    periods[periods.length - 1].activities.push(item.activity)
  }
  return periods
}

// One array per channel, index = sample; null where the watch didn't measure.
export type ActivityStreams = {
  t: number[]
  dist: (number | null)[] | null
  hr: (number | null)[] | null
  speed: (number | null)[] | null
  alt: (number | null)[] | null
  lat: (number | null)[] | null
  lon: (number | null)[] | null
  // Steps/min (running, walking) or rpm (cycling); °C
  cadence: (number | null)[] | null
  temp: (number | null)[] | null
}

export type ChartAxis = { values: number[]; elapsedTime: boolean }

export function chartAxis(streams: ActivityStreams): ChartAxis {
  let last = streams.dist?.find((d) => d !== null) ?? 0
  const values = (streams.dist ?? []).map((d) => (last = d ?? last) / 1000)
  const increasing = values.some((d) => d > values[0])
  const invalid = values.some((d, i) => !Number.isFinite(d) || (i > 0 && d < values[i - 1]))
  return increasing && !invalid
    ? { values, elapsedTime: false }
    : { values: streams.t, elapsedTime: true }
}

// Laps the watch recorded (manual or autolap); startS = seconds from the activity start
export type ActivityLap = {
  lapIndex: number
  startS: number
  durationS: number
  movingS: number | null
  distanceM: number
  avgHr: number | null
  maxHr: number | null
  ascentM: number | null
  descentM: number | null
  avgCadence: number | null
}

export type ActivityDetail = Activity & {
  userId: string
  displayName: string
  privacy: 'private' | 'followers' | 'public'
  excludeFromRecords: boolean
  description: string | null
  movingS: number | null
  maxHr: number | null
  avgSpeedMps: number | null
  elevationGainM: number | null
  device: string | null
  streams: ActivityStreams | null
  originalParts: ActivityPart[]
  originalSport: string
  parts: ActivityPart[]
  partsEdited: boolean
  canSplit: boolean
  laps: ActivityLap[]
}

// Mean of the measured values in a channel (gaps don't count), rounded
export function average(values: (number | null)[] | null) {
  const measured = values?.filter((v): v is number => v !== null) ?? []
  return measured.length ? Math.round(measured.reduce((a, b) => a + b, 0) / measured.length) : null
}

export function formatDuration(seconds: number) {
  seconds = Math.round(seconds)
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  const mmss = `${String(m).padStart(h ? 2 : 1, '0')}:${String(s).padStart(2, '0')}`
  return h ? `${h}:${mmss}` : mmss
}

// "1:02:30", "45:00" or "45" (minutes) → seconds; null if it can't be parsed
export function parseDuration(text: string) {
  const parts = text.trim().split(':')
  if (parts.length > 3 || parts.some((p) => !/^\d+$/.test(p))) return null
  const numbers = parts.map(Number)
  if (numbers.slice(1).some((n) => n >= 60)) return null
  const seconds =
    parts.length === 1 ? numbers[0] * 60 : numbers.reduce((total, n) => total * 60 + n, 0)
  return seconds > 0 ? seconds : null
}

// The value for <input type="datetime-local">: local time without time zone
export function toDateTimeLocal(date: Date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function formatPace(durationS: number, distanceM: number) {
  if (distanceM < 1) return '–'
  return `${formatDuration(Math.round(durationS / (distanceM / 1000)))} /km`
}

export function formatSportPace(
  sport: string,
  durationS: number,
  distanceM: number,
  locale?: string,
) {
  if (
    ['triathlon', 'swimrun', 'multisport', 'transition'].includes(sport) ||
    distanceM < 1 ||
    durationS <= 0
  )
    return '–'
  if (isCyclingSport(sport))
    return `${((distanceM / durationS) * 3.6).toLocaleString(locale, { maximumFractionDigits: 1 })} km/h`
  if (sport === 'swimming')
    return `${formatDuration(Math.round(durationS / (distanceM / 100)))} /100 m`
  return formatPace(durationS, distanceM)
}

// Own title if the runner wrote one, otherwise sport + time of day ("Löpning eftermiddag").
// Place ("Löpning Uppsala") comes when the town is looked up at import.
export function activityTitle(
  activity: Pick<Activity, 'title' | 'sport' | 'startedAt'>,
  t: TFunction,
) {
  if (activity.title) return activity.title
  const sport = knownSport(activity.sport)
  const hour = new Date(activity.startedAt).getHours()
  const timeOfDay =
    hour < 5
      ? 'night'
      : hour < 10
        ? 'morning'
        : hour < 12
          ? 'forenoon'
          : hour < 17
            ? 'afternoon'
            : hour < 22
              ? 'evening'
              : 'night'
  return t('activity.defaultTitle', {
    sport: t(`newActivity.sports.${sport}`),
    timeOfDay: t(`activity.timeOfDay.${timeOfDay}`),
  })
}

export type Split = {
  km: number
  distanceM: number
  durationS: number
  elevationM: number | null
  avgHr: number | null
}

// Interpolate kilometer boundaries between samples, without extrapolating outside the recording.
export function splits(streams: ActivityStreams): Split[] {
  const { t, dist, alt, hr } = streams
  if (!dist) return []
  const first = dist.findIndex((d) => d !== null)
  if (first < 0) return []
  const result: Split[] = []
  let previous = first
  let fromD = dist[first]!
  let fromT = t[first]
  let fromAlt = alt?.[first] ?? null
  let km = Math.floor(fromD / 1000) + 1
  let hrTotal = 0
  let hrSeconds = 0
  const interpolate = (
    a: number | null | undefined,
    b: number | null | undefined,
    ratio: number,
  ) => (a == null || b == null ? null : a + (b - a) * ratio)
  function finish(d: number, time: number, elevation: number | null) {
    result.push({
      km,
      distanceM: d - fromD,
      durationS: time - fromT,
      elevationM: fromAlt !== null && elevation !== null ? elevation - fromAlt : null,
      avgHr: hrSeconds > 0 ? Math.round(hrTotal / hrSeconds) : null,
    })
    fromD = d
    fromT = time
    fromAlt = elevation
    hrTotal = hrSeconds = 0
    km++
  }
  for (let i = first; i < t.length; i++) {
    const d = dist[i]
    if (d === null) continue
    // A reset or broken stream has no unambiguous kilometer split.
    if (!Number.isFinite(d) || d < 0 || !Number.isFinite(t[i])) return []
    if (i === first) continue
    const previousD = dist[previous]!
    if (d < previousD || t[i] <= t[previous]) return []
    let fromRatio = 0
    while (fromRatio < 1) {
      const boundary = Math.min(km * 1000, d)
      const ratio = d === previousD ? 1 : (boundary - previousD) / (d - previousD)
      const seconds = (ratio - fromRatio) * (t[i] - t[previous])
      const hrFrom = interpolate(hr?.[previous], hr?.[i], fromRatio)
      const hrTo = interpolate(hr?.[previous], hr?.[i], ratio)
      if (hrFrom !== null && hrTo !== null) {
        hrTotal += ((hrFrom + hrTo) / 2) * seconds
        hrSeconds += seconds
      }
      if (boundary === km * 1000)
        finish(
          boundary,
          t[previous] + ratio * (t[i] - t[previous]),
          interpolate(alt?.[previous], alt?.[i], ratio),
        )
      fromRatio = ratio
    }
    previous = i
  }
  // A final remainder of a few meters is noise, not a split.
  if (dist[previous]! - fromD >= 50) finish(dist[previous]!, t[previous], alt?.[previous] ?? null)
  return result
}
