import { queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'
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

export function GarminConnect() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const status = useQuery(garminStatusQuery)
  const [result, setResult] = useState(() => {
    const value = new URLSearchParams(window.location.search).get('garmin')
    return results.find((r) => r === value) as ConnectResult | undefined
  })
  const [confirming, setConfirming] = useState(false)
  const dialog = useRef<HTMLDialogElement>(null)

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

  useEffect(() => {
    if (confirming) dialog.current?.showModal()
    else dialog.current?.close()
  }, [confirming])

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
      {result && (
        <p role="status" className={result === 'connected' ? undefined : 'text-destructive'}>
          {t(`garmin.result.${result}`)}
        </p>
      )}
      {connected ? (
        <>
          <p>{t('garmin.connected')}</p>
          {!activityExport && <p className="text-destructive">{t('garmin.noActivityExport')}</p>}
          <p className="text-muted-foreground">{t('garmin.privateForNow')}</p>
          <Button
            variant="outline"
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
      <Modal
        ref={dialog}
        aria-labelledby="garmin-disconnect-title"
        aria-describedby="garmin-disconnect-warning"
        onCancel={(event) => {
          event.preventDefault()
          if (!disconnect.isPending) setConfirming(false)
        }}
      >
        <h2 id="garmin-disconnect-title" className="text-xl font-semibold">
          {t('garmin.disconnectTitle')}
        </h2>
        <p id="garmin-disconnect-warning" className="mt-3 text-sm text-muted-foreground">
          {t('garmin.disconnectWarning')}
        </p>
        {disconnect.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={disconnect.error} />
          </p>
        )}
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Button
            variant="outline"
            disabled={disconnect.isPending}
            onClick={() => setConfirming(false)}
          >
            {t('dangerZone.cancel')}
          </Button>
          <Button
            variant="destructive"
            disabled={disconnect.isPending}
            onClick={() => disconnect.mutate()}
          >
            {t('garmin.disconnect')}
          </Button>
        </div>
      </Modal>
    </div>
  )
}
