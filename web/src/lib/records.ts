export type StoredBestEffort = {
  activityId: string
  distanceM: number
  elapsedS: number
  startOffsetS: number
  achievedAt: string
}

export type RecordMonth = {
  year: number
  month: number
  activityId: string
  elapsedS: number
  achievedAt: string
}

export type RecordTrend = { months: RecordMonth[]; progression: StoredBestEffort[] }

export type RacePredictions = {
  basis: StoredBestEffort | null
  vdot: number | null
  predictions: { distanceM: number; elapsedS: number }[]
}

// Etikett (API:ts distanceM) → exakt längd, som rekordmotorn räknar med.
export const recordDistances = [
  [1000, 1000],
  [1609, 1609.344],
  [5000, 5000],
  [10000, 10000],
  [21097, 21097.5],
  [42195, 42195],
] as const

export type RecordDistance = (typeof recordDistances)[number][0]

export function recordMeters(distanceM: number) {
  return recordDistances.find(([label]) => label === distanceM)?.[1] ?? distanceM
}

export type TrendPoint = {
  x: number
  y: number
  elapsedS: number
  activityId: string
  achievedAt: string
}

// Snabbast överst. x följer tiden så att glesa perioder syns som luckor; en ensam punkt hamnar i mitten.
export function trendChart(
  trend: RecordTrend,
  { width, height, padding }: { width: number; height: number; padding: number },
) {
  const all = [...trend.months, ...trend.progression]
  if (all.length === 0) return null
  const times = all.map((p) => Date.parse(p.achievedAt))
  const firstTime = Math.min(...times)
  const lastTime = Math.max(...times)
  const fastest = Math.min(...all.map((p) => p.elapsedS))
  const slowest = Math.max(...all.map((p) => p.elapsedS))
  const x = (time: number) =>
    lastTime === firstTime
      ? width / 2
      : padding + ((time - firstTime) / (lastTime - firstTime)) * (width - 2 * padding)
  const y = (elapsedS: number) =>
    slowest === fastest
      ? height / 2
      : padding + ((elapsedS - fastest) / (slowest - fastest)) * (height - 2 * padding)
  const point = (p: { elapsedS: number; activityId: string; achievedAt: string }): TrendPoint => ({
    x: x(Date.parse(p.achievedAt)),
    y: y(p.elapsedS),
    elapsedS: p.elapsedS,
    activityId: p.activityId,
    achievedAt: p.achievedAt,
  })
  const records = trend.progression.map(point)
  // Trappsteg: ett rekord gäller tills nästa slår det, och det senaste till diagrammets slut.
  const steps = records
    .flatMap((p, i) => {
      const nextX = i + 1 < records.length ? records[i + 1].x : x(lastTime)
      return [`${p.x},${p.y}`, `${nextX},${p.y}`]
    })
    .join(' ')
  return {
    months: trend.months.map(point),
    records,
    steps,
    fastest,
    slowest,
    first: new Date(firstTime),
    last: new Date(lastTime),
  }
}
