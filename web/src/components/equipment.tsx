import { EquipmentNameFields } from '@/components/equipment-name-fields'
import { SlowLoading } from '@/components/slow-loading'
import { FormField } from '@/components/ui/form-field'
import { Menu } from '@base-ui/react/menu'
import { Select } from '@base-ui/react/select'
import { Check, ChevronDown, EllipsisVertical } from 'lucide-react'
import { Modal } from '@/components/ui/modal'
import { useRef, useState, type FormEvent } from 'react'
import { Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { formatDuration, parseDuration } from '@/lib/activity'
import { equipmentQuery, equipmentKindsQuery, type Equipment } from '@/lib/equipment'
import { Button } from '@/components/ui/button'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { Input } from '@/components/ui/input'
import { ErrorMessage } from '@/components/error-message'
import { Select as NativeSelect } from '@/components/ui/select'
import { Heading } from '@/components/ui/heading'

export function EquipmentSection() {
  const { t, i18n } = useTranslation()
  const equipment = useQuery(equipmentQuery)
  const kinds = useQuery(equipmentKindsQuery)
  const queryClient = useQueryClient()
  const [deleting, setDeleting] = useState<Equipment | null>(null)
  const formDialog = useRef<HTMLDialogElement>(null)
  const [editing, setEditing] = useState<Equipment | null>(null)
  const [name, setName] = useState('')
  const [brand, setBrand] = useState('')
  const [kind, setKind] = useState('shoe')
  const singleKind = kinds.data?.length === 1
  const selectedKind = singleKind ? kinds.data![0] : kind
  const [distanceKm, setDistanceKm] = useState('')
  const [duration, setDuration] = useState('')
  const km = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 2 })
  const save = useMutation({
    mutationFn: () =>
      api.send(
        editing ? 'PUT' : 'POST',
        editing ? `/api/equipment/${editing.id}` : '/api/equipment',
        {
          name,
          brand,
          kind: selectedKind,
          initialDistanceM: distanceKm === '' ? 0 : Number(distanceKm) * 1000,
          initialDurationS: duration === '' ? 0 : parseDuration(duration),
        },
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey })
      formDialog.current?.close()
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
      setDeleting(null)
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
    if (!save.isPending) save.mutate()
  }
  return (
    <section className="mt-8 border-t pt-6" aria-labelledby="equipment-heading">
      <div className="flex items-center justify-between gap-3">
        <Heading level={2} id="equipment-heading">
          {t('shoes.title')}
        </Heading>
        <Button
          size="sm"
          variant="secondary-outline"
          onClick={() => {
            setEditing(null)
            setName('')
            setBrand('')
            setKind('shoe')
            setDistanceKm('')
            setDuration('')
            save.reset()
            formDialog.current?.showModal()
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
      {[false, true].map((retired) => {
        const items = equipment.data?.filter((item) => item.retired === retired) ?? []
        if (items.length === 0) return null
        return (
          <section
            key={String(retired)}
            className="mt-6"
            aria-labelledby={retired ? 'retired-equipment-heading' : 'active-equipment-heading'}
          >
            <Heading
              level={3}
              id={retired ? 'retired-equipment-heading' : 'active-equipment-heading'}
            >
              {t(retired ? 'equipment.retiredHeading' : 'equipment.activeHeading')}
            </Heading>
            <ul className="mt-2 divide-y">
              {items.map((item) => (
                <li key={item.id} className="py-3">
                  <div className="flex items-center justify-between gap-4">
                    <div className="min-w-0 break-words">
                      <p className="font-medium">{item.name}</p>
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
                              className="cursor-default rounded-sm px-2 py-1.5 text-sm outline-none data-highlighted:bg-accent data-highlighted:text-accent-foreground"
                              onClick={() => {
                                setEditing(item)
                                setName(item.name)
                                setBrand(item.brand ?? '')
                                setKind(item.kind)
                                setDistanceKm(
                                  item.initialDistanceM ? String(item.initialDistanceM / 1000) : '',
                                )
                                setDuration(
                                  item.initialDurationS
                                    ? formatDuration(item.initialDurationS)
                                    : '',
                                )
                                save.reset()
                                formDialog.current?.showModal()
                              }}
                            >
                              {t('equipment.edit')}
                            </Menu.Item>
                            <Menu.Item
                              className="cursor-default rounded-sm px-2 py-1.5 text-sm outline-none data-highlighted:bg-accent data-highlighted:text-accent-foreground data-disabled:opacity-50"
                              disabled={setRetired.isPending}
                              onClick={() =>
                                setRetired.mutate({ id: item.id, retired: !item.retired })
                              }
                            >
                              {t(item.retired ? 'shoes.reactivate' : 'shoes.retire')}
                            </Menu.Item>
                            <Menu.Item
                              className="cursor-default rounded-sm px-2 py-1.5 text-sm text-destructive outline-none data-highlighted:bg-destructive/10"
                              onClick={() => {
                                setDeleting(item)
                                remove.reset()
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
          </section>
        )
      })}
      <Modal
        ref={formDialog}
        aria-labelledby="equipment-form-title"
        onCancel={(event) => {
          if (save.isPending) event.preventDefault()
        }}
      >
        <Heading level={2} id="equipment-form-title">
          {t(editing ? 'equipment.edit' : 'equipment.add')}
        </Heading>
        <form onSubmit={onSubmit}>
          <fieldset disabled={save.isPending} className="mt-5 grid grid-cols-2 gap-4">
            <EquipmentNameFields
              name={name}
              brand={brand}
              onNameChange={setName}
              onBrandChange={setBrand}
              brandClassName={singleKind ? 'col-span-2 text-sm font-medium' : 'text-sm font-medium'}
            />
            {kinds.data && kinds.data.length > 1 && (
              <FormField label={t('equipment.kind')} className="text-sm font-medium">
                {(id) => (
                  <NativeSelect
                    id={id}
                    value={selectedKind}
                    onChange={(e) => setKind(e.currentTarget.value)}
                  >
                    {kinds.data?.map((value) => (
                      <option key={value} value={value}>
                        {t(`equipmentKinds.${value}`)}
                      </option>
                    ))}
                  </NativeSelect>
                )}
              </FormField>
            )}
            <p className="col-span-2 text-xs text-muted-foreground">{t('equipment.initialHint')}</p>
            <FormField label={t('equipment.initialDistance')} className="text-sm font-medium">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="any"
                  value={distanceKm}
                  onChange={(e) => setDistanceKm(e.currentTarget.value)}
                />
              )}
            </FormField>
            <FormField label={t('equipment.initialDuration')} className="text-sm font-medium">
              {(id) => (
                <Input
                  id={id}
                  pattern="\d+(:[0-5]\d){0,2}"
                  placeholder="0:00:00"
                  title={t('newActivity.durationHint')}
                  value={duration}
                  onChange={(e) => setDuration(e.currentTarget.value)}
                />
              )}
            </FormField>
            {kinds.error && (
              <p role="alert" className="col-span-2 text-sm text-destructive">
                <ErrorMessage error={kinds.error} />
              </p>
            )}

            {save.error && (
              <p role="alert" className="col-span-2 text-sm text-destructive">
                <ErrorMessage error={save.error} />
              </p>
            )}
            <div className="col-span-2 flex flex-wrap justify-end gap-3 pt-2">
              <Button
                type="button"
                variant="secondary-outline"
                onClick={() => formDialog.current?.close()}
              >
                {t('equipment.cancel')}
              </Button>
              <Button type="submit" disabled={!kinds.data?.length}>
                {save.isPending ? t('newActivity.saving') : t('equipment.save')}
              </Button>
            </div>
          </fieldset>
        </form>
      </Modal>
      <ConfirmationModal
        open={deleting !== null}
        title={t('shoes.confirmDelete', { name: deleting?.name })}
        description={t('shoes.deleteWarning')}
        confirmLabel={remove.isPending ? t('shoes.deleting') : t('shoes.delete')}
        cancelLabel={t('common.cancel')}
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </section>
  )
}

export function EquipmentPicker({
  value,
  initialIds,
  onChange,
  disabled = false,
}: {
  value: string[]
  initialIds: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const equipment = useQuery(equipmentQuery)
  const options =
    equipment.data?.filter(
      (item) => !item.retired || value.includes(item.id) || initialIds.includes(item.id),
    ) ?? []
  const label = (item: Equipment) =>
    `${item.name}${item.retired ? ` · ${t('equipment.retired')}` : ''}`
  return (
    <fieldset disabled={disabled}>
      <legend className="mb-2 text-sm font-medium">{t('equipment.title')}</legend>
      <SlowLoading loading={equipment.isPending}>
        {equipment.data?.length === 0 && (
          <p className="text-sm text-muted-foreground">
            {t('equipment.empty')}{' '}
            <Link to="/app/settings/equipment" className="underline underline-offset-2">
              {t('equipment.addInSettings')}
            </Link>
          </p>
        )}
        {options.length > 3 ? (
          <FormField label={t('equipment.title')} hideLabel>
            {(id) => (
              <Select.Root
                multiple
                value={value}
                onValueChange={onChange}
                disabled={disabled}
                items={options.map((item) => ({ value: item.id, label: label(item) }))}
              >
                <Select.Trigger
                  id={id}
                  className="flex min-h-9 w-full items-center justify-between gap-2 rounded-md border bg-background px-3 py-2 text-left text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
                >
                  <Select.Value
                    placeholder={t('equipment.choose')}
                    className="min-w-0 break-words"
                  />
                  <Select.Icon>
                    <ChevronDown className="size-4 shrink-0" aria-hidden="true" />
                  </Select.Icon>
                </Select.Trigger>
                <Select.Portal>
                  <Select.Positioner alignItemWithTrigger={false} sideOffset={4} className="z-50">
                    <Select.Popup className="max-h-[min(20rem,var(--available-height))] w-[var(--anchor-width)] overflow-y-auto rounded-md border bg-card p-1 text-card-foreground shadow-md outline-none">
                      <Select.List>
                        {options.map((item) => (
                          <Select.Item
                            key={item.id}
                            value={item.id}
                            className="flex cursor-default items-center gap-2 rounded-sm px-2 py-2 text-sm outline-none data-highlighted:bg-accent data-highlighted:text-accent-foreground"
                          >
                            <span className="size-4 shrink-0">
                              <Select.ItemIndicator>
                                <Check className="size-4" aria-hidden="true" />
                              </Select.ItemIndicator>
                            </span>
                            <Select.ItemText className="min-w-0 break-words">
                              {label(item)}
                            </Select.ItemText>
                          </Select.Item>
                        ))}
                      </Select.List>
                    </Select.Popup>
                  </Select.Positioner>
                </Select.Portal>
              </Select.Root>
            )}
          </FormField>
        ) : (
          <div className="flex flex-wrap gap-3">
            {options.map((item) => (
              <FormField
                key={item.id}
                layout="choice"
                label={label(item)}
                className="rounded-md border px-3 py-2 text-sm"
              >
                {(id) => (
                  <input
                    id={id}
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
                )}
              </FormField>
            ))}
          </div>
        )}
        {equipment.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={equipment.error} />
          </p>
        )}
      </SlowLoading>
    </fieldset>
  )
}
