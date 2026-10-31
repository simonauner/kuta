import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'

type StravaImportResult = { imported: number; duplicates: number; failedActivityIds: string[] }

export function StravaImport() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)
  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<StravaImportResult>('/api/activities/import/strava', form)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
    onError: () => queryClient.invalidateQueries({ queryKey: ['activities'] }),
  })

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!file) return
    const form = e.currentTarget
    upload.mutate(file, {
      onSuccess: () => {
        form.reset()
        setFile(null)
      },
    })
  }

  return (
    <form onSubmit={onSubmit} className="mt-4">
      <Label htmlFor="strava-file" className="mb-2">
        {t('settings.stravaFile')}
      </Label>
      <Input
        id="strava-file"
        type="file"
        accept=".zip"
        disabled={upload.isPending}
        onChange={(e) => {
          setFile(e.currentTarget.files?.[0] ?? null)
          upload.reset()
        }}
      />
      <p className="mt-2 text-xs text-muted-foreground">{t('settings.importHelp')}</p>
      <Button type="submit" className="mt-4" disabled={!file || upload.isPending}>
        {upload.isPending ? t('settings.importing') : t('settings.import')}
      </Button>
      {upload.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={upload.error} />
        </p>
      )}
      {upload.isPending && (
        <p role="status" className="mt-2 text-sm text-muted-foreground">
          {t('settings.importWait')}
        </p>
      )}
      {upload.data && (
        <div role="status" className="mt-4 text-sm">
          <p>
            {t('settings.importResult', {
              imported: upload.data.imported,
              duplicates: upload.data.duplicates,
              failed: upload.data.failedActivityIds.length,
            })}
          </p>
          {upload.data.failedActivityIds.length > 0 && (
            <details className="mt-2">
              <summary className="cursor-pointer">{t('settings.failedImports')}</summary>
              <p className="mt-1 break-words text-muted-foreground">
                {upload.data.failedActivityIds.join(', ')}
              </p>
            </details>
          )}
        </div>
      )}
    </form>
  )
}
