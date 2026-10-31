import { useState, type FormEvent } from 'react'
import { Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { formatDuration, parseDuration } from '@/lib/activity'
import { equipmentQuery, equipmentKindsQuery } from '@/lib/equipment'
import { activityQuery } from '@/lib/activity-query'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ErrorMessage } from '@/components/error-message'

export function EquipmentSection() {
  const { t, i18n } = useTranslation()
  const equipment = useQuery(equipmentQuery)
  const kinds = useQuery(equipmentKindsQuery)
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState(false)
  const [name, setName] = useState('')
  const [brand, setBrand] = useState('')
  const [kind, setKind] = useState('shoe')
  const [distanceKm, setDistanceKm] = useState('')
  const [duration, setDuration] = useState('')
  const km = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 2 })
  const create = useMutation({
    mutationFn: () =>
      api.send('POST', '/api/equipment', {
        name,
        brand,
        kind,
        initialDistanceM: distanceKm === '' ? 0 : Number(distanceKm) * 1000,
        initialDurationS: duration === '' ? 0 : parseDuration(duration),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey })
      setAdding(false)
      setName('')
      setBrand('')
      setDistanceKm('')
      setDuration('')
    },
  })
  function onSubmit(e: FormEvent) {
    e.preventDefault()
    create.mutate()
  }
  return (
    <section className="mt-8 border-t pt-6" aria-labelledby="equipment-heading">
      <div className="flex items-center justify-between gap-3">
        <h2 id="equipment-heading" className="text-lg font-semibold">
          {t('equipment.title')}
        </h2>
        <Button
          size="sm"
          variant="outline"
          onClick={() => {
            setAdding(!adding)
            create.reset()
          }}
        >
          {t(adding ? 'equipment.cancel' : 'equipment.add')}
        </Button>
      </div>
      <p className="mt-2 text-sm text-muted-foreground">{t('equipment.description')}</p>
      {equipment.error && (
        <p role="alert" className="mt-3 text-sm text-destructive">
          <ErrorMessage error={equipment.error} />
        </p>
      )}
      {equipment.data?.length === 0 && (
        <p className="mt-4 text-sm text-muted-foreground">{t('equipment.empty')}</p>
      )}
      <ul className="mt-4 divide-y">
        {equipment.data?.map((item) => (
          <li key={item.id} className="py-3">
            <p className="font-medium">
              {item.name}
              {item.retired && ` · ${t('equipment.retired')}`}
            </p>
            <p className="text-xs text-muted-foreground">
              {t(`equipmentKinds.${item.kind}`)}
              {item.brand && ` · ${item.brand}`}
            </p>
            <dl className="mt-2 flex gap-8 text-sm tabular-nums">
              <div>
                <dt className="text-xs text-muted-foreground">{t('equipment.distance')}</dt>
                <dd>{km.format(item.distanceM / 1000)} km</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">{t('equipment.duration')}</dt>
                <dd>{formatDuration(item.durationS)}</dd>
              </div>
            </dl>
          </li>
        ))}
      </ul>
      {adding && (
        <form onSubmit={onSubmit} className="mt-4 grid grid-cols-2 gap-4">
          <label className="col-span-2 space-y-2 text-sm font-medium">
            {t('equipment.name')}
            <Input
              required
              maxLength={100}
              value={name}
              onChange={(e) => setName(e.currentTarget.value)}
            />
          </label>
          <label className="space-y-2 text-sm font-medium">
            {t('equipment.brand')}
            <Input
              maxLength={100}
              value={brand}
              onChange={(e) => setBrand(e.currentTarget.value)}
            />
          </label>
          <label className="space-y-2 text-sm font-medium">
            {t('equipment.kind')}
            <select
              className="h-9 w-full rounded-md border bg-background px-3"
              value={kind}
              onChange={(e) => setKind(e.currentTarget.value)}
            >
              {kinds.data?.map((value) => (
                <option key={value} value={value}>
                  {t(`equipmentKinds.${value}`)}
                </option>
              ))}
            </select>
          </label>
          <p className="col-span-2 text-xs text-muted-foreground">{t('equipment.initialHint')}</p>
          <label className="space-y-2 text-sm font-medium">
            {t('equipment.initialDistance')}
            <Input
              type="number"
              inputMode="decimal"
              min={0}
              step={0.01}
              value={distanceKm}
              onChange={(e) => setDistanceKm(e.currentTarget.value)}
            />
          </label>
          <label className="space-y-2 text-sm font-medium">
            {t('equipment.initialDuration')}
            <Input
              pattern="\d+(:[0-5]\d){0,2}"
              placeholder="0:00:00"
              title={t('newActivity.durationHint')}
              value={duration}
              onChange={(e) => setDuration(e.currentTarget.value)}
            />
          </label>
          {kinds.error && (
            <p role="alert" className="col-span-2 text-sm text-destructive">
              <ErrorMessage error={kinds.error} />
            </p>
          )}
          <div className="col-span-2">
            <Button disabled={create.isPending || !kinds.data?.length}>
              {t('equipment.save')}
            </Button>
          </div>
          {create.error && (
            <p role="alert" className="col-span-2 text-sm text-destructive">
              <ErrorMessage error={create.error} />
            </p>
          )}
        </form>
      )}
    </section>
  )
}

