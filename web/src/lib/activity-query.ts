import { queryOptions, type QueryClient } from '@tanstack/react-query'
import { ApiError, api } from './api.ts'
import type { ActivityDetail, ActivityTag } from './activity.ts'
import type { SportGroup } from './sport-groups.ts'
import type { GoalPrivacy } from './goals.ts'

export async function clearHeartRateQueries(queryClient: QueryClient) {
  const filter = {
    predicate: (query: { queryKey: readonly unknown[] }) =>
      ['activities', 'activity', 'activityPreview'].includes(String(query.queryKey[0])),
  }
  // Also stop late responses from putting deleted heart rate back.
  await queryClient.cancelQueries(filter)
  queryClient.removeQueries(filter)
}

export const activityQuery = (activityId: string) =>
  queryOptions({
    queryKey: ['activity', activityId],
    queryFn: () => api.get<ActivityDetail>(`/api/activities/${activityId}`),
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  })

export const activityTagsQuery = {
  queryKey: ['activityTags'],
  queryFn: () => api.get<ActivityTag[]>('/api/activity-tags'),
  staleTime: Infinity,
}

export const sportGroupsQuery = {
  queryKey: ['sportGroups'],
  queryFn: () => api.get<SportGroup[]>('/api/activities/sport-groups'),
  staleTime: Infinity,
}

export const activityYearsQuery = (timeZone: string) =>
  queryOptions({
    queryKey: ['activities', 'years', timeZone],
    queryFn: () => api.get<number[]>(`/api/activities/years?${new URLSearchParams({ timeZone })}`),
    staleTime: 60_000,
  })

const isGoalQuery = (key: readonly unknown[]) =>
  (key[0] === 'activities' && key[1] === 'goals') || (key[0] === 'runner' && key[2] === 'goals')

// Visibilities among own activities; key under 'activities' so it refreshes with the activities.
export const activityPrivaciesQuery = {
  queryKey: ['activities', 'privacies'],
  queryFn: () => api.get<GoalPrivacy[]>('/api/activities/privacies'),
}

export function refreshGoalQueries(queryClient: QueryClient) {
  return queryClient.invalidateQueries({ predicate: ({ queryKey }) => isGoalQuery(queryKey) })
}

export function refreshActivityQueries(queryClient: QueryClient) {
  return queryClient.invalidateQueries({
    predicate: ({ queryKey }) =>
      [
        'me',
        'activities',
        'activity',
        'records',
        'feed',
        'equipment',
        'equipmentLastUsedShoe',
      ].includes(String(queryKey[0])) || isGoalQuery(queryKey),
  })
}
