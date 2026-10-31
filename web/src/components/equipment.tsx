import { Menu } from '@base-ui/react/menu'
import { EllipsisVertical } from 'lucide-react'
import { Modal } from '@/components/ui/modal'
import { useRef, useState, type FormEvent } from 'react'
import { Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import { formatDuration, parseDuration } from '@/lib/activity'
import { equipmentQuery, equipmentKindsQuery, type Equipment } from '@/lib/equipment'
import { activityQuery } from '@/lib/activity-query'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ErrorMessage } from '@/components/error-message'

export function EquipmentSection() {
  const { t, i18n } = useTranslation()
  const equipment = useQuery(equipmentQuery)
  const kinds = useQuery(equipmentKindsQuery)
  const queryClient = useQueryClient()
  const deleteDialog = useRef<HTMLDialogElement>(null)
  const [deleting, setDeleting] = useState<Equipment | null>(null)
  const createDialog = useRef<HTMLDialogElement>(null)
  const [name, setName] = useState('')
  const [brand, setBrand] = useState('')
  const [kind, setKind] = useState('shoe')
  const singleKind = kinds.data?.length === 1
  const selectedKind = singleKind ? kinds.data![0] : kind
  const [distanceKm, setDistanceKm] = useState('')
  const [duration, setDuration] = useState('')
  const km = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 2 })
  const create = useMutation({
    mutationFn: () =>
      api.send('POST', '/api/equipment', {
        name,
        brand,
        kind: selectedKind,
        initialDistanceM: distanceKm === '' ? 0 : Number(distanceKm) * 1000,
        initialDurationS: duration === '' ? 0 : parseDuration(duration),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey })
      createDialog.current?.close()
      setName('')
      setBrand('')
      setDistanceKm('')
      setDuration('')
    },
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey })
  const setRetired = useMutation({
    mutationFn: ({ id, retired }: Pick<Equipment, 'id' | 'retired'>) =>
      api.send(retired ? 'PUT' : 'DELETE', `/api/equipment/${id}/retire`),
    onSuccess: () =>
      Promise.all([
        refresh(),
        queryClient.invalidateQueries({ queryKey: ['equipmentLastUsedShoe'] }),
      ]),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.send('DELETE', `/api/equipment/${id}`),
    onSuccess: async () => {
      deleteDialog.current?.close()
      await Promise.all([
        refresh(),
        queryClient.invalidateQueries({
          predicate: (query) =>
            ['activities', 'activity', 'equipmentLastUsedShoe'].includes(String(query.queryKey[0])),
        }),
      ])
    },
  })
  function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (!create.isPending) create.mutate()
  }
  return (
    <section className="mt-8 border-t pt-6" aria-labelledby="equipment-heading">
      <div className="flex items-center justify-between gap-3">
        <h2 id="equipment-heading" className="text-lg font-semibold">
          {t('shoes.title')}
        </h2>
        <Button
          size="sm"
          variant="outline"
          onClick={() => {
            setName('')
            setBrand('')
            setKind('shoe')
            setDistanceKm('')
            setDuration('')
            create.reset()
            createDialog.current?.showModal()
          }}
        >
          {t('equipment.add')}
        </Button>
      </div>
      <p className="mt-2 text-sm text-muted-foreground">{t('equipment.description')}</p>
      {equipment.error && (
        <p role="alert" className="mt-3 text-sm text-destructive">
          <ErrorMessage error={equipment.error} />
        </p>
      )}
      {setRetired.error && (
        <p role="alert" className="mt-3 text-sm text-destructive">
          <ErrorMessage error={setRetired.error} />
        </p>
      )}
      {equipment.data?.length === 0 && (
        <p className="mt-4 text-sm text-muted-foreground">{t('equipment.empty')}</p>
      )}
      <ul className="mt-4 divide-y">
        {equipment.data?.map((item) => (
          <li key={item.id} className="py-3">
            <div className="flex items-center justify-between gap-4">
              <div className="min-w-0 break-words">
                <p className="font-medium">
                  {item.name}
                  {item.retired && (
                    <span className="font-normal text-muted-foreground">
                      {' '}
                      · {t('shoes.retired')}
                    </span>
                  )}
                </p>
                <p className="text-xs text-muted-foreground">
                  {t(`equipmentKinds.${item.kind}`)}
                  {item.brand && ` · ${item.brand}`}
                </p>
              </div>
              <Menu.Root>
                <Menu.Trigger
                  render={<Button size="icon-sm" variant="ghost" />}
                  aria-label={t('equipment.actionsLabel', { name: item.name })}
                >
                  <EllipsisVertical aria-hidden="true" />
                </Menu.Trigger>
                <Menu.Portal>
                  <Menu.Positioner align="end" sideOffset={4} className="z-50">
                    <Menu.Popup className="min-w-40 rounded-md border bg-card p-1 text-card-foreground shadow-md outline-none">
                      <Menu.Item
                        className="cursor-default rounded-sm px-2 py-1.5 text-sm outline-none data-highlighted:bg-accent data-highlighted:text-accent-foreground data-disabled:opacity-50"
                        disabled={setRetired.isPending}
                        onClick={() => setRetired.mutate({ id: item.id, retired: !item.retired })}
                      >
                        {t(item.retired ? 'shoes.reactivate' : 'shoes.retire')}
                      </Menu.Item>
                      <Menu.Item
                        className="cursor-default rounded-sm px-2 py-1.5 text-sm text-destructive outline-none data-highlighted:bg-destructive/10"
                        onClick={() => {
                          setDeleting(item)
                          remove.reset()
                          deleteDialog.current?.showModal()
                        }}
                      >
                        {t('shoes.delete')}
                      </Menu.Item>
                    </Menu.Popup>
                  </Menu.Positioner>
                </Menu.Portal>
              </Menu.Root>
            </div>
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
      <Modal
        ref={createDialog}
        aria-labelledby="create-equipment-title"
        onCancel={(event) => {
          if (create.isPending) event.preventDefault()
        }}
      >
        <h2 id="create-equipment-title" className="text-xl font-semibold">
          {t('equipment.add')}
        </h2>
        <form onSubmit={onSubmit}>
          <fieldset disabled={create.isPending} className="mt-5 grid grid-cols-2 gap-4">
            <label className="col-span-2 space-y-2 text-sm font-medium">
              {t('equipment.name')}
              <Input
                autoFocus
                required
                maxLength={100}
                value={name}
                onChange={(e) => setName(e.currentTarget.value)}
              />
            </label>
            <label className={cn('space-y-2 text-sm font-medium', singleKind && 'col-span-2')}>
              {t('equipment.brand')}
              <Input
                maxLength={100}
                value={brand}
                onChange={(e) => setBrand(e.currentTarget.value)}
              />
            </label>
            {kinds.data && kinds.data.length > 1 && (
              <label className="space-y-2 text-sm font-medium">
                {t('equipment.kind')}
                <select
                  className="h-9 w-full rounded-md border bg-background px-3"
                  value={selectedKind}
                  onChange={(e) => setKind(e.currentTarget.value)}
                >
                  {kinds.data?.map((value) => (
                    <option key={value} value={value}>
                      {t(`equipmentKinds.${value}`)}
                    </option>
                  ))}
                </select>
              </label>
            )}
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

            {create.error && (
              <p role="alert" className="col-span-2 text-sm text-destructive">
                <ErrorMessage error={create.error} />
              </p>
            )}
            <div className="col-span-2 flex flex-wrap justify-end gap-3 pt-2">
              <Button type="button" variant="outline" onClick={() => createDialog.current?.close()}>
                {t('equipment.cancel')}
              </Button>
              <Button type="submit" disabled={!kinds.data?.length}>
                {create.isPending ? t('newActivity.saving') : t('equipment.save')}
              </Button>
            </div>
          </fieldset>
        </form>
      </Modal>
      <Modal
        ref={deleteDialog}
        aria-labelledby="delete-shoe-title"
        aria-describedby="delete-shoe-warning"
        onClose={() => setDeleting(null)}
        onCancel={(event) => {
          if (remove.isPending) event.preventDefault()
        }}
      >
        <h2 id="delete-shoe-title" className="break-words text-xl font-semibold">
          {t('shoes.confirmDelete', { name: deleting?.name })}
        </h2>
        <p id="delete-shoe-warning" className="mt-3 text-sm leading-relaxed text-muted-foreground">
          {t('shoes.deleteWarning')}
        </p>
        {remove.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={remove.error} />
          </p>
        )}
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Button
            variant="outline"
            disabled={remove.isPending}
            onClick={() => deleteDialog.current?.close()}
          >
            {t('shoes.cancel')}
          </Button>
          <Button
            variant="destructive"
            disabled={remove.isPending || !deleting}
            onClick={() => {
              if (deleting) remove.mutate(deleting.id)
            }}
          >
            {remove.isPending ? t('shoes.deleting') : t('shoes.delete')}
          </Button>
        </div>
      </Modal>
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
          <Link to="/app/profile/equipment" className="underline underline-offset-2">
            {t('equipment.addInProfile')}
          </Link>
        </p>
      )}
      <div className="flex flex-wrap gap-3">
        {equipment.data
          ?.filter((item) => !item.retired || value.includes(item.id))
          .map((item) => (
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
