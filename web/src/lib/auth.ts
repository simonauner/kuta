import { queryOptions, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, resetCsrfToken } from '@/lib/api'

// hrConsent: uttryckligt samtycke till att puls (hälsodata) sparas
export type Me = { id: string; email: string; displayName: string | null; hrConsent: boolean }

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
