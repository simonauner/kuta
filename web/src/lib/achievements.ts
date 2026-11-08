import { queryOptions } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { api } from './api.ts'

export type AchievementKind =
  | 'firstDistance'
  | 'firstUltra'
  | 'runDistance'
  | 'yearRunDistance'
  | 'climb'
  | 'activityCount'
  | 'monthRunDistance'
  | 'weekStreak'
  | 'earlyBird'
  | 'nightOwl'

// Evaluated by the API from the own activities (Achievements.cs), newest first
export type Achievement = {
  key: string
  kind: AchievementKind
  threshold: number
  year: number | null
  achievedAt: string
  activityId: string
  seen: boolean
}

// Loaded once per app start and not refreshed with the activities, so a new achievement appears the next time the
// app is opened rather than in the middle of an upload.
export const achievementsQuery = (timeZone: string) =>
  queryOptions({
    queryKey: ['achievements', timeZone],
    queryFn: () => api.get<Achievement[]>(`/api/achievements?${new URLSearchParams({ timeZone })}`),
    staleTime: Infinity,
  })

// Thresholds with their own comparison; others get the generic text for the kind
const comparisons: Partial<Record<AchievementKind, number[]>> = {
  runDistance: [100_000, 470_000, 1_572_000, 10_000_000, 40_075_000],
  climb: [2_097, 4_806, 8_849, 17_698],
}

export function achievementText(a: Achievement, t: TFunction, locale?: string) {
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 })
  const values = {
    km: number.format(a.threshold / 1000),
    meters: number.format(a.threshold),
    count: a.threshold,
    year: a.year ?? undefined,
    distance:
      a.kind === 'firstDistance'
        ? t(`achievements.distances.${a.threshold as 5000 | 10000 | 21097 | 42195}`)
        : undefined,
  }
  const compared = comparisons[a.kind]?.includes(a.threshold)
  return {
    title: t(`achievements.kinds.${a.kind}.title`, values),
    detail: compared
      ? t(
          `achievements.comparisons.${a.kind}.${a.threshold}` as 'achievements.comparisons.climb.2097',
          values,
        )
      : t(`achievements.kinds.${a.kind}.detail`, values),
  }
}
