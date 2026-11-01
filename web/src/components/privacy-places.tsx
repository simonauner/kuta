import { SlowLoading } from '@/components/slow-loading'
import { FormField } from '@/components/ui/form-field'
import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { currentLocation, LocationError } from '@/lib/geolocation'
import { Modal } from '@/components/ui/modal'
import { Button } from '@/components/ui/button'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { Input } from '@/components/ui/input'
import { ErrorMessage } from '@/components/error-message'
import { Link } from '@tanstack/react-router'

const PlaceMap = lazy(() => import('./privacy-place-map'))
type PrivacyPlace = {
  id: string
  name: string
  latitude: number
  longitude: number
  radiusM: number
}
const queryKey = ['privacyPlaces']
const endpoint = '/api/me/privacy-places'

// Below a visibility choice: sign-up is open, so "public" in practice means everyone, and without hidden places
// the map thumbnails can still show roughly where the runner lives.
export function SharingHints({ privacy }: { privacy: 'private' | 'followers' | 'public' }) {
  const { t } = useTranslation()
  const places = useQuery({
    queryKey,
    queryFn: () => api.get<PrivacyPlace[]>(endpoint),
    enabled: privacy !== 'private',
  })
  return (
    <>
      {privacy === 'public' && (
        <span className="mt-2 block text-xs font-normal text-muted-foreground">
          {t('social.publicHint')}
        </span>
      )}
      {privacy !== 'private' && places.data?.length === 0 && (
        <span role="status" className="mt-2 block text-xs font-normal text-destructive">
          {t('social.noHiddenPlaces')}{' '}
          <Link to="/app/settings/privacy-places" className="underline underline-offset-4">
            {t('social.addHiddenPlace')}
          </Link>
        </span>
      )}
    </>
  )
}

