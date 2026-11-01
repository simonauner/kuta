import { api } from './api.ts'

export type JobKind = 'strava_import' | 'recalculate'
export type JobStatus = 'queued' | 'running' | 'succeeded' | 'failed'

export type Job<Progress = unknown, Result = unknown> = {
  id: string
  kind: JobKind
  status: JobStatus
  progress: Progress | null
  result: Result | null
  errorCode: string | null
  createdAt: string
  finishedAt: string | null
}

export const isActive = (job: Pick<Job, 'status'> | null | undefined) =>
  job?.status === 'queued' || job?.status === 'running'

// Senaste jobbet av varje slag; frågas om varje sekund medan något jobb pågår.
export const latestJobsQuery = {
  queryKey: ['jobs'],
  queryFn: () => api.get<Job[]>('/api/jobs'),
  refetchInterval: (query: { state: { data?: Job[] } }) =>
    query.state.data?.some(isActive) ? 1000 : false,
}

export function latestJob<Progress, Result>(jobs: Job[] | undefined, kind: JobKind) {
  return jobs?.find((job) => job.kind === kind) as Job<Progress, Result> | undefined
}

// Andel klar, 0–1, för förloppsindikatorn; okänt totalt antal ger null.
export function jobFraction(progress: { processed: number; total: number } | null | undefined) {
  if (!progress || progress.total <= 0) return null
  return Math.min(1, progress.processed / progress.total)
}
