export type CalendarDay = { day: string; count: number; distanceM: number }
export type CalendarCell = CalendarDay & { level: 0 | 1 | 2 | 3 | 4 }

const dayMs = 86_400_000
const isoDay = (ms: number) => new Date(ms).toISOString().slice(0, 10)
// Day number since the epoch for a yyyy-mm-dd string, so calendar arithmetic ignores time zones.
const dayNumber = (day: string) => Date.parse(`${day}T00:00:00Z`) / dayMs

function level(day: CalendarDay | undefined): CalendarCell['level'] {
  if (!day || day.count === 0) return 0
  if (day.distanceM < 5_000) return 1
  if (day.distanceM < 10_000) return 2
  if (day.distanceM < 20_000) return 3
  return 4
}

// Weeks (Monday first) up to and including the week of today; days after today are left out.
export function calendarWeeks(days: CalendarDay[], today: string, weeks = 53): CalendarCell[][] {
  const byDay = new Map(days.map((d) => [d.day, d]))
  const todayNumber = dayNumber(today)
  // 1970-01-01 was a Thursday, so Monday is weekday 0 after shifting by 3.
  const monday = todayNumber - ((todayNumber + 3) % 7)
  return Array.from({ length: weeks }, (_, w) => {
    const start = monday - (weeks - 1 - w) * 7
    return Array.from({ length: 7 }, (_, d) => start + d)
      .filter((n) => n <= todayNumber)
      .map((n) => {
        const day = isoDay(n * dayMs)
        const found = byDay.get(day)
        return {
          day,
          count: found?.count ?? 0,
          distanceM: found?.distanceM ?? 0,
          level: level(found),
        }
      })
  })
}

// Consecutive weeks with at least one activity. The current week counts once it has one, but an empty
// current week does not break the streak since it is not over yet.
export function weekStreaks(weeks: CalendarCell[][]) {
  const active = weeks.map((week) => week.some((d) => d.count > 0))
  let longest = 0
  let run = 0
  for (const a of active) {
    run = a ? run + 1 : 0
    longest = Math.max(longest, run)
  }
  let current = 0
  for (let i = active.length - (active.at(-1) ? 1 : 2); i >= 0 && active[i]; i--) current++
  return { current, longest }
}
