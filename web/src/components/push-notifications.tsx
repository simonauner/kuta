import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorText } from '@/components/error-message'
import { Card } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { FormField } from '@/components/ui/form-field'
import { api, ApiError } from '@/lib/api'

const supported =
  typeof window !== 'undefined' &&
  'serviceWorker' in navigator &&
  'PushManager' in window &&
  'Notification' in window

async function currentSubscription() {
  const registration = await navigator.serviceWorker.getRegistration()
  return { registration, subscription: (await registration?.pushManager.getSubscription()) ?? null }
}

function save(subscription: PushSubscription, locale: string) {
  const { endpoint, keys } = subscription.toJSON()
  return api.send('PUT', '/api/me/push-subscriptions', {
    endpoint,
    p256dh: keys?.p256dh,
    auth: keys?.auth,
    locale,
  })
}

// Stops notifications to this browser, for example before another account signs in on it.
export async function unsubscribeThisDevice() {
  if (!supported) return
  const { subscription } = await currentSubscription()
  if (!subscription) return
  await api.send('DELETE', '/api/me/push-subscriptions', { endpoint: subscription.endpoint })
  await subscription.unsubscribe()
}

// Web push for this browser. Every device chooses for itself; the subscription follows the sign-in session.
export function PushNotifications({ className }: { className?: string }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const locale = i18n.resolvedLanguage === 'sv' ? 'sv' : 'en'
  const key = useQuery({
    queryKey: ['push', 'key'],
    queryFn: () => api.get<{ publicKey: string | null }>('/api/push/key'),
    enabled: supported,
  })
  const state = useQuery({
    queryKey: ['push', 'subscription'],
    queryFn: currentSubscription,
    enabled: supported,
  })
  const subscription = state.data?.subscription ?? null

  // Moves an existing subscription to the current session and language, which also covers a new sign-in.
  useEffect(() => {
    if (subscription && Notification.permission === 'granted')
      save(subscription, locale).catch(() => {})
  }, [subscription, locale])

  const change = useMutation({
    mutationFn: async (on: boolean) => {
      if (!on) return unsubscribeThisDevice()
      // Asked first, while the click still counts as a user gesture (Safari requires it).
      if ((await Notification.requestPermission()) !== 'granted') return
      const created = await state
        .data!.registration!.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: key.data!.publicKey!,
        })
        // For example a browser without a push service.
        .catch(() => {
          throw new ApiError(0, 'push', 'invalidPushSubscription')
        })
      // Not left on in the browser when the server refuses it.
      await save(created, locale).catch(async (error: unknown) => {
        await created.unsubscribe()
        throw error
      })
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['push', 'subscription'] }),
  })

  // Off on the server, or no service worker (the development server registers none).
  if (supported && (!key.data?.publicKey || (state.data && !state.data.registration))) return null
  const denied = supported && Notification.permission === 'denied'
  return (
    <Card className={className}>
      <FormField
        layout="choice"
        className="items-start"
        labelClassName="font-normal leading-relaxed"
        label={
          <>
            <span className="font-medium">{t('push.title')}</span>{' '}
            <span className="text-muted-foreground">{t('push.description')}</span>
          </>
        }
      >
        {(id) => (
          <Checkbox
            id={id}
            className="mt-1"
            checked={!!subscription}
            disabled={!supported || denied || !state.data || change.isPending}
            onChange={(e) => change.mutate(e.currentTarget.checked)}
          />
        )}
      </FormField>
      {!supported && (
        <p className="mt-2 text-metadata text-muted-foreground">{t('push.unsupported')}</p>
      )}
      {denied && <p className="mt-2 text-metadata text-muted-foreground">{t('push.denied')}</p>}
      <ErrorText error={change.error} className="mt-2" />
    </Card>
  )
}
