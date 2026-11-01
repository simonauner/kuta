export type StoredBestEffort = {
  activityId: string
  distanceM: number
  elapsedS: number
  startOffsetS: number
  achievedAt: string
}

export type ActivityBestEffort = StoredBestEffort & {
  recordStatus: 'current' | 'previous' | null
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

// Label (the API's distanceM) → exact length, as the record engine uses.
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

// Fastest on top. x follows time so sparse periods show as gaps; a lone point lands in the middle.
export function trendChart(
  trend: RecordTrend,
  { width, height, padding }: { width: number; height: number; padding: number },
) {
  const all = [...trend.months, ...trend.progression]
  if (all.length === 0) return null
  const times = all.map((p) => Date.parse(p.achievedAt))
  const firstTime = Math.min(...times)
  const lastTime = Math.max(...times)
  // Monthly results must not squash the record progression.
  const scale = trend.progression.length > 0 ? trend.progression : trend.months
  const fastest = Math.min(...scale.map((p) => p.elapsedS))
  const slowest = Math.max(...scale.map((p) => p.elapsedS))
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
  // Steps: a record holds until the next beats it, and the latest until the chart's end.
  const steps = records
    .flatMap((p, i) => {
      const nextX = i + 1 < records.length ? records[i + 1].x : x(lastTime)
      return [`${p.x},${p.y}`, `${nextX},${p.y}`]
    })
    .join(' ')
  return {
    months: trend.months.filter((p) => p.elapsedS >= fastest && p.elapsedS <= slowest).map(point),
    records,
    steps,
    fastest,
    slowest,
    first: new Date(firstTime),
    last: new Date(lastTime),
  }
}
