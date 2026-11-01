import { queryOptions, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, resetCsrfToken } from '@/lib/api'

export type SupporterTier = 'bronze' | 'silver' | 'gold'

// hrConsent: explicit consent to storing heart rate (health data)
export type Me = {
  id: string
  email: string
  displayName: string | null
  hrConsent: boolean
  defaultActivityPrivacy: 'private' | 'followers' | 'public'
  allActivitiesMatchDefaultPrivacy: boolean
  // tier is null when there is no supportership or it has expired
  supporter: {
    tier: SupporterTier | null
    until: string | null
    badgePrivacy: 'private' | 'followers' | 'public'
  }
  // Controls Garmin attribution on views derived from activities (statistics, records, goals)
  hasGarminData: boolean
}

// null = not signed in
export const meQuery = queryOptions({
  queryKey: ['me'],
  queryFn: async () => {
    try {
      return await api.get<Me>('/api/me')
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return null
      throw e
    }
  },
})

// After sign-in and sign-out: new CSRF token and no cached data from the previous user
export function useSessionChanged() {
  const queryClient = useQueryClient()
  return () => {
    resetCsrfToken()
    return queryClient.resetQueries()
  }
}
