import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useId, type ChangeEvent } from 'react'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'

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

const activitiesQuery = { queryKey: ['activities'], queryFn: () => api.get<Activity[]>('/api/activities') }

const dateFormat = new Intl.DateTimeFormat('sv-SE', { dateStyle: 'medium', timeStyle: 'short' })
const kmFormat = new Intl.NumberFormat('sv-SE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

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

  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<Activity>('/api/activities', form)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
  })

  function onFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.currentTarget.files?.[0]
    e.currentTarget.value = ''
    if (file) upload.mutate(file)
  }

  return (
    <section>
      <h1 className="text-2xl font-semibold tracking-tight">Pass</h1>

      <div className="mt-6 space-y-2">
        <Label htmlFor="fit-file">Ladda upp en FIT-fil från din klocka</Label>
        <Input id="fit-file" type="file" accept=".fit" onChange={onFile} disabled={upload.isPending} />
        {upload.isPending && <p className="text-sm text-muted-foreground">Laddar upp …</p>}
        {upload.error && (
          <p role="alert" className="text-sm text-destructive">
            {upload.error.message}
          </p>
        )}
      </div>

      <HeartRateConsent />

      {activities.data?.length === 0 && (
        <p className="mt-8 text-muted-foreground">Inga pass än.</p>
      )}
      <ul className="mt-8 divide-y">
        {activities.data?.map((a) => (
          <li key={a.id} className="py-4">
            <p className="font-medium">{a.title ?? dateFormat.format(new Date(a.startedAt))}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {kmFormat.format(a.distanceM / 1000)} km · {formatDuration(a.durationS)} · {formatPace(a.durationS, a.distanceM)}
              {a.avgHr !== null && ` · ${a.avgHr} bpm`}
            </p>
          </li>
        ))}
      </ul>
    </section>
  )
}

// Puls är hälsodata: sparas bara med uttryckligt samtycke, som går att återkalla (då raderas all sparad puls).
function HeartRateConsent() {
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)
  const id = useId()

  const change = useMutation({
    mutationFn: (consent: boolean) => api.send<Me>(consent ? 'PUT' : 'DELETE', '/api/me/hr-consent'),
    onSuccess: async (updated) => {
      queryClient.setQueryData(['me'], updated)
      await queryClient.invalidateQueries({ queryKey: ['activities'] })
    },
  })

  function onChange(e: ChangeEvent<HTMLInputElement>) {
    const consent = e.currentTarget.checked
    if (!consent && !window.confirm('All puls som redan sparats från dina pass raderas. Fortsätta?')) return
    change.mutate(consent)
  }

  return (
    <div className="mt-6 rounded-lg border p-4">
      <div className="flex items-start gap-3">
        <input
          id={id}
          type="checkbox"
          className="mt-1 size-4 accent-primary"
          checked={me?.hrConsent ?? false}
          disabled={change.isPending}
          onChange={onChange}
        />
        <Label htmlFor={id} className="block font-normal leading-relaxed">
          <span className="font-medium">Spara puls från mina pass.</span>{' '}
          <span className="text-muted-foreground">
            Puls är hälsodata. Utan ditt samtycke sparar Kuta passen utan puls och behåller inte originalfilen. Stänger
            du av det raderas all sparad puls.
          </span>
        </Label>
      </div>
      {change.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {change.error.message}
        </p>
      )}
    </div>
  )
}
