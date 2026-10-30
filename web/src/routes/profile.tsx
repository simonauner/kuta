import { ErrorMessage } from '@/components/error-message'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery } from '@tanstack/react-query'
import { HeartRateConsent } from '@/components/heart-rate-consent'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import { meQuery, useSessionChanged } from '@/lib/auth'

export function ProfilePage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const sessionChanged = useSessionChanged()
  const logout = useMutation({
    mutationFn: () => api.send('POST', '/api/auth/logout'),
    onSuccess: sessionChanged,
  })

  return (
    <section>
      <h1 className="text-2xl font-semibold tracking-tight">{me?.displayName}</h1>
      <p className="mt-1 text-sm text-muted-foreground">{me?.email}</p>
      <p className="mt-6 text-muted-foreground">{t('profile.description')}</p>
      <HeartRateConsent className="mt-6" />
      <Button
        variant="outline"
        className="mt-8"
        disabled={logout.isPending}
        onClick={() => logout.mutate()}
      >
        {t('profile.logout')}
      </Button>
      {logout.error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          <ErrorMessage error={logout.error} />
        </p>
      )}
    </section>
  )
}
