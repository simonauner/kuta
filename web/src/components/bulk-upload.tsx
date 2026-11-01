import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { ApiError, api } from '@/lib/api'
import type { Activity } from '@/lib/activity'
import { refreshActivityQueries } from '@/lib/activity-query'

type Result =
  | { state: 'waiting' | 'uploading' | 'duplicate' }
  | { state: 'saved'; id: string }
  | { state: 'failed'; error: Error }

// Several files are saved straight away, one at a time (the API parses at most two files at once and limits
// uploads per user), each with what the file contains and the chosen visibility.
export function BulkUpload({
  files,
  privacy,
  onReset,
}: {
  files: File[]
  privacy: string
  onReset: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [results, setResults] = useState<Result[]>(() => files.map(() => ({ state: 'waiting' })))
  const started = useRef(false)

  useEffect(() => {
    if (started.current) return
    started.current = true
    void (async () => {
      for (const [index, file] of files.entries()) {
        const set = (result: Result) =>
          setResults((current) => current.map((r, i) => (i === index ? result : r)))
        set({ state: 'uploading' })
        const form = new FormData()
        form.append('file', file)
        form.append('privacy', privacy)
        try {
          set({ state: 'saved', id: (await api.upload<Activity>('/api/activities', form)).id })
        } catch (error) {
          set(
            error instanceof ApiError && error.code === 'duplicateActivity'
              ? { state: 'duplicate' }
              : { state: 'failed', error: error as Error },
          )
        }
      }
      await refreshActivityQueries(queryClient)
    })()
  }, [files, privacy, queryClient])

  const done = results.every((r) => r.state !== 'waiting' && r.state !== 'uploading')
  const count = (state: Result['state']) => results.filter((r) => r.state === state).length
  return (
    <section aria-labelledby="bulk-title">
      <Heading level={2} id="bulk-title">
        {t('newActivity.bulkTitle', { count: files.length })}
      </Heading>
      <ul className={card({ padding: 'none', className: 'mt-4 divide-y' })}>
        {files.map((file, index) => {
          const result = results[index]
          return (
            <li key={index} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
              <span className="min-w-0 break-all text-sm font-medium">{file.name}</span>
              <span className="text-sm text-muted-foreground">
                {result.state === 'saved' ? (
                  <Link
                    to="/app/activities/$activityId"
                    params={{ activityId: result.id }}
                    className="underline underline-offset-4"
                  >
                    {t('newActivity.bulk.saved')}
                  </Link>
                ) : result.state === 'failed' ? (
                  <span className="text-destructive">
                    <ErrorMessage error={result.error} />
                  </span>
                ) : (
                  t(`newActivity.bulk.${result.state}`)
                )}
              </span>
            </li>
          )
        })}
      </ul>
      <p role="status" className="mt-4 text-sm">
        {done &&
          t('newActivity.bulkDone', {
            saved: count('saved'),
            duplicates: count('duplicate'),
            failed: count('failed'),
          })}
      </p>
      {done && (
        <div className="mt-4 flex flex-wrap gap-3">
          <Button asChild>
            <Link to="/app/activities">{t('newActivity.showActivities')}</Link>
          </Button>
          <Button variant="secondary-outline" onClick={onReset}>
            {t('newActivity.uploadMore')}
          </Button>
        </div>
      )}
    </section>
  )
}
