import { ErrorMessage } from '@/components/error-message'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState, type ChangeEvent, type FormEvent } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import { activityTitle, formatDuration, formatPace, type Activity } from '@/lib/activity'
import { meQuery } from '@/lib/auth'

const activitiesQuery = {
  queryKey: ['activities'],
  queryFn: () => api.get<Activity[]>('/api/activities'),
}

export function ActivitiesPage() {
  const { t, i18n } = useTranslation()
  const dateFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
  const kmFormat = new Intl.NumberFormat(i18n.resolvedLanguage, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  const queryClient = useQueryClient()
  const activities = useQuery(activitiesQuery)
  const { data: me } = useQuery(meQuery)

  const [file, setFile] = useState<File | null>(null)

  const remove = useMutation({
    mutationFn: (id: string) => api.send('DELETE', `/api/activities/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
  })

  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<Activity>('/api/activities', form)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
  })

  // Filvalet startar ingen uppladdning.
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

  return (
    <section>
      <h1 className="text-2xl font-semibold tracking-tight">{t('nav.activities')}</h1>

      <form onSubmit={onSubmit} className="mt-6">
        <Label htmlFor="fit-file" className="mb-2">
          {t('activities.file')}
        </Label>
        <Input
          id="fit-file"
          type="file"
          accept=".fit"
          disabled={upload.isPending}
          onChange={(e: ChangeEvent<HTMLInputElement>) => {
            setFile(e.currentTarget.files?.[0] ?? null)
            upload.reset()
          }}
        />
        <p className="mt-2 text-xs text-muted-foreground">
          {t(me?.hrConsent ? 'activities.hrSaved' : 'activities.hrNotSaved')}{' '}
          <Link to="/app/settings" className="underline underline-offset-2">
            {t('settings.title')}
          </Link>
        </p>
        <Button type="submit" className="mt-4" disabled={!file || upload.isPending}>
          {upload.isPending ? t('activities.uploading') : t('activities.upload')}
        </Button>
        {upload.error && (
          <p role="alert" className="mt-2 text-sm text-destructive">
            <ErrorMessage error={upload.error} />
          </p>
        )}
      </form>

      {activities.data?.length === 0 && (
        <p className="mt-8 text-muted-foreground">{t('activities.empty')}</p>
      )}
      <ul className="mt-8 divide-y">
        {activities.data?.map((a) => (
          <li key={a.id} className="flex items-start justify-between gap-4 py-4">
            <div>
              <Link
                to="/app/activities/$activityId"
                params={{ activityId: a.id }}
                className="font-medium underline-offset-2 hover:underline"
              >
                {activityTitle(a, t)}
              </Link>
              <p className="text-xs text-muted-foreground">
                {dateFormat.format(new Date(a.startedAt))}
              </p>
              <p className="mt-1 text-sm text-muted-foreground">
                {kmFormat.format(a.distanceM / 1000)} km · {formatDuration(a.durationS)} ·{' '}
                {formatPace(a.durationS, a.distanceM)}
                {a.avgHr !== null && ` · ${a.avgHr} bpm`}
              </p>
            </div>
            <Button
              variant="ghost"
              size="sm"
              className="text-muted-foreground"
              disabled={remove.isPending}
              aria-label={t('activities.deleteLabel', {
                date: dateFormat.format(new Date(a.startedAt)),
              })}
              onClick={() => {
                if (window.confirm(t('activities.confirmDelete'))) remove.mutate(a.id)
              }}
            >
              {t('activities.delete')}
            </Button>
          </li>
        ))}
      </ul>
    </section>
  )
}
