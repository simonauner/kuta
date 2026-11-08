import { Checkbox } from '@/components/ui/checkbox'
import { PrivacySelect, usePrivacyLabel } from '@/components/privacy-select'
import { FormField } from '@/components/ui/form-field'
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from '@tanstack/react-router'
import { Check } from 'lucide-react'
import { ErrorText } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { api } from '@/lib/api'
import { SharingHints } from '@/components/privacy-places'
import { meQuery, type Me } from '@/lib/auth'
import { Heading } from '@/components/ui/heading'

// New and imported activities silently get the default visibility, so the forms that create them say which it is.
export function DefaultPrivacyNote({ imported = false }: { imported?: boolean }) {
  const { t } = useTranslation()
  const privacy = useQuery(meQuery).data?.defaultActivityPrivacy ?? 'private'
  const label = usePrivacyLabel()
  return (
    <p className="text-sm">
      <Trans
        i18nKey={imported ? 'activityPrivacy.importNote' : 'activityPrivacy.newNote'}
        values={{ visibility: label(privacy) }}
        components={{ strong: <strong /> }}
      />
      {!imported && (
        <>
          {' '}
          <Link to="/app/settings/profile" className="underline underline-offset-4">
            {t('activityPrivacy.changeDefault')}
          </Link>
        </>
      )}
      <SharingHints privacy={privacy} />
    </p>
  )
}

export function ActivityPrivacy() {
  const { t } = useTranslation()
  const { data: me, isFetching } = useQuery({ ...meQuery, staleTime: 0 })
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const privacy = me?.defaultActivityPrivacy ?? 'private'
  const label = usePrivacyLabel()
  const save = useMutation({
    mutationFn: (request: { privacy: Me['defaultActivityPrivacy']; applyToAll: boolean }) =>
      api.send<Me>('PUT', '/api/me/activity-privacy', request),
    onSuccess: async (updated, request) => {
      queryClient.setQueryData(meQuery.queryKey, updated)
      setConfirming(false)
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
          <PrivacySelect
            id={id}
            value={privacy}
            disabled={!me || save.isPending}
            aria-describedby="activity-privacy-help"
            onChange={(value) => save.mutate({ privacy: value, applyToAll: false })}
          />
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
          disabled={!me || isFetching || save.isPending || me.activitiesWithOtherPrivacy === 0}
          onClick={() => {
            save.reset()
            setConfirming(true)
          }}
        >
          {t('activityPrivacy.applyAll')}
        </Button>
        {me?.activitiesWithOtherPrivacy === 0 && !isFetching && (
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
        <ErrorText error={save.error} className="mt-2" />
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
          <Checkbox
            id={id}
            className="mt-1"
            checked={me?.publicProfile ?? false}
            disabled={!me || publicProfile.isPending}
            onChange={(event) => publicProfile.mutate(event.currentTarget.checked)}
          />
        )}
      </FormField>
      <ErrorText error={publicProfile.error} className="mt-2" />
      <ConfirmationModal
        open={confirming}
        title={t('activityPrivacy.applyAll')}
        description={t('activityPrivacy.confirm', {
          count: me?.activitiesWithOtherPrivacy ?? 0,
          visibility: label(privacy),
        })}
        confirmLabel={t(save.isPending ? 'activityPrivacy.applying' : 'activityPrivacy.applyAll')}
        cancelLabel={t('activity.cancel')}
        confirmVariant="primary-filled"
        pending={save.isPending}
        error={save.variables?.applyToAll ? save.error : null}
        onConfirm={() => save.mutate({ privacy, applyToAll: true })}
        onClose={() => setConfirming(false)}
      />
    </section>
  )
}
