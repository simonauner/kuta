import { FormField } from '@/components/ui/form-field'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { api, ApiError } from '@/lib/api'

type StravaImportResult = {
  imported: number
  duplicates: number
  failedActivityIds: string[]
  equipmentImported: number
  equipmentLinked: number
}

type StravaImportProgress = {
  processed: number
  total: number
  imported: number
  duplicates: number
  failed: number
  result: StravaImportResult | null
}

export function StravaImport() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [progress, setProgress] = useState<StravaImportProgress | null>(null)
  const [file, setFile] = useState<File | null>(null)
  const refreshImportedData = () =>
    queryClient.invalidateQueries({
      predicate: ({ queryKey }) =>
        ['activities', 'activity', 'equipment', 'equipmentLastUsedShoe'].includes(
          String(queryKey[0]),
        ),
    })
  const upload = useMutation({
    mutationFn: async (file: File): Promise<StravaImportResult> => {
      setProgress(null)
      const form = new FormData()
      form.append('file', file)
      let result: StravaImportResult | null = null
      try {
        await api.uploadStream<StravaImportProgress>(
          '/api/activities/import/strava',
          form,
          (message) => {
            setProgress(message)
            if (message.result) result = message.result
          },
        )
      } catch (error) {
        if (error instanceof ApiError && error.status !== 502) throw error
        throw new ApiError(502, 'Import interrupted', 'stravaImportInterrupted')
      }
      if (!result) throw new ApiError(502, 'Import interrupted', 'stravaImportInterrupted')
      return result
    },
    onSuccess: refreshImportedData,
    onError: refreshImportedData,
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
      <FormField label={t('settings.stravaFile')}>
        {(id) => (
          <Input
            id={id}
            type="file"
            accept=".zip"
            disabled={upload.isPending}
            onChange={(e) => {
              setFile(e.currentTarget.files?.[0] ?? null)
              upload.reset()
            }}
          />
        )}
      </FormField>
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
        <div className="mt-3 text-sm text-muted-foreground">
          <p role="status">
            {progress
              ? t('settings.importProgress', {
                  processed: progress.processed,
                  total: progress.total,
                })
              : t('settings.importPreparing')}
          </p>
          {progress && (
            <>
              <progress
                className="mt-2 h-2 w-full appearance-none overflow-hidden rounded-full bg-muted [&::-moz-progress-bar]:bg-primary [&::-webkit-progress-bar]:bg-muted [&::-webkit-progress-value]:bg-primary"
                aria-label={t('settings.importing')}
                max={Math.max(progress.total, 1)}
                value={progress.processed}
              />
              <p className="mt-2">{t('settings.importResult', progress)}</p>
            </>
          )}
          <p className="mt-2">{t('settings.importWait')}</p>
        </div>
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
          <p>
            {t('settings.importEquipmentResult', {
              imported: upload.data.equipmentImported,
              linked: upload.data.equipmentLinked,
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
