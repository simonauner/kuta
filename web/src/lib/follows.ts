import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { SupporterTier } from '@/lib/auth'

export type Runner = {
  id: string
  displayName: string
  status: 'pending' | 'accepted' | 'blocked'
  supporterTier: SupporterTier | null
  // Only for incoming requests: names are free, so the account's age helps spot impersonation.
  joinedAt: string | null
}

export type FollowLists = { following: Runner[]; followers: Runner[]; blocked: Runner[] }

// Polled so that a new follow request shows up on the Feed tab without a reload.
export const followsQuery = queryOptions({
  queryKey: ['follows'],
  queryFn: () => api.get<FollowLists>('/api/follows'),
  refetchInterval: 60_000,
})

export const pendingRequests = (lists: FollowLists | undefined) =>
  lists?.followers.filter((runner) => runner.status === 'pending') ?? []

// Approve, decline, remove, unfollow and block share one mutation, so the buttons lock together while one runs.
export function useFollowChange() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ method, path }: { method: 'POST' | 'PUT' | 'DELETE'; path: string }) =>
      api.send(method, path),
    onSuccess: async () => {
      await Promise.all(
        ['follows', 'runner', 'feed', 'activity'].map((key) =>
          queryClient.invalidateQueries({ queryKey: [key] }),
        ),
      )
    },
  })
}
