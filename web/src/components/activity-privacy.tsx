import { FormField } from '@/components/ui/form-field'
import { useRef } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Check } from 'lucide-react'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'
import { api } from '@/lib/api'
import { SharingHints } from '@/components/privacy-places'
import { meQuery, type Me } from '@/lib/auth'
import { Select } from '@/components/ui/select'
import { Heading } from '@/components/ui/heading'

export function ActivityPrivacy() {
  const { t } = useTranslation()
  const { data: me, isFetching } = useQuery({ ...meQuery, staleTime: 0 })
  const queryClient = useQueryClient()
  const dialog = useRef<HTMLDialogElement>(null)
  const privacy = me?.defaultActivityPrivacy ?? 'private'
  const labels = {
    private: t('social.private'),
    followers: t('social.followersVisibility'),
    public: t('social.public'),
  }
  const save = useMutation({
    mutationFn: (request: { privacy: Me['defaultActivityPrivacy']; applyToAll: boolean }) =>
      api.send<Me>('PUT', '/api/me/activity-privacy', request),
    onSuccess: async (updated, request) => {
      queryClient.setQueryData(meQuery.queryKey, updated)
      dialog.current?.close()
      if (request.applyToAll) {
        await Promise.all(
          ['activities', 'activity', 'feed'].map((key) =>
            queryClient.invalidateQueries({ queryKey: [key] }),
          ),
        )
      }
    },
  })

  const publicProfile = useMutation({
    mutationFn: (enabled: boolean) => api.send<Me>('PUT', '/api/me/public-profile', { enabled }),
    onSuccess: (updated) => queryClient.setQueryData(meQuery.queryKey, updated),
  })

  return (
    <section aria-labelledby="activity-privacy-heading" className="mt-8 border-t pt-6">
      <Heading level={2} id="activity-privacy-heading">
        {t('activityPrivacy.title')}
      </Heading>
      <FormField label={t('activityPrivacy.default')} className="text-sm font-medium mt-4">
        {(id) => (
          <Select
            id={id}
            value={privacy}
            disabled={!me || save.isPending}
            aria-describedby="activity-privacy-help"
            onChange={(event) =>
              save.mutate({
                privacy: event.target.value as Me['defaultActivityPrivacy'],
                applyToAll: false,
              })
            }
          >
            {Object.entries(labels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </Select>
        )}
      </FormField>
      <p id="activity-privacy-help" className="mt-2 text-sm text-muted-foreground">
        {t('activityPrivacy.help')}
      </p>
      <p className="mt-2 text-sm text-muted-foreground">{t('social.sharingHint')}</p>
      <SharingHints privacy={privacy} />
      <div className="mt-4 flex items-center gap-2">
        <Button
          variant="secondary-outline"
          disabled={!me || isFetching || save.isPending || me.allActivitiesMatchDefaultPrivacy}
          onClick={() => {
            save.reset()
            dialog.current?.showModal()
          }}
        >
          {t('activityPrivacy.applyAll')}
        </Button>
        {me?.allActivitiesMatchDefaultPrivacy && !isFetching && (
          <span role="status">
            <Check className="size-5 text-primary" aria-hidden="true" />
            <span className="sr-only">{t('activityPrivacy.allMatch')}</span>
          </span>
        )}
      </div>
      {save.isSuccess && (
        <p role="status" className="mt-2 text-sm text-muted-foreground">
          {t(save.variables.applyToAll ? 'activityPrivacy.applied' : 'activityPrivacy.saved')}
        </p>
      )}
      {save.error && !save.variables?.applyToAll && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={save.error} />
        </p>
      )}
      <FormField
        layout="choice"
        className="mt-6 items-start"
        labelClassName="font-normal leading-relaxed"
        label={
          <>
            <span className="font-medium">{t('activityPrivacy.publicProfile')}</span>{' '}
            <span className="text-muted-foreground">{t('activityPrivacy.publicProfileHelp')}</span>
          </>
        }
      >
        {(id) => (
          <input
            id={id}
            type="checkbox"
            className="mt-1 size-4 accent-primary"
            checked={me?.publicProfile ?? false}
            disabled={!me || publicProfile.isPending}
            onChange={(event) => publicProfile.mutate(event.currentTarget.checked)}
          />
        )}
      </FormField>
      {publicProfile.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={publicProfile.error} />
        </p>
      )}
      <Modal
        ref={dialog}
        aria-labelledby="apply-privacy-title"
        aria-describedby="apply-privacy-help"
        onCancel={(event) => {
          if (save.isPending) event.preventDefault()
        }}
      >
        <Heading level={3} id="apply-privacy-title">
          {t('activityPrivacy.applyAll')}
        </Heading>
        <p id="apply-privacy-help" className="mt-3 text-sm text-muted-foreground">
          {t('activityPrivacy.confirm', { visibility: labels[privacy] })}
        </p>
        {save.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <Button
            variant="secondary-outline"
            disabled={save.isPending}
            onClick={() => dialog.current?.close()}
          >
            {t('activity.cancel')}
          </Button>
          <Button
            disabled={save.isPending}
            onClick={() => save.mutate({ privacy, applyToAll: true })}
          >
            {t(save.isPending ? 'activityPrivacy.applying' : 'activityPrivacy.applyAll')}
          </Button>
        </div>
      </Modal>
    </section>
  )
}
