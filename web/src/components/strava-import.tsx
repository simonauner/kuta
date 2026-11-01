import { FormField } from '@/components/ui/form-field'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { refreshActivityQueries } from '@/lib/activity-query'
import { api, ApiError } from '@/lib/api'
import { isActive, jobFraction, latestJob, latestJobsQuery, type Job } from '@/lib/jobs'

type StravaImportResult = {
  imported: number
  duplicates: number
  failedActivityIds: string[]
  equipmentImported: number
  equipmentLinked: number
}

type StravaImportProgress = {
  processed: number
  total: number
  imported: number
  duplicates: number
  failed: number
}

// Hämtar om passen en gång när ett jobb av slaget går från aktivt till avslutat.
function useRefreshWhenFinished(job: Job | undefined) {
  const queryClient = useQueryClient()
  const wasActive = useRef(false)
  useEffect(() => {
    if (isActive(job)) wasActive.current = true
    else if (wasActive.current && job) {
      wasActive.current = false
      void refreshActivityQueries(queryClient)
    }
  }, [job, queryClient])
}

export function JobProgress({
  job,
  label,
}: {
  job: Job<{ processed: number; total: number }>
  label: string
}) {
  const { t } = useTranslation()
  const fraction = jobFraction(job.progress)
  return (
    <div className="mt-3 text-sm text-muted-foreground">
      <p role="status">
        {job.status === 'queued' || !job.progress
          ? t('jobs.queued')
          : t('jobs.progress', { processed: job.progress.processed, total: job.progress.total })}
      </p>
      {fraction !== null && (
        <progress
          className="mt-2 h-2 w-full appearance-none overflow-hidden rounded-full bg-muted [&::-moz-progress-bar]:bg-primary [&::-webkit-progress-bar]:bg-muted [&::-webkit-progress-value]:bg-primary"
          aria-label={label}
          max={1}
          value={fraction}
        />
      )}
    </div>
  )
}

export function JobError({ job }: { job: Job }) {
  return (
    <p role="alert" className="mt-2 text-sm text-destructive">
      <ErrorMessage
        error={new ApiError(500, job.errorCode ?? 'jobFailed', job.errorCode ?? 'jobFailed')}
      />
    </p>
  )
}

export function StravaImport() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)
  const jobs = useQuery(latestJobsQuery)
  const job = latestJob<StravaImportProgress, StravaImportResult>(jobs.data, 'strava_import')
  const running = isActive(job)
  useRefreshWhenFinished(job)
  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<Job>('/api/activities/import/strava', form)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: latestJobsQuery.queryKey }),
  })

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!file) return
    const form = e.currentTarget
    upload.mutate(file, {
      onSuccess: () => {
        form.reset()
        setFile(null)
      },
    })
  }

  const result = job?.status === 'succeeded' ? job.result : null
  return (
    <form onSubmit={onSubmit} className="mt-4">
      <FormField label={t('settings.stravaFile')}>
        {(id) => (
          <Input
            id={id}
            type="file"
            accept=".zip"
            disabled={upload.isPending || running}
            onChange={(e) => {
              setFile(e.currentTarget.files?.[0] ?? null)
              upload.reset()
            }}
          />
        )}
      </FormField>
      <p className="mt-2 text-xs text-muted-foreground">{t('settings.importHelp')}</p>
      <Button type="submit" className="mt-4" disabled={!file || upload.isPending || running}>
        {upload.isPending
          ? t('settings.importUploading')
          : running
            ? t('settings.importing')
            : t('settings.import')}
      </Button>
      {upload.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={upload.error} />
        </p>
      )}
      {job && running && (
        <>
          <JobProgress job={job} label={t('settings.importing')} />
          {job.progress && (
            <p className="mt-2 text-sm text-muted-foreground">
              {t('settings.importResult', job.progress)}
            </p>
          )}
          <p className="mt-2 text-sm text-muted-foreground">{t('settings.importWait')}</p>
        </>
      )}
      {job?.status === 'failed' && <JobError job={job} />}
      {result && (
        <div role="status" className="mt-4 text-sm">
          <p>
            {t('settings.importResult', {
              imported: result.imported,
              duplicates: result.duplicates,
              failed: result.failedActivityIds.length,
            })}
          </p>
          <p>
            {t('settings.importEquipmentResult', {
              imported: result.equipmentImported,
              linked: result.equipmentLinked,
            })}
          </p>
          {result.failedActivityIds.length > 0 && (
            <details className="mt-2">
              <summary className="cursor-pointer">{t('settings.failedImports')}</summary>
              <p className="mt-1 break-words text-muted-foreground">
                {result.failedActivityIds.join(', ')}
              </p>
            </details>
          )}
        </div>
      )}
    </form>
  )
}

type RecalculationProgress = { processed: number; total: number }
type RecalculationResult = { recalculated: number }

export function Recalculation() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const jobs = useQuery(latestJobsQuery)
  const job = latestJob<RecalculationProgress, RecalculationResult>(jobs.data, 'recalculate')
  const running = isActive(job)
  useRefreshWhenFinished(job)
  const start = useMutation({
    mutationFn: () => api.send<Job>('POST', '/api/activities/recalculate'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: latestJobsQuery.queryKey }),
  })
  return (
    <div className="mt-4">
      <p className="text-sm text-muted-foreground">{t('settings.recalculateHelp')}</p>
      <Button
        className="mt-4"
        variant="outline"
        disabled={start.isPending || running}
        onClick={() => start.mutate()}
      >
        {running ? t('settings.recalculating') : t('settings.recalculate')}
      </Button>
      {start.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={start.error} />
        </p>
      )}
      {job && running && <JobProgress job={job} label={t('settings.recalculating')} />}
      {job?.status === 'failed' && <JobError job={job} />}
      {job?.status === 'succeeded' && job.result && (
        <p role="status" className="mt-3 text-sm">
          {t('settings.recalculated', { count: job.result.recalculated })}
        </p>
      )}
    </div>
  )
}
