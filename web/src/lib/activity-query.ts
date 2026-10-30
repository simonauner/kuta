import { queryOptions } from '@tanstack/react-query'
import { ApiError, api } from '@/lib/api'
import type { ActivityDetail } from '@/lib/activity'

export const activityQuery = (activityId: string) =>
  queryOptions({
    queryKey: ['activity', activityId],
    queryFn: () => api.get<ActivityDetail>(`/api/activities/${activityId}`),
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  })
