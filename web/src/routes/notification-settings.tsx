import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorText } from '@/components/error-message'
import { PushNotifications } from '@/components/push-notifications'
import { QueryError } from '@/components/query-error'
import { SettingsBreadcrumb } from '@/components/settings-breadcrumb'
import { Card } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { api } from '@/lib/api'

const types = ['followRequest', 'followAccepted', 'groupJoinRequest', 'groupJoinAccepted'] as const
type Preferences = { disabled: string[] }
const preferencesQuery = {
  queryKey: ['push', 'preferences'],
  queryFn: () => api.get<Preferences>('/api/me/push-preferences'),
}

// Notifications: on or off for this device, and which types every device gets.
export function NotificationSettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const preferences = useQuery(preferencesQuery)
  const change = useMutation({
    mutationFn: (disabled: string[]) =>
      api.send<Preferences>('PUT', '/api/me/push-preferences', { disabled }),
    onSuccess: (saved) => queryClient.setQueryData(preferencesQuery.queryKey, saved),
  })
  const disabled = preferences.data?.disabled ?? []
  return (
    <section className="space-y-6">
      <SettingsBreadcrumb page={t('push.pageTitle')} />
      <Heading level={1}>{t('push.pageTitle')}</Heading>
      <PushNotifications />
      <section aria-labelledby="push-types-heading" className="space-y-3">
        <Heading level={2} id="push-types-heading">
          {t('push.typesTitle')}
        </Heading>
        <p className="text-body text-muted-foreground">{t('push.typesDescription')}</p>
        <QueryError query={preferences} />
        {preferences.data && (
          <Card className="space-y-3">
            {types.map((type) => (
              <FormField key={type} layout="choice" label={t(`push.types.${type}`)}>
                {(id) => (
                  <Checkbox
                    id={id}
                    checked={!disabled.includes(type)}
                    disabled={change.isPending}
                    onChange={(e) =>
                      change.mutate(
                        e.currentTarget.checked
                          ? disabled.filter((d) => d !== type)
                          : [...disabled, type],
                      )
                    }
                  />
                )}
              </FormField>
            ))}
          </Card>
        )}
        <ErrorText error={change.error} />
      </section>
    </section>
  )
}
