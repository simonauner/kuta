import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { SupporterBadge } from '@/components/supporter'
import { Button } from '@/components/ui/button'
import type { Runner, useFollowChange } from '@/lib/follows'

export function FollowRow({
  runner,
  incoming,
  change,
}: {
  runner: Runner
  incoming: boolean
  change: ReturnType<typeof useFollowChange>
}) {
  const { t, i18n } = useTranslation()
  return (
    <li className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
      <span className="flex min-w-0 items-center gap-1.5">
        <Link
          to="/app/runners/$runnerId"
          params={{ runnerId: runner.id }}
          className="min-w-0 break-words font-medium underline-offset-4 underline"
        >
          {runner.displayName}
        </Link>
        <SupporterBadge tier={runner.supporterTier} />
        {runner.previousName && (
          <span className="text-metadata text-muted-foreground">
            {t('social.previousName', { name: runner.previousName })}
          </span>
        )}
        {runner.joinedAt && (
          <span className="text-metadata text-muted-foreground">
            {t('social.joined', {
              date: new Intl.DateTimeFormat(i18n.language, {
                year: 'numeric',
                month: 'long',
                day: 'numeric',
              }).format(new Date(runner.joinedAt)),
            })}
          </span>
        )}
      </span>
      {runner.similarTo && (
        <p role="status" className="basis-full text-metadata text-destructive">
          {t('social.similarName', { name: runner.similarTo })}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {runner.status === 'pending' && !incoming && (
          <span className="self-center text-xs text-muted-foreground">{t('social.pending')}</span>
        )}
        {incoming && runner.status === 'pending' && (
          <Button
            size="sm"
            disabled={change.isPending}
            onClick={() => change.mutate({ method: 'PUT', path: `/api/followers/${runner.id}` })}
          >
            {t('social.approve')}
          </Button>
        )}
        <Button
          size="sm"
          variant="secondary-outline"
          disabled={change.isPending}
          onClick={() =>
            change.mutate({
              method: 'DELETE',
              path: `/api/${incoming ? 'followers' : 'following'}/${runner.id}`,
            })
          }
        >
          {incoming
            ? t(runner.status === 'pending' ? 'social.decline' : 'social.remove')
            : t(runner.status === 'pending' ? 'social.cancel' : 'social.unfollow')}
        </Button>
        {incoming && (
          <Button
            size="sm"
            variant="secondary-outline"
            disabled={change.isPending}
            onClick={() => change.mutate({ method: 'PUT', path: `/api/blocks/${runner.id}` })}
          >
            {t('social.block')}
          </Button>
        )}
      </div>
    </li>
  )
}
