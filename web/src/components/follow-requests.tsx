import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { UserPlus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { FollowRow } from '@/components/follow-row'
import { card } from '@/components/ui/card-variants'
import { Heading } from '@/components/ui/heading'
import { followsQuery, pendingRequests, useFollowChange } from '@/lib/follows'

// Incoming requests at the top of the feed, so nobody has to find them under Settings.
export function FollowRequests() {
  const { t } = useTranslation()
  const follows = useQuery(followsQuery)
  const change = useFollowChange()
  const pending = pendingRequests(follows.data)
  if (pending.length === 0) return null

  return (
    <section className={card({ tone: 'highlight' })} aria-labelledby="follow-requests-title">
      <div className="flex items-start gap-3">
        <UserPlus aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-primary" />
        <div className="min-w-0 flex-1">
          <Heading level={2} id="follow-requests-title">
            {t('social.requestsTitle', { count: pending.length, name: pending[0].displayName })}
          </Heading>
          <p className="mt-1 text-sm text-muted-foreground">{t('social.requestHint')}</p>
        </div>
      </div>
      <ul className={card({ padding: 'none', className: 'mt-4 divide-y' })}>
        {pending.map((runner) => (
          <FollowRow key={runner.id} runner={runner} incoming change={change} />
        ))}
      </ul>
      {change.error && (
        <p role="alert" className="mt-3 text-sm text-destructive">
          <ErrorMessage error={change.error} />
        </p>
      )}
      <Link
        to="/app/settings/followers"
        className="mt-3 inline-block text-sm underline underline-offset-4"
      >
        {t('social.manageFollowers')}
      </Link>
    </section>
  )
}
