import { useMutation } from '@tanstack/react-query'
import { Share2 } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorText } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { Input } from '@/components/ui/input'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { Heading } from '@/components/ui/heading'
import { api } from '@/lib/api'

type ShareCode = { code: string }

const shareLink = (code: string) => new URL(`/app/r/${code}`, window.location.origin).href

export function ShareProfile({ label }: { label?: string }) {
  const { t } = useTranslation()
  const [profileUrl, setProfileUrl] = useState('')
  const share = useMutation({
    mutationFn: async (): Promise<'shared' | 'copied' | 'cancelled'> => {
      const fetchLink = () =>
        api.send<ShareCode>('POST', '/api/me/share-code').then(({ code }) => {
          const url = shareLink(code)
          setProfileUrl(url)
          return url
        })
      // Phones get the system share sheet; desktops copy, since a share dialog there is rarely what people want.
      if (typeof navigator.share === 'function' && matchMedia('(pointer: coarse)').matches) {
        // A known link shares straight from the click; the first one is fetched before the sheet opens.
        const url = profileUrl || (await fetchLink())
        try {
          await navigator.share({ url })
          return 'shared'
        } catch (error) {
          if (error instanceof DOMException && error.name === 'AbortError') return 'cancelled'
          throw error
        }
      }
      const link = fetchLink()
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
      return 'copied'
    },
  })

  return (
    <div className="min-w-0 space-y-2">
      <Button
        variant="secondary-outline"
        size="sm"
        disabled={share.isPending}
        onClick={() => share.mutate()}
      >
        <Share2 aria-hidden="true" />
        {label ?? t('social.shareProfile')}
      </Button>
      {share.data === 'copied' && (
        <p role="status" className="text-sm text-muted-foreground">
          {t('social.profileLinkCopied')}
        </p>
      )}
      {share.isError && !profileUrl && <ErrorText error={share.error} />}
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
  const [confirming, setConfirming] = useState(false)
  const replace = useMutation({
    mutationFn: () => api.send<ShareCode>('POST', '/api/me/share-code/replace'),
    onSuccess: () => setConfirming(false),
  })

  return (
    <section aria-labelledby="profile-link-heading" className="mt-8 border-t pt-6">
      <Heading level={2} id="profile-link-heading">
        {t('social.profileLink')}
      </Heading>
      <p className="mt-2 text-sm text-muted-foreground">{t('social.profileLinkHint')}</p>
      <div className="mt-4 flex flex-wrap items-start gap-3">
        <ShareProfile />
        <Button
          variant="secondary-outline"
          size="sm"
          onClick={() => {
            replace.reset()
            setConfirming(true)
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
      <ConfirmationModal
        open={confirming}
        title={t('social.newProfileLink')}
        description={t('social.newProfileLinkConfirm')}
        confirmLabel={t(
          replace.isPending ? 'social.newProfileLinkCreating' : 'social.newProfileLink',
        )}
        cancelLabel={t('common.cancel')}
        pending={replace.isPending}
        error={replace.error}
        onConfirm={() => replace.mutate()}
        onClose={() => setConfirming(false)}
      />
    </section>
  )
}
