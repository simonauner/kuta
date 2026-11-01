import { queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { CircleCheckIcon } from 'lucide-react'
import { Alert, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { refreshActivityQueries } from '@/lib/activity-query'
import { api } from '@/lib/api'
import { meQuery } from '@/lib/auth'

// Garmin requires attribution on statistics and anything else derived from their data; own views only,
// since hasGarminData applies to the signed-in user.
export function GarminDerivedNote({ className }: { className?: string }) {
  const { data: me } = useQuery(meQuery)
  const { t } = useTranslation()
  if (!me?.hasGarminData) return null
  return <p className={`text-xs text-muted-foreground ${className ?? ''}`}>{t('garmin.derived')}</p>
}

type GarminStatus = {
  available: boolean
  connected: boolean
  activityExport: boolean
  connectedAt: string | null
}

const garminStatusQuery = queryOptions({
  queryKey: ['garmin'],
  queryFn: () => api.get<GarminStatus>('/api/garmin'),
})

// The result of Garmin's consent arrives as ?garmin=… when the API redirects the browser back.
const results = ['connected', 'declined', 'failed', 'inUse', 'unavailable'] as const
type ConnectResult = (typeof results)[number]

function resultFromUrl() {
  const value = new URLSearchParams(window.location.search).get('garmin')
  return results.find((r) => r === value) as ConnectResult | undefined
}

// Shown at the top of Settings after Garmin's consent; hidden again once the account is disconnected.
export function GarminConnectedAlert({ className }: { className?: string }) {
  const { t } = useTranslation()
  const status = useQuery(garminStatusQuery)
  const [result] = useState(resultFromUrl)
  if (result !== 'connected' || !status.data?.connected) return null
  return (
    <Alert variant="success" role="status" className={className}>
      <CircleCheckIcon aria-hidden="true" />
      <AlertTitle>{t('garmin.result.connected')}</AlertTitle>
    </Alert>
  )
}

export function GarminConnect() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery(garminStatusQuery)
  const [result, setResult] = useState(resultFromUrl)
  const [confirming, setConfirming] = useState(false)

  const connect = useMutation({
    mutationFn: () => api.send<{ url: string }>('POST', '/api/garmin/connect'),
    onSuccess: ({ url }) => window.location.assign(url),
  })
  const disconnect = useMutation({
    mutationFn: () => api.send('DELETE', '/api/garmin'),
    onSuccess: async () => {
      setConfirming(false)
      setResult(undefined)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: garminStatusQuery.queryKey }),
        queryClient.invalidateQueries({ queryKey: meQuery.queryKey }),
        refreshActivityQueries(queryClient),
      ])
    },
  })

  if (status.error)
    return (
      <p role="alert" className="mt-4 text-sm text-destructive">
        <ErrorMessage error={status.error} />
      </p>
    )
  if (!status.data) return null
  const { available, connected, activityExport } = status.data

  return (
    <div className="mt-4 space-y-3 text-sm">
      {result && result !== 'connected' && (
        <p role="status" className="text-destructive">
          {t(`garmin.result.${result}`)}
        </p>
      )}
      {connected ? (
        <>
          <p>{t('garmin.connected')}</p>
          {!activityExport && <p className="text-destructive">{t('garmin.noActivityExport')}</p>}
          <p className="text-muted-foreground">{t('garmin.privateForNow')}</p>
          <Button
            variant="secondary-outline"
            onClick={() => {
              disconnect.reset()
              setConfirming(true)
            }}
          >
            {t('garmin.disconnect')}
          </Button>
        </>
      ) : available ? (
        <>
          <p className="text-muted-foreground">{t('garmin.description')}</p>
          <p className="text-muted-foreground">{t('garmin.privateForNow')}</p>
          <Button disabled={connect.isPending} onClick={() => connect.mutate()}>
            {t('garmin.connect')}
          </Button>
          {connect.error && (
            <p role="alert" className="text-destructive">
              <ErrorMessage error={connect.error} />
            </p>
          )}
        </>
      ) : (
        <p className="text-muted-foreground">{t('garmin.notAvailable')}</p>
      )}
      <ConfirmationModal
        open={confirming}
        title={t('garmin.disconnectTitle')}
        description={t('garmin.disconnectWarning')}
        confirmLabel={t('garmin.disconnect')}
        cancelLabel={t('common.cancel')}
        pending={disconnect.isPending}
        error={disconnect.error}
        onConfirm={() => disconnect.mutate()}
        onClose={() => setConfirming(false)}
      />
    </div>
  )
}
