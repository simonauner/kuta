import assert from 'node:assert/strict'
import { test } from 'node:test'
import { QueryClient } from '@tanstack/react-query'
import {
  clearHeartRateQueries,
  refreshActivityQueries,
  refreshGoalQueries,
} from '../src/lib/activity-query.ts'

test('revoking consent clears heart-rate caches and discards late responses', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000 } } })
  const keys = [
    ['activities', 'list'],
    ['activities', 'statistics', 'Europe/Stockholm', 2026, 'running'],
    ['activity', 'run'],
    ['activityPreview', 'file'],
  ]
  for (const key of keys) client.setQueryData(key, { avgHr: 150 })
  client.setQueryData(['equipment'], ['shoe'])
  let finish
  const pending = client
    .fetchQuery({
      queryKey: ['activity', 'late'],
      queryFn: () => new Promise((resolve) => (finish = resolve)),
    })
    .catch(() => {})

  await clearHeartRateQueries(client)
  finish({ avgHr: 160 })
  await pending

  for (const key of [...keys, ['activity', 'late']])
    assert.equal(client.getQueryData(key), undefined)
  assert.deepEqual(client.getQueryData(['equipment']), ['shoe'])
  client.clear()
})

test('activity changes refresh lists, details, summaries and profile goals', async () => {
  const client = new QueryClient()
  const affected = [
    ['me'],
    ['activities', 'list'],
    ['activities', 'statistics'],
    ['activities', 'training-load'],
    ['activities', 'goals'],
    ['activity', 'run'],
    ['records'],
    ['feed'],
    ['feed', 'owner'],
    ['runner', 'owner', 'goals', 'UTC'],
    ['equipment'],
    ['equipmentLastUsedShoe'],
  ]
  for (const key of [...affected, ['sportGroups'], ['runner', 'owner']])
    client.setQueryData(key, {})
  await refreshActivityQueries(client)
  for (const key of affected)
    assert.equal(client.getQueryState(key).isInvalidated, true, String(key))
  assert.equal(client.getQueryState(['sportGroups']).isInvalidated, false)
  assert.equal(client.getQueryState(['runner', 'owner']).isInvalidated, false)
  client.clear()
})

test('goal changes refresh both own and profile goals without refreshing activity lists', async () => {
  const client = new QueryClient()
  const keys = [
    ['activities', 'goals', 'UTC'],
    ['runner', 'owner', 'goals', 'UTC'],
  ]
  for (const key of [...keys, ['activities', 'list']]) client.setQueryData(key, {})
  await refreshGoalQueries(client)
  for (const key of keys) assert.equal(client.getQueryState(key).isInvalidated, true)
  assert.equal(client.getQueryState(['activities', 'list']).isInvalidated, false)
  client.clear()
})
