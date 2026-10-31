import assert from 'node:assert/strict'
import { test } from 'node:test'
import { QueryClient } from '@tanstack/react-query'
import { clearHeartRateQueries } from '../src/lib/activity-query.ts'

test('revoking consent clears heart-rate caches and discards late responses', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000 } } })
  const keys = [
    ['activities', 'list'],
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
