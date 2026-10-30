import type { TFunction } from 'i18next'

// Som API:t: SI-enheter (meter, sekunder, m/s). Metriskt här; imperial blir ett val per användare senare.
export type Activity = {
  id: string
  sport: string
  title: string | null
  startedAt: string
  durationS: number
  distanceM: number
  avgHr: number | null
}

// En array per kanal, index = mätpunkt; null där klockan inte mätte.
export type ActivityStreams = {
  t: number[]
  dist: (number | null)[] | null
  hr: (number | null)[] | null
  speed: (number | null)[] | null
  alt: (number | null)[] | null
  lat: (number | null)[] | null
  lon: (number | null)[] | null
  // Steg/min (löpning, gång) eller varv/min (cykling); °C
  cadence: (number | null)[] | null
  temp: (number | null)[] | null
}

// Varv som klockan registrerade (manuellt eller autolap); startS = sekunder från passets start
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
  movingS: number | null
  maxHr: number | null
  avgSpeedMps: number | null
  elevationGainM: number | null
  device: string | null
  streams: ActivityStreams | null
  laps: ActivityLap[]
}

// Medel av de uppmätta värdena i en kanal (luckor räknas inte), avrundat
export function average(values: (number | null)[] | null) {
  const measured = values?.filter((v): v is number => v !== null) ?? []
  return measured.length ? Math.round(measured.reduce((a, b) => a + b, 0) / measured.length) : null
}

export function formatDuration(seconds: number) {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = Math.round(seconds % 60)
  const mmss = `${String(m).padStart(h ? 2 : 1, '0')}:${String(s).padStart(2, '0')}`
  return h ? `${h}:${mmss}` : mmss
}

// "1:02:30", "45:00" eller "45" (minuter) → sekunder; null om det inte går att tolka
export function parseDuration(text: string) {
  const parts = text.trim().split(':')
  if (parts.length > 3 || parts.some((p) => !/^\d+$/.test(p))) return null
  const numbers = parts.map(Number)
  if (numbers.slice(1).some((n) => n >= 60)) return null
  const seconds =
    parts.length === 1 ? numbers[0] * 60 : numbers.reduce((total, n) => total * 60 + n, 0)
  return seconds > 0 ? seconds : null
}

// Värdet för <input type="datetime-local">: lokal tid utan tidszon
export function toDateTimeLocal(date: Date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function formatPace(durationS: number, distanceM: number) {
  if (distanceM < 1) return '–'
  return `${formatDuration(Math.round(durationS / (distanceM / 1000)))} /km`
}

export const sports = ['running', 'cycling', 'walking', 'hiking'] as const

// Egen titel om löparen skrivit en, annars sport + tid på dygnet ("Löpning eftermiddag").
// Plats ("Löpning Uppsala") kommer när orten slås upp vid importen.
export function activityTitle(
  activity: Pick<Activity, 'title' | 'sport' | 'startedAt'>,
  t: TFunction,
) {
  if (activity.title) return activity.title
  const sport = (sports as readonly string[]).includes(activity.sport) ? activity.sport : 'other'
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
    sport: t(`activity.sport.${sport as (typeof sports)[number] | 'other'}`),
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

// Ett split per hel kilometer plus resten. Tiden mellan första mätpunkterna på vardera sidan om gränsen.
export function splits(streams: ActivityStreams): Split[] {
  const { t, dist, alt, hr } = streams
  if (!dist) return []
  const result: Split[] = []
  let from = 0
  let km = 1
  for (let i = 0; i < t.length; i++) {
    const d = dist[i]
    const last = i === t.length - 1
    if (d === null || (d < km * 1000 && !last)) continue
    const startD = dist[from] ?? 0
    const hrs = hr?.slice(from, i + 1).filter((v): v is number => v !== null) ?? []
    const altFrom = alt?.[from]
    const altTo = alt?.[i]
    result.push({
      km,
      distanceM: d - startD,
      durationS: t[i] - t[from],
      elevationM: altFrom != null && altTo != null ? altTo - altFrom : null,
      avgHr: hrs.length ? Math.round(hrs.reduce((a, b) => a + b, 0) / hrs.length) : null,
    })
    from = i
    km++
  }
  // En sista rest på några meter är brus, inte ett split
  return result.filter((s, i) => i < result.length - 1 || s.distanceM >= 50)
}
