import { EquipmentNameFields } from '@/components/equipment-name-fields'
import { FormField } from '@/components/ui/form-field'
import {
  multisportKinds,
  multisportView,
  partsFromForm,
  type MultisportDraft,
  type ActivityPart,
} from '@/lib/multisport'
import { MultisportEditor } from '@/components/multisport-editor'
import { Modal } from '@/components/ui/modal'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useRef, useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { BackLink } from '@/components/back-link'
import { ErrorMessage } from '@/components/error-message'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { api } from '@/lib/api'
import {
  activityTitle,
  formatDuration,
  parseDuration,
  toDateTimeLocal,
  type Activity,
  type ActivityStreams,
  type ActivityTag,
} from '@/lib/activity'
import { sports as sportOptions, knownSport } from '@/lib/sports'
import { meQuery } from '@/lib/auth'
import { equipmentQuery, lastUsedShoeQuery, type Equipment } from '@/lib/equipment'
import { refreshActivityQueries } from '@/lib/activity-query'
import { TagPicker } from '@/components/activity-tags'
import { Select } from '@/components/ui/select'
import { Heading } from '@/components/ui/heading'

// What the API reads from a FIT file, without saving anything (POST /api/activities/preview)
type ActivityPreview = {
  sport: string
  startedAt: string
  durationS: number
  distanceM: number
  avgHr: number | null
  elevationGainM: number | null
  device: string | null
  streams: ActivityStreams
  parts: ActivityPart[]
  canSplit: boolean
}

