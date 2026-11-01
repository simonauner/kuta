import { parseDuration, type ActivityDetail, type ActivityStreams } from './activity.ts'

export type ActivityPart = {
  partIndex: number
  sport: string
  startS: number
  endS: number
  distanceM: number
  useEquipment: boolean
}
export type MultisportDraft = { kind: string; parts: ActivityPart[] } | null | undefined

export function multisportView<
  T extends Pick<
    ActivityDetail,
    'sport' | 'parts' | 'partsEdited' | 'originalParts' | 'originalSport'
  >,
>(activity: T, draft: MultisportDraft): T {
  if (draft === undefined) return activity
  return {
    ...activity,
    sport: draft?.kind ?? activity.originalSport,
    parts: draft?.parts ?? activity.originalParts,
    partsEdited: draft !== null,
  }
}

export function partsFromForm(draft: MultisportDraft, form: HTMLFormElement): MultisportDraft {
  if (!draft) return draft
  const values = new FormData(form)
  // The boundaries are typed as time from the start (h:mm:ss)
  const ends = draft.parts.map(
    (p, i) => parseDuration(String(values.get(`endS-${i}`) ?? '')) ?? p.endS,
  )
  return {
    ...draft,
    parts: draft.parts.map((p, i) => ({ ...p, startS: i ? ends[i - 1] : p.startS, endS: ends[i] })),
  }
}

export const multisportKinds = ['triathlon', 'swimrun', 'multisport'] as const
export const partSports = [
  'running',
  'swimming',
  'cycling',
  'walking',
  'hiking',
  'transition',
  'other',
] as const
export type PartSport = (typeof partSports)[number]
export const sportColors: Record<string, string> = {
  running: '#e8590c',
  swimming: '#1c7ed6',
  cycling: '#2f9e44',
  transition: '#868e96',
}
export function sportNameKey(sport: string) {
  const known = [...partSports, ...multisportKinds].find((s) => s === sport) ?? 'other'
  return `activity.sport.${known}` as const
}

// Include interpolated endpoints so adjacent legs meet at exactly the same time and position.
export function partStreams(
  streams: ActivityStreams,
  part: Pick<ActivityPart, 'startS' | 'endS'>,
): ActivityStreams {
  const t = streams.t
  const times = [part.startS, ...t.filter((v) => v > part.startS && v < part.endS), part.endS]
  const indices = times.map((time) => {
    let low = 0,
      high = t.length
    while (low < high) {
      const mid = (low + high) >>> 1
      if (t[mid] < time) low = mid + 1
      else high = mid
    }
    return low
  })
  function channel(values: (number | null)[] | null) {
    if (!values) return null
    return times.map((time, j) => {
      const i = indices[j]
      if (t[i] === time) return values[i]
      if (i === 0 || i === t.length || values[i - 1] == null || values[i] == null) return null
      return (
        values[i - 1]! + ((values[i]! - values[i - 1]!) * (time - t[i - 1])) / (t[i] - t[i - 1])
      )
    })
  }
  const dist = channel(streams.dist)
  const first = dist?.find((v) => v !== null) ?? 0
  return {
    t: times.map((v) => v - part.startS),
    dist: dist?.map((v) => (v === null ? null : v - first)) ?? null,
    hr: channel(streams.hr),
    speed: channel(streams.speed),
    alt: channel(streams.alt),
    lat: channel(streams.lat),
    lon: channel(streams.lon),
    cadence: channel(streams.cadence),
    temp: channel(streams.temp),
  }
}
