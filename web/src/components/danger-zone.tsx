import { FormField } from '@/components/ui/form-field'
import { Modal } from '@/components/ui/modal'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { refreshActivityQueries } from '@/lib/activity-query'
import { api } from '@/lib/api'
import { useSessionChanged } from '@/lib/auth'

export function DangerZone() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const sessionChanged = useSessionChanged()
  const [target, setTarget] = useState<'activities' | 'account'>('activities')
  const dialog = useRef<HTMLDialogElement>(null)
  const [word, setWord] = useState<string | null>(null)
  const [confirmation, setConfirmation] = useState('')
  // Kontoradering bekräftas med lösenordet (API:t kontrollerar det mot Supabase) i stället för löparordet.
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

  useEffect(() => {
    if (word !== null) dialog.current?.showModal()
    else dialog.current?.close()
  }, [word])

  function openDialog(target: 'activities' | 'account') {
    setTarget(target)
    const words = t('dangerZone.words', { returnObjects: true })
    setWord(words[Math.floor(Math.random() * words.length)])
    setConfirmation('')
    setPassword('')
    remove.reset()
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (confirmed && !remove.isPending) remove.mutate()
  }

  return (
    <section
      className="mt-10 rounded-lg border border-destructive/40 p-4"
      aria-labelledby="danger-zone-title"
    >
      <h2 id="danger-zone-title" className="font-semibold text-destructive">
        {t('dangerZone.title')}
      </h2>
      <p className="mt-2 text-sm text-muted-foreground">{t('dangerZone.description')}</p>
      <Button variant="destructive" className="mt-4" onClick={() => openDialog('activities')}>
        {t('dangerZone.delete')}
      </Button>
      <div className="mt-6 border-t border-destructive/20 pt-4">
        <p className="text-sm text-muted-foreground">{t('dangerZone.accountDescription')}</p>
        <Button variant="destructive" className="mt-4" onClick={() => openDialog('account')}>
          {t('dangerZone.deleteAccount')}
        </Button>
      </div>
      <Modal
        ref={dialog}
        aria-labelledby="delete-all-title"
        aria-describedby="delete-all-warning"
        onCancel={(event) => {
          event.preventDefault()
          if (!remove.isPending) setWord(null)
        }}
      >
        <h2 id="delete-all-title" className="text-xl font-semibold">
          {target === 'account'
            ? t('dangerZone.accountConfirmTitle')
            : t('dangerZone.confirmTitle')}
        </h2>
        <p id="delete-all-warning" className="mt-3 text-sm leading-relaxed text-muted-foreground">
          {target === 'account' ? t('dangerZone.accountWarning') : t('dangerZone.warning')}
        </p>
        <form onSubmit={onSubmit} className="mt-5">
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
          {remove.error && (
            <p role="alert" className="mt-3 text-sm text-destructive">
              <ErrorMessage error={remove.error} />
            </p>
          )}
          <div className="mt-6 flex flex-wrap justify-end gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={remove.isPending}
              onClick={() => setWord(null)}
            >
              {t('dangerZone.cancel')}
            </Button>

            <Button type="submit" variant="destructive" disabled={!confirmed || remove.isPending}>
              {remove.isPending
                ? t('dangerZone.deleting')
                : target === 'account'
                  ? t('dangerZone.deleteAccount')
                  : t('dangerZone.delete')}
            </Button>
          </div>
        </form>
      </Modal>
      {remove.isSuccess && (
        <p role="status" className="mt-3 text-sm">
          {t('dangerZone.success')}
        </p>
      )}
    </section>
  )
}