// An activity is created either fully manually or from a FIT file that fills in the fields. From a file, title,
// description, sport and shoe can be changed: the metrics belong with the streams (map, charts, splits, records).
export function NewActivityPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const search = useSearch({ from: '/app/activities/new' })
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)
  const shoes = useQuery(equipmentQuery)
  const [shoeChoice, setShoeChoice] = useState<string | null>(null)
  const activeShoes = shoes.data?.filter((shoe) => shoe.kind === 'shoe' && !shoe.retired) ?? []
  const lastUsedShoe = useQuery(lastUsedShoeQuery)
  const shoeId =
    shoeChoice ??
    (activeShoes.some((shoe) => shoe.id === lastUsedShoe.data?.id) ? lastUsedShoe.data?.id : '') ??
    ''
  const uploadDialog = useRef<HTMLDialogElement>(null)
  const shoeDialog = useRef<HTMLDialogElement>(null)
  const shoeSelect = useRef<HTMLSelectElement>(null)
  const [shoeName, setShoeName] = useState('')
  const [shoeBrand, setShoeBrand] = useState('')
  const addShoe = useMutation({
    mutationFn: () =>
      api.send<Equipment>('POST', '/api/equipment', {
        name: shoeName,
        brand: shoeBrand,
        kind: 'shoe',
      }),
    onSuccess: (shoe) => {
      queryClient.setQueryData(equipmentQuery.queryKey, (current) => [...(current ?? []), shoe])
      setShoeChoice(shoe.id)
      shoeDialog.current?.close()
      shoeSelect.current?.focus()
      return queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey })
    },
  })

  const [pendingFile, setPendingFile] = useState<File | null>(null) // picked in the modal, not used yet
  const [dialogKey, setDialogKey] = useState(0)
  const [file, setFile] = useState<File | null>(null)
  const [split, setSplit] = useState<MultisportDraft>(undefined)
  const [tags, setTags] = useState<ActivityTag[]>([])
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [sport, setSport] = useState<string | null>(null) // null = as in the file (or running)
  const [initialStartedAt] = useState(() => toDateTimeLocal(new Date()))
  const [startedAt, setStartedAt] = useState(initialStartedAt)
  const [distanceKm, setDistanceKm] = useState('')
  const [duration, setDuration] = useState('')
  const [elevation, setElevation] = useState('')
  const [avgHr, setAvgHr] = useState('')

  // Re-read when consent changes, so the heart rate shown is the one saved
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
  const importedMultisport = fromFile && multisportKinds.some((k) => k === fromFile.sport)
  const selectedSport =
    split?.kind ??
    (importedMultisport
      ? fromFile.sport
      : (sport ?? (fromFile ? knownSport(fromFile.sport) : 'running')))

  const save = useMutation({
    mutationFn: (draft: MultisportDraft) => {
      if (file) {
        const form = new FormData()
        form.append('file', file)
        form.append('title', title)
        if (draft) form.append('parts', JSON.stringify(draft))
        form.append('description', description)
        if (shoeId) form.append('equipmentIds', shoeId)
        tags.forEach((tag) => form.append('tags', tag))
        if (sport) form.append('sport', sport)
        return api.upload<Activity>('/api/activities', form)
      }
      return api.send<Activity>('POST', '/api/activities/manual', {
        equipmentIds: shoeId ? [shoeId] : [],
        tags,
        sport: selectedSport,
        title,
        description,
        startedAt: new Date(startedAt).toISOString(),
        durationS: parseDuration(duration),
        distanceM: Math.round(Number(distanceKm) * 1000),
        elevationGainM: elevation === '' ? null : Number(elevation),
        avgHr: avgHr === '' ? null : Number(avgHr),
      })
    },
    onSuccess: async (activity) => {
      await refreshActivityQueries(queryClient)
      await navigate({
        to: '/app/activities/$activityId',
        params: { activityId: activity.id },
        search,
      })
    },
  })

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    save.mutate(partsFromForm(split, e.currentTarget))
  }

  // Filled fields the file overwrites; title and description are kept and not warned about. The start field counts only if changed.
  const overwritten = [
    sport !== null && t('newActivity.sport'),
    startedAt !== initialStartedAt && t('newActivity.startedAt'),
    distanceKm !== '' && t('newActivity.distance'),
    duration !== '' && t('newActivity.duration'),
    elevation !== '' && t('newActivity.elevation'),
    avgHr !== '' && t('newActivity.avgHr'),
  ].filter((field): field is string => !!field && !file)

  const locked = file !== null
  // The title the activity gets if the field is left empty (sport + time of day)
  const start = fromFile?.startedAt ?? startedAt
  const titlePlaceholder = start
    ? activityTitle({ title: null, sport: selectedSport, startedAt: start }, t)
    : undefined

  return (
    <section>
      <BackLink />
      <div className="mt-4 flex items-center justify-between gap-4">
        <Heading level={1}>{t('newActivity.title')}</Heading>
        <Button
          variant="secondary-outline"
          size="sm"
          disabled={save.isPending}
          onClick={() => {
            setPendingFile(null)
            setDialogKey((k) => k + 1) // empty file field every time the modal opens
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
              <Button
                type="button"
                variant="link"
                onClick={() => {
                  setFile(null)
                  setSplit(undefined)
                  setSport(null)
                  save.reset()
                }}
              >
                {t('newActivity.removeFile')}
              </Button>
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
          <FormField label={t('newActivity.name')} className="col-span-2">
            {(id) => (
              <Input
                id={id}
                value={title}
                maxLength={100}
                placeholder={titlePlaceholder}
                onChange={(e) => setTitle(e.currentTarget.value)}
              />
            )}
          </FormField>
          <FormField label={t('newActivity.description')} className="col-span-2">
            {(id) => (
              <textarea
                id={id}
                value={description}
                maxLength={10000}
                rows={3}
                onChange={(e) => setDescription(e.currentTarget.value)}
                className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
              />
            )}
          </FormField>
          <FormField label={t('newActivity.sport')}>
            {(id) => (
              <Select
                id={id}
                value={selectedSport}
                disabled={!!importedMultisport || !!split}
                onChange={(e) => setSport(e.currentTarget.value)}
              >
                {sportOptions.map((s) => (
                  <option key={s} value={s}>
                    {t(`newActivity.sports.${s}`)}
                  </option>
                ))}
              </Select>
            )}
          </FormField>
          <FormField
            label={t('shoes.title')}
            action={
              <Button
                type="button"
                variant="link"
                disabled={save.isPending || addShoe.isPending}
                onClick={() => {
                  setShoeName('')
                  setShoeBrand('')
                  addShoe.reset()
                  shoeDialog.current?.showModal()
                }}
              >
                {t('shoes.createNew')}
              </Button>
            }
          >
            {(id) => (
              <Select
                ref={shoeSelect}
                id={id}
                value={shoeId}
                onChange={(e) => setShoeChoice(e.currentTarget.value)}
                disabled={shoes.isPending || !!shoes.error}
              >
                <option value="">{t('shoes.none')}</option>
                {activeShoes.map((shoe) => (
                  <option key={shoe.id} value={shoe.id}>
                    {shoe.name}
                    {shoe.brand ? ` (${shoe.brand})` : ''}
                  </option>
                ))}
              </Select>
            )}
          </FormField>
          {(shoes.error || lastUsedShoe.error) && (
            <p role="alert" className="col-span-2 text-sm text-destructive">
              <ErrorMessage error={(shoes.error || lastUsedShoe.error)!} />
            </p>
          )}
          <FormField label={t('newActivity.startedAt')}>
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
          </FormField>
          <FormField label={t('newActivity.distance')}>
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
          </FormField>
          <FormField label={t('newActivity.duration')}>
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
          </FormField>
          <FormField label={t('newActivity.elevation')}>
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
          </FormField>
          {me?.hrConsent && (
            <FormField label={t('newActivity.avgHr')}>
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
            </FormField>
          )}
        </div>
        {fromFile && (
          <MultisportEditor
            key={`${file?.name}:${file?.lastModified}`}
            activity={multisportView(
              {
                ...fromFile,
                partsEdited: false,
                originalParts: fromFile.parts,
                originalSport: fromFile.sport,
              },
              split,
            )}
            onChange={setSplit}
            disabled={save.isPending}
          />
        )}
        <TagPicker value={tags} onChange={setTags} disabled={save.isPending} />
        {fromFile && (
          <p className="text-xs text-muted-foreground">
            {fromFile.device
              ? t('newActivity.lockedFrom', { device: fromFile.device })
              : t('newActivity.locked')}
          </p>
        )}

        <Button
          type="submit"
          disabled={
            save.isPending ||
            shoes.isPending ||
            !!shoes.error ||
            lastUsedShoe.isPending ||
            !!lastUsedShoe.error ||
            (locked && !fromFile) ||
            preview.isFetching
          }
        >
          {save.isPending ? t('newActivity.saving') : t('newActivity.save')}
        </Button>
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
      </form>

      <Modal
        ref={shoeDialog}
        aria-labelledby="add-shoe-title"
        onCancel={(event) => {
          if (addShoe.isPending) event.preventDefault()
        }}
      >
        <Heading level={2} id="add-shoe-title">
          {t('shoes.add')}
        </Heading>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            if (!addShoe.isPending) addShoe.mutate()
          }}
          className="mt-5 space-y-4"
        >
          <EquipmentNameFields
            name={shoeName}
            brand={shoeBrand}
            onNameChange={setShoeName}
            onBrandChange={setShoeBrand}
            nameLabel={t('shoes.name')}
            disabled={addShoe.isPending}
          />
          {addShoe.error && (
            <p role="alert" className="text-sm text-destructive">
              <ErrorMessage error={addShoe.error} />
            </p>
          )}
          <div className="flex flex-wrap justify-end gap-3 pt-2">
            <Button
              type="button"
              variant="secondary-outline"
              disabled={addShoe.isPending}
              onClick={() => shoeDialog.current?.close()}
            >
              {t('shoes.cancel')}
            </Button>
            <Button type="submit" disabled={addShoe.isPending}>
              {addShoe.isPending ? t('newActivity.saving') : t('newActivity.save')}
            </Button>
          </div>
        </form>
      </Modal>

      <Modal ref={uploadDialog} aria-labelledby="upload-title">
        <Heading level={2} id="upload-title">
          {t('newActivity.upload')}
        </Heading>
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
        <FormField label={t('newActivity.file')} className="mt-5">
          {(id) => (
            <Input
              key={dialogKey}
              id={id}
              type="file"
              accept=".fit"
              onChange={(e) => setPendingFile(e.currentTarget.files?.[0] ?? null)}
            />
          )}
        </FormField>
        {/* The full consent only while it is missing; once given, one line is enough */}
        {me?.hrConsent ? (
          <p className="mt-4 text-sm text-muted-foreground">
            <Trans
              i18nKey="newActivity.hrSaved"
              components={{
                settings: <Link to="/app/settings" className="underline underline-offset-4" />,
              }}
            />
          </p>
        ) : (
          <HeartRateConsent className="mt-4" />
        )}
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Button
            type="button"
            variant="secondary-outline"
            onClick={() => uploadDialog.current?.close()}
          >
            {t('newActivity.cancel')}
          </Button>
          <Button
            type="button"
            disabled={!pendingFile}
            onClick={() => {
              setFile(pendingFile)
              setSplit(undefined)
              setSport(null)
              save.reset()
              uploadDialog.current?.close()
            }}
          >
            {t('newActivity.useFile')}
          </Button>
        </div>
      </Modal>
    </section>
  )
}
