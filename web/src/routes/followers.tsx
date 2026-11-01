import { SlowLoading } from '@/components/slow-loading'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { followsQuery, useFollowChange } from '@/lib/follows'
import { FollowRow } from '@/components/follow-row'

import { SettingsBreadcrumb } from '@/components/settings-breadcrumb'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'

export function FollowersPage() {
  const { t } = useTranslation()
  const follows = useQuery(followsQuery)
  const change = useFollowChange()
  return (
    <section className="space-y-6">
      <SettingsBreadcrumb page={t('settings.followers')} />
      <Heading level={1}>{t('settings.followers')}</Heading>
      <p className="text-sm text-muted-foreground">{t('social.profileLinksHint')}</p>
      <SlowLoading loading={follows.isPending}>
        {follows.error && (
          <p role="alert">
            <ErrorMessage error={follows.error} />
            <Button variant="secondary-outline" onClick={() => void follows.refetch()}>
              {t('activities.retry')}
            </Button>
          </p>
        )}
        {follows.data && (
          <>
            <section aria-labelledby="followers-heading" className="border-t pt-6">
              <Heading level={2} id="followers-heading">
                {t('social.followers')}
              </Heading>
              {follows.data.followers.length === 0 && (
                <p className="mt-2 text-sm text-muted-foreground">{t('social.noFollowers')}</p>
              )}
              {follows.data.followers.some((runner) => runner.status === 'pending') && (
                <p className="mt-2 text-sm text-muted-foreground">{t('social.requestHint')}</p>
              )}
              {follows.data.followers.length > 0 && (
                <ul className={card({ padding: 'none', className: 'mt-3 divide-y' })}>
                  {follows.data.followers.map((runner) => (
                    <FollowRow key={runner.id} runner={runner} incoming change={change} />
                  ))}
                </ul>
              )}
            </section>
            <section aria-labelledby="following-heading" className="border-t pt-6">
              <Heading level={2} id="following-heading">
                {t('social.following')}
              </Heading>
              {follows.data.following.length === 0 && (
                <p className="mt-2 text-sm text-muted-foreground">{t('social.noFollowing')}</p>
              )}
              {follows.data.following.length > 0 && (
                <ul className={card({ padding: 'none', className: 'mt-3 divide-y' })}>
                  {follows.data.following.map((runner) => (
                    <FollowRow key={runner.id} runner={runner} incoming={false} change={change} />
                  ))}
                </ul>
              )}
            </section>
            {follows.data.blocked.length > 0 && (
              <section aria-labelledby="blocked-heading" className="border-t pt-6">
                <Heading level={2} id="blocked-heading">
                  {t('social.blocked')}
                </Heading>
                <p className="mt-2 text-sm text-muted-foreground">{t('social.blockedHint')}</p>
                <ul className={card({ padding: 'none', className: 'mt-3 divide-y' })}>
                  {follows.data.blocked.map((runner) => (
                    <li
                      key={runner.id}
                      className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
                    >
                      <span className="min-w-0 break-words font-medium">{runner.displayName}</span>
                      <Button
                        size="sm"
                        variant="secondary-outline"
                        disabled={change.isPending}
                        onClick={() =>
                          change.mutate({ method: 'DELETE', path: `/api/blocks/${runner.id}` })
                        }
                      >
                        {t('social.unblock')}
                      </Button>
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </>
        )}
      </SlowLoading>
      {change.error && (
        <p role="alert" className="mt-3 text-destructive">
          <ErrorMessage error={change.error} />
        </p>
      )}
    </section>
  )
}
