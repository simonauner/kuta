import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Settings, Share2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { ActivityFeed } from '@/components/activity-feed'
import { Input } from '@/components/ui/input'
import { meQuery } from '@/lib/auth'

export function ProfilePage() {
  const { t } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const profileUrl = me ? new URL(`/app/runners/${me.id}`, window.location.origin).href : ''
  const share = useMutation({
    mutationFn: async () => {
      await navigator.clipboard.writeText(profileUrl)
    },
  })

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <h1 className="min-w-0 break-words text-2xl font-semibold tracking-tight">
          {me?.displayName}
        </h1>
        <div className="flex shrink-0 items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={!me || share.isPending}
            onClick={() => share.mutate()}
          >
            <Share2 aria-hidden="true" />
            {t('social.shareProfile')}
          </Button>
          <Button asChild variant="ghost" size="icon">
            <Link to="/app/settings" aria-label={t('settings.title')}>
              <Settings aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </div>
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
          <Input
            aria-label={t('social.profileLink')}
            value={profileUrl}
            readOnly
            onFocus={(event) => event.currentTarget.select()}
          />
        </div>
      )}
      <h2 className="text-lg font-semibold">{t('nav.activities')}</h2>
      {me && <ActivityFeed runnerId={me.id} />}
    </section>
  )
}
