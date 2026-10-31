export type DailyTotal = { day: string; count: number; distanceM: number; durationS: number }

export const yearMetrics = ['distanceM', 'durationS', 'count'] as const
export type YearMetric = (typeof yearMetrics)[number]

export type LocalDate = { year: number; month: number; day: number }

export type YearSeries = {
  year: number
  // Ackumulerat värde vid slutet av varje dag med pass, med dag 0 = 0 först och årets sista (eller dagens) dag sist.
  points: { day: number; value: number }[]
  total: number
  // Ackumulerat till och med samma månad och dag som idag, så att skottår inte förskjuter jämförelsen.
  toDate: number
}

export function dayOfYear({ year, month, day }: LocalDate) {
  return (Date.UTC(year, month - 1, day) - Date.UTC(year, 0, 1)) / 86_400_000 + 1
}

export function daysInYear(year: number) {
  return dayOfYear({ year, month: 12, day: 31 })
}

function parseDay(text: string): LocalDate {
  const [year, month, day] = text.split('-').map(Number)
  return { year, month, day }
}

// Bygger en ackumulerad serie per år (nyast först) ur dagssummorna. Innevarande år slutar idag.
export function yearComparison(days: DailyTotal[], metric: YearMetric, today: LocalDate) {
  const byYear = new Map<number, { date: LocalDate; value: number }[]>()
  for (const total of days) {
    const date = parseDay(total.day)
    const entries = byYear.get(date.year) ?? []
    entries.push({ date, value: total[metric] })
    byYear.set(date.year, entries)
  }
  const sameDateOrEarlier = (d: LocalDate) =>
    d.month < today.month || (d.month === today.month && d.day <= today.day)
  return [...byYear.entries()]
    .sort(([a], [b]) => b - a)
    .map(([year, entries]): YearSeries => {
      entries.sort((a, b) => dayOfYear(a.date) - dayOfYear(b.date))
      let total = 0
      let toDate = 0
      const points = [{ day: 0, value: 0 }]
      for (const { date, value } of entries) {
        total += value
        if (sameDateOrEarlier(date)) toDate += value
        points.push({ day: dayOfYear(date), value: total })
      }
      const end = year === today.year ? dayOfYear(today) : daysInYear(year)
      if (points[points.length - 1].day < end) points.push({ day: end, value: total })
      return { year, points, total, toDate }
    })
}

// Förändring mot ett jämförelsevärde; null när jämförelsen saknar underlag.
export function relativeChange(value: number, previous: number | undefined) {
  if (previous === undefined || previous <= 0) return null
  return (value - previous) / previous
}

// Flyttar isär etiketter (y i diagrammets koordinater) så att de har minst `gap` mellan sig och stannar inom [min, max].
export function spreadLabels(ys: number[], gap: number, min: number, max: number) {
  const order = ys.map((y, index) => ({ y, index })).sort((a, b) => a.y - b.y)
  for (let i = 0; i < order.length; i++) {
    order[i].y = Math.max(order[i].y, i === 0 ? min : order[i - 1].y + gap)
  }
  for (let i = order.length - 1; i >= 0; i--) {
    order[i].y = Math.min(order[i].y, i === order.length - 1 ? max : order[i + 1].y - gap)
  }
  const result = new Array<number>(ys.length)
  for (const { y, index } of order) result[index] = y
  return result
}
