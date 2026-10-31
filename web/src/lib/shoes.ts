import { queryOptions } from '@tanstack/react-query'
import { api } from './api'

export type Shoe = { id: string; name: string; brand: string | null; retired: boolean }
export const shoesQuery = queryOptions({
  queryKey: ['shoes'],
  queryFn: () => api.get<{ shoes: Shoe[]; lastUsedShoeId: string | null }>('/api/shoes'),
})
