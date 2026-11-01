import { useMutation, useQuery } from '@tanstack/react-query'
import { Share2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { Input } from '@/components/ui/input'
import { meQuery } from '@/lib/auth'

export function ShareProfile({ label }: { label?: string }) {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const profileUrl = me ? new URL(`/app/runners/${me.id}`, window.location.origin).href : ''
  const share = useMutation({
    mutationFn: async () => {
      await navigator.clipboard.writeText(profileUrl)
    },
  })

  return (
    <div className="min-w-0 space-y-2">
      <Button
        variant="secondary-outline"
        size="sm"
        disabled={!me || share.isPending}
        onClick={() => share.mutate()}
      >
        <Share2 aria-hidden="true" />
        {label ?? t('social.shareProfile')}
      </Button>
      {share.isSuccess && (
        <p role="status" className="text-sm text-muted-foreground">
          {t('social.profileLinkCopied')}
        </p>
      )}
      {share.isError && (
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
