import { useMutation } from '@tanstack/react-query'
import { Share2 } from 'lucide-react'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { Input } from '@/components/ui/input'
import { Modal } from '@/components/ui/modal'
import { api } from '@/lib/api'

type ShareCode = { code: string }

const shareLink = (code: string) => new URL(`/app/r/${code}`, window.location.origin).href

export function ShareProfile({ label }: { label?: string }) {
  const { t } = useTranslation()
  const [profileUrl, setProfileUrl] = useState('')
  const share = useMutation({
    mutationFn: async () => {
      const link = api.send<ShareCode>('POST', '/api/me/share-code').then(({ code }) => {
        const url = shareLink(code)
        setProfileUrl(url)
        return url
      })
      // Safari allows the copy only if it starts during the click, so hand it the pending link.
      if (typeof ClipboardItem === 'undefined') await navigator.clipboard.writeText(await link)
      // The link comes first so that a failed request reports its own error.
      else
        await Promise.all([
          link,
          navigator.clipboard.write([
            new ClipboardItem({
              'text/plain': link.then((url) => new Blob([url], { type: 'text/plain' })),
            }),
          ]),
        ])
    },
  })

  return (
    <div className="min-w-0 space-y-2">
      <Button variant="outline" size="sm" disabled={share.isPending} onClick={() => share.mutate()}>
        <Share2 aria-hidden="true" />
        {label ?? t('social.shareProfile')}
      </Button>
      {share.isSuccess && (
        <p role="status" className="text-sm text-muted-foreground">
          {t('social.profileLinkCopied')}
        </p>
      )}
      {share.isError && !profileUrl && (
        <p role="alert" className="text-sm text-destructive">
          <ErrorMessage error={share.error} />
        </p>
      )}
      {share.isError && profileUrl && (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {t('social.profileLinkCopyFailed')}
          </p>
          <FormField label={t('social.profileLink')} hideLabel>
            {(id) => (
              <Input
                id={id}
                value={profileUrl}
                readOnly
                onFocus={(event) => event.currentTarget.select()}
              />
            )}
          </FormField>
        </div>
      )}
    </div>
  )
}

// Replacing the code makes the old link stop working; access itself still follows blocks and visibility.
export function ReplaceProfileLink() {
  const { t } = useTranslation()
  const dialog = useRef<HTMLDialogElement>(null)
  const replace = useMutation({
    mutationFn: () => api.send<ShareCode>('POST', '/api/me/share-code/replace'),
    onSuccess: () => dialog.current?.close(),
  })

  return (
    <section aria-labelledby="profile-link-heading" className="mt-8 border-t pt-6">
      <h2 id="profile-link-heading" className="text-lg font-semibold">
        {t('social.profileLink')}
      </h2>
      <p className="mt-2 text-sm text-muted-foreground">{t('social.profileLinkHint')}</p>
      <div className="mt-4 flex flex-wrap items-start gap-3">
        <ShareProfile />
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            replace.reset()
            dialog.current?.showModal()
          }}
        >
          {t('social.newProfileLink')}
        </Button>
      </div>
      {replace.isSuccess && (
        <p role="status" className="mt-2 text-sm text-muted-foreground">
          {t('social.newProfileLinkDone')}
        </p>
      )}
      <Modal
        ref={dialog}
        aria-labelledby="new-profile-link-title"
        aria-describedby="new-profile-link-help"
        onCancel={(event) => {
          if (replace.isPending) event.preventDefault()
        }}
      >
        <h3 id="new-profile-link-title" className="text-lg font-semibold">
          {t('social.newProfileLink')}
        </h3>
        <p id="new-profile-link-help" className="mt-3 text-sm text-muted-foreground">
          {t('social.newProfileLinkConfirm')}
        </p>
        {replace.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={replace.error} />
          </p>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <Button
            variant="outline"
            disabled={replace.isPending}
            onClick={() => dialog.current?.close()}
          >
            {t('activity.cancel')}
          </Button>
          <Button disabled={replace.isPending} onClick={() => replace.mutate()}>
            {t(replace.isPending ? 'social.newProfileLinkCreating' : 'social.newProfileLink')}
          </Button>
        </div>
      </Modal>
    </section>
  )
}
