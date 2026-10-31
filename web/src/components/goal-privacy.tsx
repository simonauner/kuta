import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { api } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'

// Gäller alla mål direkt; visas med förlopp på profilen för dem som får se dem.
export function GoalPrivacy() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const queryClient = useQueryClient()
  const save = useMutation({
    mutationFn: (privacy: Me['goalPrivacy']) =>
      api.send<Me>('PUT', '/api/me/goal-privacy', { privacy }),
    onSuccess: (updated) => queryClient.setQueryData(meQuery.queryKey, updated),
  })
  const labels = {
    private: t('social.private'),
    followers: t('social.followersVisibility'),
    public: t('social.public'),
  }
  return (
    <section aria-labelledby="goal-privacy-heading" className="mt-8 border-t pt-6">
      <h2 id="goal-privacy-heading" className="text-lg font-semibold">
        {t('goalPrivacy.title')}
      </h2>
      <label className="mt-4 block space-y-2 text-sm font-medium">
        <span>{t('goalPrivacy.label')}</span>
        <select
          className="block w-full rounded-md border bg-background px-3 py-2"
          value={me?.goalPrivacy ?? 'private'}
          disabled={!me || save.isPending}
          aria-describedby="goal-privacy-help"
          onChange={(event) => save.mutate(event.target.value as Me['goalPrivacy'])}
        >
          {Object.entries(labels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <p id="goal-privacy-help" className="mt-2 text-sm text-muted-foreground">
        {t('goalPrivacy.help')}
      </p>
      {save.isSuccess && (
        <p role="status" className="mt-2 text-sm text-muted-foreground">
          {t('goalPrivacy.saved')}
        </p>
      )}
      {save.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={save.error} />
        </p>
      )}
    </section>
  )
}
