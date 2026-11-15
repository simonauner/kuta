import { queryOptions } from '@tanstack/react-query'
import { api } from './api.ts'
import type { GoalMetric } from './goals.ts'

// From /api/groups. Membership gives no access to activities: the leaderboard only has the weekly totals that members
// chose to share with the group.
export type GroupRole = 'admin' | 'member'
export type GroupSummary = {
  id: string
  name: string
  role: GroupRole
  status: 'member' | 'pending'
  memberCount: number
}
export type GroupMember = {
  id: string
  displayName: string
  role: GroupRole
  avatarId: string | null
  previousName: string | null
  // Only for requests, as for follow requests: they help an admin spot impersonation.
  joinedAt: string | null
  similarTo: string | null
}
export type GroupDetail = {
  id: string
  name: string
  timeZone: string
  inviteCode: string
  role: GroupRole
  shareTotals: boolean
  members: GroupMember[]
  requests: GroupMember[]
}
export type GroupInvite = {
  id: string
  name: string
  memberCount: number
  status: 'none' | 'pending' | 'member'
}
export type Leaderboard = {
  weekStart: string
  rows: { userId: string; displayName: string; avatarId: string | null; value: number }[]
}

export const groupsQuery = queryOptions({
  queryKey: ['groups'],
  queryFn: () => api.get<GroupSummary[]>('/api/groups'),
})

export const groupQuery = (id: string) =>
  queryOptions({
    queryKey: ['groups', id],
    queryFn: () => api.get<GroupDetail>(`/api/groups/${id}`),
    retry: false,
  })

export const leaderboardQuery = (
  id: string,
  sportGroup: string,
  metric: GoalMetric,
  week: string | undefined,
) =>
  queryOptions({
    queryKey: ['groups', id, 'leaderboard', sportGroup, metric, week],
    queryFn: () => {
      const params = new URLSearchParams({ metric })
      if (sportGroup) params.set('sportGroup', sportGroup)
      if (week) params.set('week', week)
      return api.get<Leaderboard>(`/api/groups/${id}/leaderboard?${params}`)
    },
  })

export const inviteLink = (code: string, origin = window.location.origin) =>
  new URL(`/app/g/${code}`, origin).href

// Monday of the week before or after, as yyyy-mm-dd (the API counts weeks in the group's time zone).
export function shiftWeek(weekStart: string, weeks: number) {
  const date = new Date(`${weekStart}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + 7 * weeks)
  return date.toISOString().slice(0, 10)
}
