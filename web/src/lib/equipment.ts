import { queryOptions } from '@tanstack/react-query'
import { api } from '@/lib/api'

export type EquipmentKind = keyof typeof import('@/locales/sv.json').equipmentKinds
export type Equipment = {
  id: string
  name: string
  brand: string | null
  kind: EquipmentKind
  retired: boolean
  initialDistanceM: number
  initialDurationS: number
  distanceM: number
  durationS: number
  distanceLimitM: number | null
}

// Active equipment that has reached the owner's replacement distance.
export const isWorn = (item: Equipment) =>
  !item.retired && item.distanceLimitM !== null && item.distanceM >= item.distanceLimitM

export const equipmentQuery = queryOptions({
  queryKey: ['equipment'],
  queryFn: () => api.get<Equipment[]>('/api/equipment'),
})

export const equipmentKindsQuery = {
  queryKey: ['equipmentKinds'],
  queryFn: () => api.get<EquipmentKind[]>('/api/equipment/kinds'),
  staleTime: Infinity,
}

export const lastUsedShoeQuery = queryOptions({
  queryKey: ['equipmentLastUsedShoe'],
  queryFn: () => api.get<{ id: string | null }>('/api/equipment/last-used-shoe'),
})
