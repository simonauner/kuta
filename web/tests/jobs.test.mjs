import assert from 'node:assert/strict'
import { test } from 'node:test'
import { isActive, jobFraction, latestJob, latestJobsQuery } from '../src/lib/jobs.ts'

test('jobs are polled only while one of them is queued or running', () => {
  const job = (kind, status) => ({ kind, status })
  const interval = (data) => latestJobsQuery.refetchInterval({ state: { data } })
  assert.equal(interval([job('strava_import', 'succeeded'), job('recalculate', 'running')]), 1000)
  assert.equal(interval([job('strava_import', 'queued')]), 1000)
  assert.equal(interval([job('strava_import', 'failed')]), false)
  assert.equal(interval(undefined), false)
  assert.equal(isActive(null), false)
  assert.equal(latestJob([job('recalculate', 'queued')], 'strava_import'), undefined)
})

test('progress fraction is bounded and unknown without a total', () => {
  assert.equal(jobFraction({ processed: 5, total: 10 }), 0.5)
  assert.equal(jobFraction({ processed: 12, total: 10 }), 1)
  assert.equal(jobFraction({ processed: 0, total: 0 }), null)
  assert.equal(jobFraction(null), null)
})
