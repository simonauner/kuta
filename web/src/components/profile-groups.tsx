import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { QueryError } from '@/components/query-error'
import { Heading } from '@/components/ui/heading'
import { card } from '@/components/ui/card-variants'
import { groupsQuery, type GroupSummary } from '@/lib/groups'

// The owner's groups on the profile, with a link to the groups page (where groups are created).
export function ProfileGroups() {
  const { t } = useTranslation()
  const groups = useQuery(groupsQuery)
  return (
    <section aria-labelledby="profile-groups-heading" className="space-y-3">
      <div className="flex items-baseline justify-between gap-4">
        <Heading level={2} id="profile-groups-heading">
          {t('groups.profileHeading')}
        </Heading>
        <Link
          to="/app/groups"
          className="text-sm text-muted-foreground underline underline-offset-2"
        >
          {t('groups.all')} →
        </Link>
      </div>
      <QueryError query={groups} />
      {groups.data?.length === 0 && (
        <p className="text-body text-muted-foreground">{t('groups.none')}</p>
      )}
      {!!groups.data?.length && <GroupList groups={groups.data} />}
    </section>
  )
}

// Own groups and requests, on the profile and the groups page.
export function GroupList({ groups }: { groups: GroupSummary[] }) {
  const { t } = useTranslation()
  return (
    <ul className={card({ padding: 'none', className: 'divide-y' })}>
      {groups.map((group) => (
        <li
          key={group.id}
          className="flex flex-wrap items-baseline justify-between gap-3 px-4 py-3"
        >
          {group.status === 'member' ? (
            <Link
              to="/app/groups/$groupId"
              params={{ groupId: group.id }}
              className="min-w-0 break-words font-medium underline underline-offset-4"
            >
              {group.name}
            </Link>
          ) : (
            <span className="min-w-0 break-words font-medium">{group.name}</span>
          )}
          <span className="text-metadata text-muted-foreground">
            {group.status === 'pending'
              ? t('groups.pending')
              : t('groups.memberCount', { count: group.memberCount })}
            {group.role === 'admin' && ` · ${t('groups.admin')}`}
          </span>
        </li>
      ))}
    </ul>
  )
}
