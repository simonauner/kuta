import { FormField } from '@/components/ui/form-field'
import { ErrorMessage } from '@/components/error-message'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { type ChangeEvent } from 'react'
import { api } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'
import { clearHeartRateQueries } from '@/lib/activity-query'

// Puls är hälsodata: sparas bara med uttryckligt samtycke, som går att återkalla (då raderas all sparad puls).
export function HeartRateConsent({ className }: { className?: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)

  const change = useMutation({
    mutationFn: (consent: boolean) =>
      api.send<Me>(consent ? 'PUT' : 'DELETE', '/api/me/hr-consent'),
    onSuccess: async (updated) => {
      if (!updated.hrConsent) await clearHeartRateQueries(queryClient)
      queryClient.setQueryData(['me'], updated)
      await queryClient.invalidateQueries({ queryKey: ['activities'] })
    },
  })

  function onChange(e: ChangeEvent<HTMLInputElement>) {
    const consent = e.currentTarget.checked
    if (!consent && !window.confirm(t('consent.revoke'))) return
    change.mutate(consent)
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
      {change.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={change.error} />
        </p>
      )}
    </div>
  )
}
