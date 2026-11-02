import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { Input } from '@/components/ui/input'
import { api, ApiError } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'
import { cn } from '@/lib/utils'

const sizes = { sm: 'size-8 text-xs', md: 'size-10 text-sm', lg: 'size-16 text-xl' }

export function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => Array.from(word)[0])
    .join('')
    .toLocaleUpperCase()
}

// Decorative: always shown next to the name, which carries the accessible text.
export function Avatar({
  name,
  avatarId,
  size = 'sm',
}: {
  name: string
  avatarId?: string | null
  size?: keyof typeof sizes
}) {
  const [failed, setFailed] = useState<string | null>(null)
  const className = cn('inline-flex shrink-0 rounded-full', sizes[size])
  if (avatarId && failed !== avatarId)
    return (
      <img
        src={`/api/avatars/${avatarId}`}
        alt=""
        aria-hidden="true"
        className={cn(className, 'object-cover')}
        onError={() => setFailed(avatarId)}
      />
    )
  return (
    <span
      aria-hidden="true"
      className={cn(
        className,
        'items-center justify-center bg-muted font-semibold text-muted-foreground',
      )}
    >
      {initials(name)}
    </span>
  )
}

// Square crop scaled to 256 px. Re-encoding drops EXIF, including GPS; the browser applies the orientation first.
async function toAvatarJpeg(file: File) {
  const bitmap = await createImageBitmap(file)
  const side = Math.min(bitmap.width, bitmap.height)
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = 256
  canvas
    .getContext('2d')!
    .drawImage(
      bitmap,
      (bitmap.width - side) / 2,
      (bitmap.height - side) / 2,
      side,
      side,
      0,
      0,
      256,
      256,
    )
  bitmap.close()
  const blob = await new Promise<Blob | null>((resolve) =>
    canvas.toBlob(resolve, 'image/jpeg', 0.85),
  )
  if (!blob) throw new Error('encode')
  return blob
}

export function AvatarSection() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const queryClient = useQueryClient()
  const [confirmDelete, setConfirmDelete] = useState(false)
  const saved = async ({ avatarId }: { avatarId: string | null }) => {
    queryClient.setQueryData<Me | null>(
      meQuery.queryKey,
      (current) => current && { ...current, avatarId },
    )
    setConfirmDelete(false)
    await Promise.all(
      ['runner', 'follows', 'feed'].map((key) =>
        queryClient.invalidateQueries({ queryKey: [key] }),
      ),
    )
  }
  const upload = useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData()
      form.append('file', await toAvatarJpeg(file), 'avatar.jpg')
      return api.upload<{ avatarId: string }>('/api/me/avatar', form)
    },
    onSuccess: saved,
  })
  const remove = useMutation({
    mutationFn: () => api.send<{ avatarId: null }>('DELETE', '/api/me/avatar'),
    onSuccess: saved,
  })

  return (
    <section aria-labelledby="avatar-heading" className="mt-8 border-t pt-6">
      <Heading level={2} id="avatar-heading">
        {t('avatar.title')}
      </Heading>
      <p className="mt-2 text-sm text-muted-foreground">{t('avatar.help')}</p>
      <div className="mt-4 flex flex-wrap items-end gap-4">
        {me?.displayName && <Avatar name={me.displayName} avatarId={me.avatarId} size="lg" />}
        <FormField label={t('avatar.choose')} className="text-sm font-medium">
          {(id) => (
            <Input
              id={id}
              type="file"
              accept="image/*"
              disabled={!me || upload.isPending}
              onChange={(event) => {
                const file = event.target.files?.[0]
                event.target.value = ''
                if (file) upload.mutate(file)
              }}
            />
          )}
        </FormField>
        {me?.avatarId && (
          <Button variant="destructive-outline" onClick={() => setConfirmDelete(true)}>
            {t('avatar.delete')}
          </Button>
        )}
      </div>
      {upload.isPending && (
        <p role="status" className="mt-2 text-sm text-muted-foreground">
          {t('avatar.uploading')}
        </p>
      )}
      {upload.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {/* Anything but an API error comes from decoding the picture in the browser (HEIC in Chrome, for instance) */}
          {upload.error instanceof ApiError ? (
            <ErrorMessage error={upload.error} />
          ) : (
            t('avatar.unreadable')
          )}
        </p>
      )}
      <ConfirmationModal
        open={confirmDelete}
        title={t('avatar.deleteTitle')}
        description={t('avatar.deleteText')}
        confirmLabel={t('avatar.delete')}
        cancelLabel={t('common.cancel')}
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => remove.mutate()}
        onClose={() => setConfirmDelete(false)}
      />
    </section>
  )
}