export function PrivacyPlaces() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const places = useQuery({ queryKey, queryFn: () => api.get<PrivacyPlace[]>(endpoint) })
  const [editing, setEditing] = useState<PrivacyPlace | null | undefined>(undefined)
  const [deleting, setDeleting] = useState<PrivacyPlace | null>(null)
  const remove = useMutation({
    mutationFn: (id: string) => api.send('DELETE', `${endpoint}/${id}`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey })
      setDeleting(null)
    },
  })
  return (
    <section aria-labelledby="privacy-places-heading">
      <div className="flex items-center justify-between gap-3">
        <h1 id="privacy-places-heading" className="text-2xl font-semibold tracking-tight">
          {t('privacyPlaces.title')}
        </h1>
        <Button variant="secondary-outline" size="sm" onClick={() => setEditing(null)}>
          {t('privacyPlaces.add')}
        </Button>
      </div>
      <p className="mt-2 text-sm text-muted-foreground">{t('privacyPlaces.help')}</p>
      <SlowLoading loading={places.isPending}>
        {places.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={places.error} />
          </p>
        )}
        {places.data?.length === 0 && (
          <p className="mt-4 text-sm text-muted-foreground">{t('privacyPlaces.empty')}</p>
        )}
        <ul className="mt-4 divide-y">
          {places.data?.map((place) => (
            <li key={place.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
              <div className="min-w-0">
                <p className="break-words font-medium">{place.name}</p>
                <p className="text-sm text-muted-foreground">
                  {t('privacyPlaces.radiusValue', {
                    radius: new Intl.NumberFormat(i18n.resolvedLanguage).format(place.radiusM),
                  })}
                </p>
              </div>
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="secondary-outline"
                  aria-label={t('privacyPlaces.editNamed', { name: place.name })}
                  onClick={() => setEditing(place)}
                >
                  {t('privacyPlaces.edit')}
                </Button>
                <Button
                  size="sm"
                  variant="secondary-outline"
                  aria-label={t('privacyPlaces.deleteNamed', { name: place.name })}
                  onClick={() => {
                    setDeleting(place)
                    remove.reset()
                  }}
                >
                  {t('privacyPlaces.delete')}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      </SlowLoading>
      {editing !== undefined && (
        <PlaceEditor place={editing} onClose={() => setEditing(undefined)} />
      )}
      <ConfirmationModal
        open={deleting !== null}
        title={t('privacyPlaces.deleteNamed', { name: deleting?.name ?? '' })}
        description={t('privacyPlaces.deleteHelp')}
        confirmLabel={t(remove.isPending ? 'privacyPlaces.deleting' : 'privacyPlaces.delete')}
        cancelLabel={t('common.cancel')}
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </section>
  )
}

function PlaceEditor({ place, onClose }: { place: PrivacyPlace | null; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const dialog = useRef<HTMLDialogElement>(null)
  const [name, setName] = useState(place?.name ?? '')
  const [latitude, setLatitude] = useState(place ? String(place.latitude) : '')
  const [longitude, setLongitude] = useState(place ? String(place.longitude) : '')
  const [radius, setRadius] = useState(String(place?.radiusM ?? 200))
  const [mapRevision, setMapRevision] = useState(0)
  const location = useMutation({
    mutationFn: () => currentLocation(navigator.geolocation),
    onSuccess: (point) => {
      setLatitude(point.latitude.toFixed(6))
      setLongitude(point.longitude.toFixed(6))
      setMapRevision((revision) => revision + 1)
    },
  })
  const lat = Number(latitude),
    lon = Number(longitude),
    radiusM = Number(radius)
  const point =
    latitude !== '' &&
    longitude !== '' &&
    Number.isFinite(lat) &&
    Number.isFinite(lon) &&
    Math.abs(lat) <= 90 &&
    Math.abs(lon) <= 180
      ? { latitude: lat, longitude: lon }
      : null
  const valid =
    name.trim().length > 0 &&
    name.trim().length <= 100 &&
    point &&
    Number.isInteger(radiusM) &&
    radiusM >= 50 &&
    radiusM <= 5000
  const save = useMutation({
    mutationFn: () =>
      api.send(place ? 'PUT' : 'POST', place ? `${endpoint}/${place.id}` : endpoint, {
        name,
        latitude: lat,
        longitude: lon,
        radiusM,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey })
      onClose()
    },
  })
  useEffect(() => {
    dialog.current?.showModal()
  }, [])
  return (
    <Modal
      ref={dialog}
      className="max-w-xl"
      aria-labelledby="privacy-place-title"
      onClose={onClose}
      onCancel={(event) => {
        if (save.isPending) event.preventDefault()
      }}
    >
      <form
        onSubmit={(event) => {
          event.preventDefault()
          if (valid && !save.isPending && !location.isPending) save.mutate()
        }}
      >
        <h3 id="privacy-place-title" className="text-lg font-semibold">
          {t(place ? 'privacyPlaces.edit' : 'privacyPlaces.add')}
        </h3>
        <fieldset disabled={save.isPending || location.isPending} className="mt-4 space-y-4">
          <FormField label={t('privacyPlaces.name')} className="text-sm font-medium">
            {(id) => (
              <Input
                id={id}
                autoFocus
                required
                maxLength={100}
                value={name}
                placeholder={t('privacyPlaces.namePlaceholder')}
                onChange={(event) => setName(event.target.value)}
              />
            )}
          </FormField>
          <Suspense fallback={<p role="status">{t('privacyPlaces.loadingMap')}</p>}>
            <PlaceMap
              key={mapRevision}
              point={point}
              radiusM={
                Number.isInteger(radiusM) && radiusM >= 50 && radiusM <= 5000 ? radiusM : 200
              }
              onLocate={() => location.mutate()}
              locating={location.isPending}
              onSelect={(next) => {
                if (save.isPending || location.isPending) return
                location.reset()
                setLatitude(next.latitude.toFixed(6))
                setLongitude(next.longitude.toFixed(6))
              }}
            />
          </Suspense>
          {location.isPending && (
            <p role="status" className="text-sm">
              {t('privacyPlaces.locating')}
            </p>
          )}
          {location.error && (
            <p role="alert" className="text-sm text-destructive">
              {t(
                `privacyPlaces.${location.error instanceof LocationError ? location.error.code : 'locationUnavailable'}`,
              )}
            </p>
          )}
          <div className="grid grid-cols-2 gap-3">
            <FormField label={t('privacyPlaces.latitude')} className="text-sm font-medium">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  step="any"
                  min={-90}
                  max={90}
                  required
                  value={latitude}
                  onChange={(event) => {
                    location.reset()
                    setLatitude(event.target.value)
                  }}
                />
              )}
            </FormField>
            <FormField label={t('privacyPlaces.longitude')} className="text-sm font-medium">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  step="any"
                  min={-180}
                  max={180}
                  required
                  value={longitude}
                  onChange={(event) => {
                    location.reset()
                    setLongitude(event.target.value)
                  }}
                />
              )}
            </FormField>
          </div>
          <FormField label={t('privacyPlaces.radius')} className="text-sm font-medium">
            {(id) => (
              <Input
                id={id}
                type="number"
                min={50}
                max={5000}
                step={1}
                required
                value={radius}
                onChange={(event) => setRadius(event.target.value)}
              />
            )}
          </FormField>
        </fieldset>
        <p className="mt-3 text-xs text-muted-foreground">{t('privacyPlaces.mapPrivacy')}</p>
        {save.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <Button
            type="button"
            variant="secondary-outline"
            disabled={save.isPending}
            onClick={onClose}
          >
            {t('privacyPlaces.cancel')}
          </Button>
          <Button type="submit" disabled={!valid || save.isPending || location.isPending}>
            {t(save.isPending ? 'privacyPlaces.saving' : 'privacyPlaces.save')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
