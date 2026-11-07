// GET /api/activities/year-review (YearReview.cs): one calendar year of own activities in the viewer's time zone.
export type YearReviewTotals = {
  count: number
  distanceM: number
  durationS: number
  elevationM: number
  activeDays: number
}

export type YearReviewActivity = {
  activityId: string
  sport: string
  title: string | null
  startedAt: string
  distanceM: number
  durationS: number
}

export type YearReview = {
  year: number
  // False while the year is still running
  complete: boolean
  summary: {
    totals: YearReviewTotals
    sports: { sportGroup: string; count: number; distanceM: number; durationS: number }[]
    months: { month: number; count: number; distanceM: number; durationS: number }[]
    // 1 = Monday
    favoriteWeekday: number | null
    favoriteWeekdayCount: number
    timeOfDay: 'morning' | 'midday' | 'afternoon' | 'evening' | 'night' | null
    timeOfDayCount: number
    longestDayStreak: number
    longestWeekStreak: number
    longestRun: YearReviewActivity | null
    longestActivity: YearReviewActivity | null
    races: YearReviewActivity[]
  }
  previous: YearReviewTotals | null
  records: {
    distanceM: number
    elapsedS: number
    activityId: string
    achievedAt: string
    personalBest: boolean
  }[]
  topEquipment: { id: string; name: string; count: number; distanceM: number } | null
}

// Largest landmark first: the comparison uses the biggest one the year reaches at least once.
const distances = [
  ['equator', 40_075_000],
  ['sweden', 1_572_000],
  ['stockholmGothenburg', 470_000],
  ['marathon', 42_195],
] as const

const heights = [
  ['everest', 8_849],
  ['kebnekaise', 2_097],
  ['eiffel', 330],
] as const

function compare<K extends string>(value: number, landmarks: readonly (readonly [K, number])[]) {
  const landmark = landmarks.find(([, size]) => value >= size)
  return landmark ? { key: landmark[0], times: value / landmark[1] } : null
}

export const distanceComparison = (meters: number) => compare(meters, distances)
export const elevationComparison = (meters: number) => compare(meters, heights)

// Whole percent against the previous year, null without a previous year to compare with
export function yearChange(current: number, previous: number | null | undefined) {
  return previous ? Math.round(((current - previous) / previous) * 100) : null
}
