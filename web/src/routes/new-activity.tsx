import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useId, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { BackLink } from '@/components/back-link'
import { ErrorMessage } from '@/components/error-message'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import {
  activityTitle,
  formatDuration,
  parseDuration,
  sports,
  toDateTimeLocal,
  type Activity,
} from '@/lib/activity'
import { meQuery } from '@/lib/auth'

// Det API:t läser ur en FIT-fil, utan att spara något (POST /api/activities/preview)
type ActivityPreview = {
  sport: string
  startedAt: string
  durationS: number
  distanceM: number
  avgHr: number | null
  elevationGainM: number | null
  device: string | null
}

const sportOptions = [...sports, 'other'] as const
const knownSport = (sport: string) =>
  (sportOptions as readonly string[]).includes(sport) ? sport : 'other'

// Ett pass skapas antingen helt manuellt eller från en FIT-fil som fyller i fälten. Från en fil går bara titel
// och sport att ändra: mätvärdena hör ihop med tidsserierna (karta, diagram, splits, rekord).
export function NewActivityPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)
  const uploadDialog = useRef<HTMLDialogElement>(null)

  const [pendingFile, setPendingFile] = useState<File | null>(null) // vald i modalen, inte använd än
  const [dialogKey, setDialogKey] = useState(0)
  const [file, setFile] = useState<File | null>(null)
  const [title, setTitle] = useState('')
  const [sport, setSport] = useState<string | null>(null) // null = som filen (eller löpning)
  const [initialStartedAt] = useState(() => toDateTimeLocal(new Date()))
  const [startedAt, setStartedAt] = useState(initialStartedAt)
  const [distanceKm, setDistanceKm] = useState('')
  const [duration, setDuration] = useState('')
  const [elevation, setElevation] = useState('')
  const [avgHr, setAvgHr] = useState('')

  // Läses om när samtycket ändras, så att pulsen som visas är den som sparas
  const preview = useQuery({
    queryKey: ['activityPreview', file?.name, file?.size, file?.lastModified, me?.hrConsent],
    queryFn: () => {
      const form = new FormData()
      form.append('file', file!)
      return api.upload<ActivityPreview>('/api/activities/preview', form)
    },
    enabled: file !== null,
    retry: false,
    gcTime: 0,
  })
  const fromFile = file ? preview.data : undefined
  const selectedSport = sport ?? (fromFile ? knownSport(fromFile.sport) : 'running')

  const save = useMutation({
    mutationFn: () => {
      if (file) {
        const form = new FormData()
        form.append('file', file)
        form.append('title', title)
        if (sport) form.append('sport', sport)
        return api.upload<Activity>('/api/activities', form)
      }
      return api.send<Activity>('POST', '/api/activities/manual', {
        sport: selectedSport,
        title,
        startedAt: new Date(startedAt).toISOString(),
        durationS: parseDuration(duration),
        distanceM: Math.round(Number(distanceKm) * 1000),
        elevationGainM: elevation === '' ? null : Number(elevation),
        avgHr: avgHr === '' ? null : Number(avgHr),
      })
    },
    onSuccess: async (activity) => {
      await queryClient.invalidateQueries({ queryKey: ['activities'] })
      await navigate({ to: '/app/activities/$activityId', params: { activityId: activity.id } })
    },
  })

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  // Ifyllda fält som filen skriver över; titeln behålls och varnas inte för. Startfältet räknas bara om det ändrats.
  const overwritten = [
    sport !== null && t('newActivity.sport'),
    startedAt !== initialStartedAt && t('newActivity.startedAt'),
    distanceKm !== '' && t('newActivity.distance'),
    duration !== '' && t('newActivity.duration'),
    elevation !== '' && t('newActivity.elevation'),
    avgHr !== '' && t('newActivity.avgHr'),
  ].filter((field): field is string => !!field && !file)

  const locked = file !== null
  // Titeln passet får om fältet lämnas tomt (sport + tid på dygnet)
  const start = fromFile?.startedAt ?? startedAt
  const titlePlaceholder = start
    ? activityTitle({ title: null, sport: selectedSport, startedAt: start }, t)
    : undefined

  return (
    <section>
      <BackLink />
      <div className="mt-4 flex items-center justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">{t('newActivity.title')}</h1>
        <Button
          variant="outline"
          size="sm"
          disabled={save.isPending}
          onClick={() => {
            setPendingFile(null)
            setDialogKey((k) => k + 1) // tomt filfält varje gång modalen öppnas
            uploadDialog.current?.showModal()
          }}
        >
          {t('newActivity.upload')}
        </Button>
      </div>

      <form onSubmit={onSubmit} className="mt-6 space-y-6">
        {file && (
          <div className="text-sm">
            <p className="flex flex-wrap items-center gap-x-3">
              <span className="font-medium">{file.name}</span>
              <button
                type="button"
                className="text-muted-foreground underline underline-offset-2"
                onClick={() => {
                  setFile(null)
                  setSport(null)
                  save.reset()
                }}
              >
                {t('newActivity.removeFile')}
              </button>
            </p>
            {preview.isFetching && (
              <p className="mt-1 text-xs text-muted-foreground">{t('newActivity.reading')}</p>
            )}
            {preview.error && (
              <p role="alert" className="mt-1 text-destructive">
                <ErrorMessage error={preview.error} />
              </p>
            )}
          </div>
        )}

        <div className="grid grid-cols-2 gap-4">
          <Field label={t('newActivity.name')} className="col-span-2">
            {(id) => (
              <Input
                id={id}
                value={title}
                maxLength={100}
                placeholder={titlePlaceholder}
                onChange={(e) => setTitle(e.currentTarget.value)}
              />
            )}
          </Field>
          <Field label={t('newActivity.sport')}>
            {(id) => (
              <select
                id={id}
                value={selectedSport}
                onChange={(e) => setSport(e.currentTarget.value)}
                className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-base shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
              >
                {sportOptions.map((s) => (
                  <option key={s} value={s}>
                    {t(`newActivity.sports.${s}`)}
                  </option>
                ))}
              </select>
            )}
          </Field>
          <Field label={t('newActivity.startedAt')}>
            {(id) => (
              <Input
                id={id}
                type="datetime-local"
                required
                disabled={locked}
                value={fromFile ? toDateTimeLocal(new Date(fromFile.startedAt)) : startedAt}
                onChange={(e) => setStartedAt(e.currentTarget.value)}
              />
            )}
          </Field>
          <Field label={t('newActivity.distance')}>
            {(id) => (
              <Input
                id={id}
                type="number"
                inputMode="decimal"
                min={0}
                max={1000}
                step={0.01}
                required
                disabled={locked}
                value={fromFile ? (fromFile.distanceM / 1000).toFixed(2) : distanceKm}
                onChange={(e) => setDistanceKm(e.currentTarget.value)}
              />
            )}
          </Field>
          <Field label={t('newActivity.duration')}>
            {(id) => (
              <Input
                id={id}
                required
                disabled={locked}
                pattern="\d+(:[0-5]\d){0,2}"
                placeholder="0:45:00"
                title={t('newActivity.durationHint')}
                value={fromFile ? formatDuration(fromFile.durationS) : duration}
                onChange={(e) => setDuration(e.currentTarget.value)}
              />
            )}
          </Field>
          <Field label={t('newActivity.elevation')}>
            {(id) => (
              <Input
                id={id}
                type="number"
                inputMode="numeric"
                min={0}
                max={20000}
                disabled={locked}
                value={fromFile ? Math.round(fromFile.elevationGainM ?? 0) : elevation}
                onChange={(e) => setElevation(e.currentTarget.value)}
              />
            )}
          </Field>
          {me?.hrConsent && (
            <Field label={t('newActivity.avgHr')}>
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  inputMode="numeric"
                  min={30}
                  max={250}
                  disabled={locked}
                  value={fromFile ? (fromFile.avgHr ?? '') : avgHr}
                  onChange={(e) => setAvgHr(e.currentTarget.value)}
                />
              )}
            </Field>
          )}
        </div>
        {fromFile && (
          <p className="text-xs text-muted-foreground">
            {fromFile.device
              ? t('newActivity.lockedFrom', { device: fromFile.device })
              : t('newActivity.locked')}
          </p>
        )}

        <Button
          type="submit"
          disabled={save.isPending || (locked && !fromFile) || preview.isFetching}
        >
          {save.isPending ? t('newActivity.saving') : t('newActivity.save')}
        </Button>
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
      </form>

      <dialog
        ref={uploadDialog}
        aria-labelledby="upload-title"
        className="fixed inset-0 m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-md overflow-y-auto rounded-xl border bg-background p-6 text-foreground shadow-xl backdrop:bg-black/70"
      >
        <h2 id="upload-title" className="text-xl font-semibold">
          {t('newActivity.upload')}
        </h2>
        {overwritten.length > 0 && (
          <p
            role="alert"
            className="mt-3 rounded-md bg-destructive/10 p-3 text-sm text-destructive"
          >
            {t('newActivity.overwrite', {
              fields: new Intl.ListFormat(i18n.resolvedLanguage).format(overwritten),
            })}
          </p>
        )}
        <Label htmlFor="fit-file" className="mt-5 mb-2">
          {t('newActivity.file')}
        </Label>
        <Input
          key={dialogKey}
          id="fit-file"
          type="file"
          accept=".fit"
          onChange={(e) => setPendingFile(e.currentTarget.files?.[0] ?? null)}
        />
        <HeartRateConsent className="mt-4" />
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Button type="button" variant="outline" onClick={() => uploadDialog.current?.close()}>
            {t('newActivity.cancel')}
          </Button>
          <Button
            type="button"
            disabled={!pendingFile}
            onClick={() => {
              setFile(pendingFile)
              setSport(null)
              save.reset()
              uploadDialog.current?.close()
            }}
          >
            {t('newActivity.useFile')}
          </Button>
        </div>
      </dialog>
    </section>
  )
}

function Field({
  label,
  className,
  children,
}: {
  label: string
  className?: string
  children: (id: string) => ReactNode
}) {
  const id = useId()
  return (
    <div className={className}>
      <Label htmlFor={id} className="mb-2">
        {label}
      </Label>
      {children(id)}
    </div>
  )
}