export function EquipmentPicker({
  value,
  onChange,
  disabled = false,
}: {
  value: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const equipment = useQuery(equipmentQuery)
  return (
    <fieldset disabled={disabled}>
      <legend className="mb-2 text-sm font-medium">{t('equipment.title')}</legend>
      {equipment.isPending && (
        <p role="status" className="text-sm text-muted-foreground">
          {t('equipment.loading')}
        </p>
      )}
      {equipment.data?.length === 0 && (
        <p className="text-sm text-muted-foreground">
          {t('equipment.empty')}{' '}
          <Link to="/app/profile" className="underline underline-offset-2">
            {t('equipment.addInProfile')}
          </Link>
        </p>
      )}
      <div className="flex flex-wrap gap-3">
        {equipment.data?.map((item) => (
          <label
            key={item.id}
            className="flex cursor-pointer items-center gap-2 rounded-md border px-3 py-2 text-sm"
          >
            <input
              type="checkbox"
              checked={value.includes(item.id)}
              onChange={(e) =>
                onChange(
                  e.currentTarget.checked
                    ? [...value, item.id]
                    : value.filter((id) => id !== item.id),
                )
              }
            />
            {item.name}
            {item.retired && ` · ${t('equipment.retired')}`}
          </label>
        ))}
      </div>
      {equipment.error && (
        <p role="alert" className="text-sm text-destructive">
          <ErrorMessage error={equipment.error} />
        </p>
      )}
    </fieldset>
  )
}

export function ActivityEquipmentEditor({
  activityId,
  initialIds,
}: {
  activityId: string
  initialIds: string[]
}) {
  const { t } = useTranslation()
  const [ids, setIds] = useState(initialIds)
  const queryClient = useQueryClient()
  const save = useMutation({
    mutationFn: () =>
      api.send('PUT', `/api/activities/${activityId}/equipment`, { equipmentIds: ids }),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: activityQuery(activityId).queryKey }),
        queryClient.invalidateQueries({ queryKey: ['activities'] }),
        queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey }),
      ]),
  })
  const changed = ids.length !== initialIds.length || ids.some((id) => !initialIds.includes(id))
  return (
    <form
      className="mt-5 space-y-3"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <EquipmentPicker
        value={ids}
        onChange={(value) => {
          setIds(value)
          save.reset()
        }}
        disabled={save.isPending}
      />
      {changed && (
        <Button size="sm" disabled={save.isPending}>
          {t('equipment.saveLinks')}
        </Button>
      )}
      {save.error && (
        <p role="alert" className="text-sm text-destructive">
          <ErrorMessage error={save.error} />
        </p>
      )}
    </form>
  )
}
