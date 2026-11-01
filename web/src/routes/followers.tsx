import { SlowLoading } from '@/components/slow-loading'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/api'
import type { SupporterTier } from '@/lib/auth'
import { SupporterBadge } from '@/components/supporter'

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'

type Runner = {
  id: string
  displayName: string
  status: 'pending' | 'accepted' | 'blocked'
  supporterTier: SupporterTier | null
  // Only for incoming requests: names are free, so the account's age helps spot impersonation.
  joinedAt: string | null
}

export function FollowersPage() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const follows = useQuery({
    queryKey: ['follows'],
    queryFn: () =>
      api.get<{ following: Runner[]; followers: Runner[]; blocked: Runner[] }>('/api/follows'),
  })
  const change = useMutation({
    mutationFn: ({ method, path }: { method: 'POST' | 'PUT' | 'DELETE'; path: string }) =>
      api.send(method, path),
    onSuccess: async () => {
      await Promise.all(
        ['follows', 'runner', 'feed', 'activity'].map((key) =>
          queryClient.invalidateQueries({ queryKey: [key] }),
        ),
      )
    },
  })
  const action = (runner: Runner, incoming: boolean) => (
    <li key={runner.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
      <span className="flex min-w-0 items-center gap-1.5">
        <Link
          to="/app/runners/$runnerId"
          params={{ runnerId: runner.id }}
          className="min-w-0 break-words font-medium underline-offset-4 hover:underline"
        >
          {runner.displayName}
        </Link>
        <SupporterBadge tier={runner.supporterTier} />
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
      <div className="flex flex-wrap gap-2">
        {runner.status === 'pending' && (
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
          variant="outline"
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
            variant="outline"
            disabled={change.isPending}
            onClick={() => change.mutate({ method: 'PUT', path: `/api/blocks/${runner.id}` })}
          >
            {t('social.block')}
          </Button>
        )}
      </div>
    </li>
  )
  return (
    <section className="space-y-6">
      <Breadcrumb aria-label={t('breadcrumbs')}>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/profile" activeOptions={{ exact: true }} />}>
              {t('nav.profile')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/settings" activeOptions={{ exact: true }} />}>
              {t('settings.title')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('settings.followers')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <h1 className="text-2xl font-semibold tracking-tight">{t('settings.followers')}</h1>
      <p className="text-sm text-muted-foreground">{t('social.profileLinksHint')}</p>
      <SlowLoading loading={follows.isPending}>
        {follows.error && (
          <p role="alert">
            <ErrorMessage error={follows.error} />
            <Button variant="outline" onClick={() => void follows.refetch()}>
              {t('activities.retry')}
            </Button>
          </p>
        )}
        {follows.data && (
          <>
            <section aria-labelledby="followers-heading" className="border-t pt-6">
              <h2 id="followers-heading" className="text-lg font-semibold tracking-tight">
                {t('social.followers')}
              </h2>
              {follows.data.followers.length === 0 && (
                <p className="mt-2 text-sm text-muted-foreground">{t('social.noFollowers')}</p>
              )}
              {follows.data.followers.some((runner) => runner.status === 'pending') && (
                <p className="mt-2 text-sm text-muted-foreground">{t('social.requestHint')}</p>
              )}
              {follows.data.followers.length > 0 && (
                <ul className="mt-3 divide-y rounded-lg border">
                  {follows.data.followers.map((runner) => action(runner, true))}
                </ul>
              )}
            </section>
            <section aria-labelledby="following-heading" className="border-t pt-6">
              <h2 id="following-heading" className="text-lg font-semibold tracking-tight">
                {t('social.following')}
              </h2>
              {follows.data.following.length === 0 && (
                <p className="mt-2 text-sm text-muted-foreground">{t('social.noFollowing')}</p>
              )}
              {follows.data.following.length > 0 && (
                <ul className="mt-3 divide-y rounded-lg border">
                  {follows.data.following.map((runner) => action(runner, false))}
                </ul>
              )}
            </section>
            {follows.data.blocked.length > 0 && (
              <section aria-labelledby="blocked-heading" className="border-t pt-6">
                <h2 id="blocked-heading" className="text-lg font-semibold tracking-tight">
                  {t('social.blocked')}
                </h2>
                <p className="mt-2 text-sm text-muted-foreground">{t('social.blockedHint')}</p>
                <ul className="mt-3 divide-y rounded-lg border">
                  {follows.data.blocked.map((runner) => (
                    <li
                      key={runner.id}
                      className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
                    >
                      <span className="min-w-0 break-words font-medium">{runner.displayName}</span>
                      <Button
                        size="sm"
                        variant="outline"
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
