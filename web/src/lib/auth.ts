import { queryOptions, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, resetCsrfToken } from '@/lib/api'

export type SupporterTier = 'bronze' | 'silver' | 'gold'

// hrConsent: uttryckligt samtycke till att puls (hälsodata) sparas
export type Me = {
  id: string
  email: string
  displayName: string | null
  hrConsent: boolean
  defaultActivityPrivacy: 'private' | 'followers' | 'public'
  allActivitiesMatchDefaultPrivacy: boolean
  // tier är null när supporterskapet saknas eller har gått ut
  supporter: {
    tier: SupporterTier | null
    until: string | null
    badgePrivacy: 'private' | 'followers' | 'public'
  }
  // Styr Garmins märkning av vyer som härleds ur passen (statistik, rekord, mål)
  hasGarminData: boolean
}

// null = inte inloggad
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

// Efter in- och utloggning: ny CSRF-token och inga cachade data från förra användaren
export function useSessionChanged() {
  const queryClient = useQueryClient()
  return () => {
    resetCsrfToken()
    return queryClient.resetQueries()
  }
}
