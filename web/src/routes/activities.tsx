import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState, type ChangeEvent, type FormEvent } from 'react'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import { meQuery } from '@/lib/auth'

// Som API:t: SI-enheter (meter, sekunder). Metriskt här; imperial blir ett val per användare senare.
type Activity = {
  id: string
  sport: string
  title: string | null
  startedAt: string
  durationS: number
  distanceM: number
  avgHr: number | null
}

const activitiesQuery = {
  queryKey: ['activities'],
  queryFn: () => api.get<Activity[]>('/api/activities'),
}

const dateFormat = new Intl.DateTimeFormat('sv-SE', { dateStyle: 'medium', timeStyle: 'short' })
const kmFormat = new Intl.NumberFormat('sv-SE', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

function formatDuration(seconds: number) {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  const mmss = `${String(m).padStart(h ? 2 : 1, '0')}:${String(s).padStart(2, '0')}`
  return h ? `${h}:${mmss}` : mmss
}

function formatPace(durationS: number, distanceM: number) {
  if (distanceM < 1) return '–'
  const perKm = Math.round(durationS / (distanceM / 1000))
  return `${formatDuration(perKm)} /km`
}

export function ActivitiesPage() {
  const queryClient = useQueryClient()
  const activities = useQuery(activitiesQuery)
  const { data: me } = useQuery(meQuery)

  const [file, setFile] = useState<File | null>(null)

  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<Activity>('/api/activities', form)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
  })

  // Att välja en fil laddar inte upp den: användaren ska hinna ta ställning till pulsen först.
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
      <h1 className="text-2xl font-semibold tracking-tight">Pass</h1>

      <form onSubmit={onSubmit} className="mt-6">
        <Label htmlFor="fit-file" className="mb-2">
          FIT-fil från din klocka
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
        {/* Valet visas bara tills samtycke finns; därefter ändras det på profilsidan */}
        {me?.hrConsent ? (
          <p className="mt-2 text-xs text-muted-foreground">
            Pulsdata sparas. Ändra på{' '}
            <Link to="/app/profil" className="underline underline-offset-2">
              Profil
            </Link>
          </p>
        ) : (
          <HeartRateConsent className="mt-4" />
        )}
        <Button type="submit" className="mt-4" disabled={!file || upload.isPending}>
          {upload.isPending ? 'Laddar upp …' : 'Ladda upp'}
        </Button>
        {upload.error && (
          <p role="alert" className="mt-2 text-sm text-destructive">
            {upload.error.message}
          </p>
        )}
      </form>

      {activities.data?.length === 0 && <p className="mt-8 text-muted-foreground">Inga pass än.</p>}
      <ul className="mt-8 divide-y">
        {activities.data?.map((a) => (
          <li key={a.id} className="py-4">
            <p className="font-medium">{a.title ?? dateFormat.format(new Date(a.startedAt))}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {kmFormat.format(a.distanceM / 1000)} km · {formatDuration(a.durationS)} ·{' '}
              {formatPace(a.durationS, a.distanceM)}
              {a.avgHr !== null && ` · ${a.avgHr} bpm`}
            </p>
          </li>
        ))}
      </ul>
    </section>
  )
}
