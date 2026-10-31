import { useRef } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Check } from 'lucide-react'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'
import { api } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'

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

  return (
    <section aria-labelledby="activity-privacy-heading" className="mt-8 border-t pt-6">
      <h2 id="activity-privacy-heading" className="text-lg font-semibold">
        {t('activityPrivacy.title')}
      </h2>
      <label className="mt-4 block space-y-2 text-sm font-medium">
        <span>{t('activityPrivacy.default')}</span>
        <select
          className="block w-full rounded-md border bg-background px-3 py-2"
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
        </select>
      </label>
      <p id="activity-privacy-help" className="mt-2 text-sm text-muted-foreground">
        {t('activityPrivacy.help')}
      </p>
      <p className="mt-2 text-sm text-muted-foreground">{t('social.sharingHint')}</p>
      <div className="mt-4 flex items-center gap-2">
        <Button
          variant="outline"
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
      <Modal
        ref={dialog}
        aria-labelledby="apply-privacy-title"
        aria-describedby="apply-privacy-help"
        onCancel={(event) => {
          if (save.isPending) event.preventDefault()
        }}
      >
        <h3 id="apply-privacy-title" className="text-lg font-semibold">
          {t('activityPrivacy.applyAll')}
        </h3>
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
            variant="outline"
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
