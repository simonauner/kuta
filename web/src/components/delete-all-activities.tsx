import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'

export function DeleteAllActivities() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const dialog = useRef<HTMLDialogElement>(null)
  const [word, setWord] = useState<string | null>(null)
  const [confirmation, setConfirmation] = useState('')
  const confirmed = word !== null && confirmation.trim().toUpperCase() === word
  const remove = useMutation({
    mutationFn: () => api.send('DELETE', '/api/activities'),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        predicate: (query) =>
          ['activities', 'activity', 'records'].includes(String(query.queryKey[0])),
      })
      setWord(null)
    },
  })

  useEffect(() => {
    if (word !== null) dialog.current?.showModal()
    else dialog.current?.close()
  }, [word])

  function openDialog() {
    const words = t('dangerZone.words', { returnObjects: true })
    setWord(words[Math.floor(Math.random() * words.length)])
    setConfirmation('')
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
      <Button variant="destructive" className="mt-4" onClick={openDialog}>
        {t('dangerZone.delete')}
      </Button>
      <dialog
        ref={dialog}
        aria-labelledby="delete-all-title"
        aria-describedby="delete-all-warning"
        onCancel={(event) => {
          event.preventDefault()
          if (!remove.isPending) setWord(null)
        }}
        className="fixed inset-0 m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-md overflow-y-auto rounded-xl border bg-background p-6 text-foreground shadow-xl backdrop:bg-black/70"
      >
        <h2 id="delete-all-title" className="text-xl font-semibold">
          {t('dangerZone.confirmTitle')}
        </h2>
        <p id="delete-all-warning" className="mt-3 text-sm leading-relaxed text-muted-foreground">
          {t('dangerZone.warning')}
        </p>
        <form onSubmit={onSubmit} className="mt-5">
          <Label htmlFor="delete-confirmation" className="block leading-relaxed">
            {t('dangerZone.confirmLabel', { word })}
          </Label>
          <Input
            id="delete-confirmation"
            className="mt-2"
            value={confirmation}
            onChange={(event) => setConfirmation(event.target.value)}
            disabled={remove.isPending}
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
          />
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
              {remove.isPending ? t('dangerZone.deleting') : t('dangerZone.delete')}
            </Button>
          </div>
        </form>
      </dialog>
      {remove.isSuccess && (
        <p role="status" className="mt-3 text-sm">
          {t('dangerZone.success')}
        </p>
      )}
    </section>
  )
}
