import { FormField } from '@/components/ui/form-field'
import { ErrorMessage } from '@/components/error-message'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ChangeEvent } from 'react'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { api } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'
import { clearHeartRateQueries } from '@/lib/activity-query'

// Heart rate is health data: stored only with explicit consent, which can be revoked (deleting all stored heart rate).
export function HeartRateConsent({ className }: { className?: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)
  const [confirmingRevoke, setConfirmingRevoke] = useState(false)

  const change = useMutation({
    mutationFn: (consent: boolean) =>
      api.send<Me>(consent ? 'PUT' : 'DELETE', '/api/me/hr-consent'),
    onSuccess: async (updated) => {
      if (!updated.hrConsent) await clearHeartRateQueries(queryClient)
      queryClient.setQueryData(['me'], updated)
      await queryClient.invalidateQueries({ queryKey: ['activities'] })
      setConfirmingRevoke(false)
    },
  })

  function onChange(e: ChangeEvent<HTMLInputElement>) {
    const consent = e.currentTarget.checked
    if (consent) change.mutate(true)
    else {
      change.reset()
      setConfirmingRevoke(true)
    }
  }

  return (
    <div className={`rounded-lg border p-4 ${className ?? ''}`}>
      <FormField
        layout="choice"
        className="items-start"
        labelClassName="font-normal leading-relaxed"
        label={
          <>
            <span className="font-medium">{t('consent.title')}</span>{' '}
            <span className="text-muted-foreground">{t('consent.description')}</span>
          </>
        }
      >
        {(id) => (
          <input
            id={id}
            type="checkbox"
            className="mt-1 size-4 accent-primary"
            checked={me?.hrConsent ?? false}
            disabled={change.isPending}
            onChange={onChange}
          />
        )}
      </FormField>
      <ConfirmationModal
        open={confirmingRevoke}
        title={t('consent.revokeTitle')}
        description={t('consent.revoke')}
        confirmLabel={t('consent.revokeConfirm')}
        cancelLabel={t('common.cancel')}
        pending={change.isPending}
        error={change.error}
        onConfirm={() => change.mutate(false)}
        onClose={() => setConfirmingRevoke(false)}
      />
      {!confirmingRevoke && change.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={change.error} />
        </p>
      )}
    </div>
  )
}
