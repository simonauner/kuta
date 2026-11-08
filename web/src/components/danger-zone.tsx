import { FormField } from '@/components/ui/form-field'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { refreshActivityQueries } from '@/lib/activity-query'
import { api } from '@/lib/api'
import { useSessionChanged } from '@/lib/auth'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'

export function DangerZone() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const sessionChanged = useSessionChanged()
  const [target, setTarget] = useState<'activities' | 'account'>('activities')
  const [word, setWord] = useState<string | null>(null)
  const [confirmation, setConfirmation] = useState('')
  // Account deletion is confirmed with the password (the API checks it against Supabase) instead of the runner word.
  const [password, setPassword] = useState('')
  const confirmed =
    word !== null &&
    (target === 'account' ? password !== '' : confirmation.trim().toUpperCase() === word)
  const remove = useMutation({
    mutationFn: () =>
      target === 'account'
        ? api.send('DELETE', '/api/me', { password })
        : api.send('DELETE', '/api/activities'),
    onSuccess: async () => {
      if (target === 'account') {
        await sessionChanged()
        return
      }
      await refreshActivityQueries(queryClient)
      setWord(null)
    },
  })

  function openDialog(target: 'activities' | 'account') {
    setTarget(target)
    const words = t('dangerZone.words', { returnObjects: true })
    setWord(words[Math.floor(Math.random() * words.length)])
    setConfirmation('')
    setPassword('')
    remove.reset()
  }

  return (
    <section
      className={card({ tone: 'destructive', className: 'mt-10' })}
      aria-labelledby="danger-zone-title"
    >
      <Heading level={2} id="danger-zone-title" className="text-destructive">
        {t('dangerZone.title')}
      </Heading>
      <p className="mt-2 text-sm text-muted-foreground">{t('dangerZone.description')}</p>
      <Button
        variant="destructive-outline"
        className="mt-4"
        onClick={() => openDialog('activities')}
      >
        {t('dangerZone.delete')}
      </Button>
      <div className="mt-6 border-t border-destructive/20 pt-4">
        <p className="text-sm text-muted-foreground">{t('dangerZone.accountDescription')}</p>
        <Button
          variant="destructive-outline"
          className="mt-4"
          onClick={() => openDialog('account')}
        >
          {t('dangerZone.deleteAccount')}
        </Button>
      </div>
      <ConfirmationModal
        open={word !== null}
        title={
          target === 'account' ? t('dangerZone.accountConfirmTitle') : t('dangerZone.confirmTitle')
        }
        description={
          target === 'account' ? t('dangerZone.accountWarning') : t('dangerZone.warning')
        }
        confirmLabel={
          remove.isPending
            ? t('dangerZone.deleting')
            : target === 'account'
              ? t('dangerZone.deleteAccount')
              : t('dangerZone.delete')
        }
        cancelLabel={t('dangerZone.cancel')}
        confirmDisabled={!confirmed}
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => remove.mutate()}
        onClose={() => setWord(null)}
      >
        {target === 'activities' && (
          <FormField label={t('dangerZone.confirmLabel', { word })}>
            {(id) => (
              <Input
                id={id}
                value={confirmation}
                onChange={(event) => setConfirmation(event.target.value)}
                disabled={remove.isPending}
                autoComplete="off"
                autoCapitalize="characters"
                spellCheck={false}
              />
            )}
          </FormField>
        )}
        {target === 'account' && (
          <FormField label={t('dangerZone.passwordLabel')}>
            {(id) => (
              <Input
                id={id}
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                disabled={remove.isPending}
                autoComplete="current-password"
              />
            )}
          </FormField>
        )}
      </ConfirmationModal>
      {remove.isSuccess && (
        <p role="status" className="mt-3 text-sm">
          {t('dangerZone.success')}
        </p>
      )}
    </section>
  )
}
