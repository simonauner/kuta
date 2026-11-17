import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query'
import { api } from './api.ts'
import type { Goal, GoalMetric } from './goals.ts'

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
export type GroupVisibility = 'private' | 'open'
export type GroupDetail = {
  id: string
  name: string
  // Open clubs are searchable by name, place and description; members still need approval and stay hidden.
  visibility: GroupVisibility
  description: string | null
  place: string | null
  sportGroup: string | null
  timeZone: string
  inviteCode: string
  role: GroupRole
  shareTotals: boolean
  // The members themselves come a page at a time (groupMembersQuery): a club can have up to 1000.
  memberCount: number
  requests: GroupMember[]
  // Shared goals, oldest first, counted like the leaderboard, with the viewer's own part of the current period.
  goals: { progress: Goal; ownValue: number }[]
}
export type GroupInvite = {
  id: string
  name: string
  memberCount: number
  status: 'none' | 'pending' | 'member'
}
export type ClubSummary = {
  id: string
  name: string
  place: string | null
  sportGroup: string | null
  description: string | null
  memberCount: number
  createdAt: string
  status: 'none' | 'pending' | 'member'
}
export type Leaderboard = {
  weekStart: string
  rows: { userId: string; displayName: string; avatarId: string | null; value: number }[]
  // The totals include Garmin activities, so the leaderboard needs Garmin's attribution.
  garminData: boolean
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

const memberPageSize = 50

export const groupMembersQuery = (id: string) =>
  infiniteQueryOptions({
    queryKey: ['groups', id, 'members'],
    initialPageParam: 0,
    queryFn: ({ pageParam }) =>
      api.get<GroupMember[]>(`/api/groups/${id}/members?offset=${pageParam}`),
    // shortcut: a full last page costs one extra request that comes back empty
    getNextPageParam: (page, pages) =>
      page.length === memberPageSize ? pages.length * memberPageSize : undefined,
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

// Searches only on submit: the API limits searches per hour.
export const clubSearchQuery = (query: string) =>
  queryOptions({
    queryKey: ['clubs', 'search', query],
    queryFn: () => api.get<ClubSummary[]>(`/api/clubs?query=${encodeURIComponent(query)}`),
    enabled: query.length > 0,
    staleTime: 60_000,
  })

export const inviteLink = (code: string, origin = window.location.origin) =>
  new URL(`/app/g/${code}`, origin).href

// Monday of the week before or after, as yyyy-mm-dd (the API counts weeks in the group's time zone).
export function shiftWeek(weekStart: string, weeks: number) {
  const date = new Date(`${weekStart}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + 7 * weeks)
  return date.toISOString().slice(0, 10)
}
