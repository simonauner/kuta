import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, Navigate, useNavigate, useParams } from '@tanstack/react-router'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Settings } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Avatar } from '@/components/avatar'
import { ErrorMessage, ErrorText } from '@/components/error-message'
import { QueryError } from '@/components/query-error'
import { SlowLoading } from '@/components/slow-loading'
import { GroupList } from '@/components/profile-groups'
import { VisitorNotice } from '@/components/visitor-notice'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { card } from '@/components/ui/card-variants'
import { Checkbox } from '@/components/ui/checkbox'
import { ConfirmationModal } from '@/components/ui/confirmation-modal'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { sportGroupsQuery } from '@/lib/activity-query'
import { ApiError, api } from '@/lib/api'
import { meQuery } from '@/lib/auth'
import { formatGoalAmount, goalMetrics, type GoalMetric } from '@/lib/goals'
import {
  clubSearchQuery,
  groupMembersQuery,
  groupQuery,
  groupsQuery,
  inviteLink,
  leaderboardQuery,
  shiftWeek,
  type ClubSummary,
  type GroupDetail,
  type GroupVisibility,
  type GroupInvite,
  type GroupMember,
} from '@/lib/groups'
import { sportGroupLabel } from '@/lib/sport-groups'
import { useVisitor } from '@/lib/visitor'

// Groups → the group → the current page; the last given level is the current page.
function GroupsBreadcrumb({
  group,
  page,
}: {
  group?: { id: string; name: string }
  page?: string
}) {
  const { t } = useTranslation()
  return (
    <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
      <BreadcrumbList>
        <BreadcrumbItem>
          {group || page ? (
            <BreadcrumbLink render={<Link to="/app/groups" activeOptions={{ exact: true }} />}>
              {t('groups.title')}
            </BreadcrumbLink>
          ) : (
            <BreadcrumbPage>{t('groups.title')}</BreadcrumbPage>
          )}
        </BreadcrumbItem>
        {group && (
          <>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              {page ? (
                <BreadcrumbLink
                  render={
                    <Link
                      to="/app/groups/$groupId"
                      params={{ groupId: group.id }}
                      activeOptions={{ exact: true }}
                    />
                  }
                >
                  {group.name}
                </BreadcrumbLink>
              ) : (
                <BreadcrumbPage>{group.name}</BreadcrumbPage>
              )}
            </BreadcrumbItem>
          </>
        )}
        {page && (
          <>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbPage>{page}</BreadcrumbPage>
            </BreadcrumbItem>
          </>
        )}
      </BreadcrumbList>
    </Breadcrumb>
  )
}

// The group's own details, shared by the create form and the group settings.
type GroupForm = {
  name: string
  visibility: GroupVisibility
  place: string
  sportGroup: string
  description: string
}

const emptyForm: GroupForm = {
  name: '',
  visibility: 'private',
  place: '',
  sportGroup: '',
  description: '',
}

const groupBody = (form: GroupForm) => ({ ...form, sportGroup: form.sportGroup || null })

// Open clubs with a similar name, other than the group itself; a name too short to search has none.
async function similarClubs(
  queryClient: ReturnType<typeof useQueryClient>,
  name: string,
  groupId?: string,
) {
  const found = await queryClient
    .fetchQuery(clubSearchQuery(name.trim()))
    .catch(() => [] as ClubSummary[])
  return found.filter((club) => club.id !== groupId)
}

function GroupFields({
  form,
  onChange,
}: {
  form: GroupForm
  onChange: (change: Partial<GroupForm>) => void
}) {
  const { t } = useTranslation()
  const sportGroups = useQuery(sportGroupsQuery)
  return (
    <>
      <FormField label={t('groups.name')}>
        {(id) => (
          <Input
            id={id}
            value={form.name}
            maxLength={100}
            required
            onChange={(event) => onChange({ name: event.currentTarget.value })}
          />
        )}
      </FormField>
      <div className="space-y-2">
        <FormField label={t('groups.visibility')}>
          {(id) => (
            <Select
              id={id}
              value={form.visibility}
              aria-describedby="visibility-hint"
              onChange={(event) =>
                onChange({ visibility: event.currentTarget.value as GroupVisibility })
              }
            >
              <option value="private">{t('groups.visibilities.private')}</option>
              <option value="open">{t('groups.visibilities.open')}</option>
            </Select>
          )}
        </FormField>
        <p id="visibility-hint" className="text-metadata text-muted-foreground">
          {t(form.visibility === 'open' ? 'groups.openHint' : 'groups.privateHint')}
        </p>
      </div>
      {/* Place, sport and description are only shown to others for open clubs. */}
      {form.visibility === 'open' && (
        <>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('groups.place')}>
              {(id) => (
                <Input
                  id={id}
                  value={form.place}
                  maxLength={100}
                  onChange={(event) => onChange({ place: event.currentTarget.value })}
                />
              )}
            </FormField>
            <FormField label={t('activities.sport')}>
              {(id) => (
                <Select
                  id={id}
                  value={form.sportGroup}
                  onChange={(event) => onChange({ sportGroup: event.currentTarget.value })}
                >
                  <option value="">{t('groups.noSport')}</option>
                  {(
                    sportGroups.data ??
                    (form.sportGroup ? [{ key: form.sportGroup, sports: [] }] : [])
                  ).map(({ key }) => (
                    <option key={key} value={key}>
                      {sportGroupLabel(key, t)}
                    </option>
                  ))}
                </Select>
              )}
            </FormField>
          </div>
          <FormField label={t('groups.description')}>
            {(id) => (
              <Textarea
                id={id}
                value={form.description}
                maxLength={500}
                rows={3}
                onChange={(event) => onChange({ description: event.currentTarget.value })}
              />
            )}
          </FormField>
        </>
      )}
    </>
  )
}

function SimilarClubs({ clubs }: { clubs: ClubSummary[] }) {
  const { t } = useTranslation()
  return (
    <div role="alert" className="space-y-2">
      <p className="text-body">{t('groups.similarClubs')}</p>
      <ClubList clubs={clubs} />
    </div>
  )
}

export function GroupsPage() {
  const { t } = useTranslation()
  const groups = useQuery(groupsQuery)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [form, setForm] = useState(emptyForm)
  // Shown once before an open club is created next to clubs with similar names.
  const [similar, setSimilar] = useState<ClubSummary[] | null>(null)
  const change = (update: Partial<GroupForm>) => {
    setForm((current) => ({ ...current, ...update }))
    if ('name' in update || 'visibility' in update) setSimilar(null)
  }
  const create = useMutation({
    mutationFn: async () => {
      if (similar === null && form.visibility === 'open') {
        const found = await similarClubs(queryClient, form.name)
        if (found.length) {
          setSimilar(found)
          return null
        }
      }
      return api.send<{ id: string }>('POST', '/api/groups', {
        ...groupBody(form),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      })
    },
    onSuccess: async (created) => {
      if (!created) return
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      await navigate({ to: '/app/groups/$groupId', params: { groupId: created.id } })
    },
  })
  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    create.mutate()
  }
  return (
    <section className="space-y-6">
      <Heading level={1}>{t('groups.title')}</Heading>
      <p className="text-body text-muted-foreground">{t('groups.intro')}</p>
      <SlowLoading loading={groups.isPending}>
        <QueryError query={groups} />
        {groups.data?.length === 0 && (
          <p className="text-body text-muted-foreground">{t('groups.none')}</p>
        )}
        {!!groups.data?.length && <GroupList groups={groups.data} />}
      </SlowLoading>
      <ClubSearch />
      <form onSubmit={onSubmit} className="space-y-3 border-t pt-6">
        <Heading level={2}>{t('groups.create')}</Heading>
        <fieldset disabled={create.isPending} className="space-y-4">
          <GroupFields form={form} onChange={change} />
        </fieldset>
        {similar && <SimilarClubs clubs={similar} />}
        <Button type="submit" disabled={create.isPending || !form.name.trim()}>
          {t(
            create.isPending
              ? 'groups.creating'
              : similar
                ? 'groups.createAnyway'
                : 'groups.create',
          )}
        </Button>
        <ErrorText error={create.error} />
      </form>
    </section>
  )
}

function ClubSearch() {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const [query, setQuery] = useState('')
  const clubs = useQuery(clubSearchQuery(query))
  return (
    <section aria-labelledby="club-search-heading" className="space-y-3 border-t pt-6">
      <Heading level={2} id="club-search-heading">
        {t('groups.findClub')}
      </Heading>
      <p className="text-metadata text-muted-foreground">{t('groups.findClubHint')}</p>
      <form
        role="search"
        className="flex flex-wrap items-end gap-3"
        onSubmit={(event) => {
          event.preventDefault()
          setQuery(text.trim())
        }}
      >
        <FormField label={t('groups.clubName')} className="min-w-0 flex-1">
          {(id) => (
            <Input
              id={id}
              type="search"
              value={text}
              maxLength={100}
              onChange={(event) => setText(event.currentTarget.value)}
            />
          )}
        </FormField>
        <Button type="submit" variant="secondary-filled" disabled={!text.trim()}>
          {t('groups.search')}
        </Button>
      </form>
      <SlowLoading loading={clubs.isFetching}>
        <QueryError query={clubs} />
        {clubs.data?.length === 0 && (
          <p role="status" className="text-body text-muted-foreground">
            {t('groups.noClubs')}
          </p>
        )}
        {!!clubs.data?.length && <ClubList clubs={clubs.data} />}
      </SlowLoading>
    </section>
  )
}

function ClubFacts({ club }: { club: ClubSummary }) {
  const { t, i18n } = useTranslation()
  const facts = [
    club.place,
    club.sportGroup && sportGroupLabel(club.sportGroup, t),
    t('groups.memberCount', { count: club.memberCount }),
  ].filter(Boolean)
  return (
    <>
      <span className="block text-metadata text-muted-foreground">{facts.join(' · ')}</span>
      <span className="block text-metadata text-muted-foreground">
        {t('groups.unverified', {
          date: new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'long' }).format(
            new Date(club.createdAt),
          ),
        })}
      </span>
    </>
  )
}

function ClubList({ clubs }: { clubs: ClubSummary[] }) {
  return (
    <ul className={card({ padding: 'none', className: 'divide-y' })}>
      {clubs.map((club) => (
        <li key={club.id} className="space-y-1 px-4 py-3">
          <Link
            to="/app/clubs/$clubId"
            params={{ clubId: club.id }}
            className="break-words font-medium underline underline-offset-4"
          >
            {club.name}
          </Link>
          <ClubFacts club={club} />
        </li>
      ))}
    </ul>
  )
}

export function GroupPage() {
  const { groupId } = useParams({ from: '/app/groups/$groupId' })
  return <GroupShell groupId={groupId} content={(group) => <GroupContent group={group} />} />
}

export function GroupSettingsPage() {
  const { groupId } = useParams({ from: '/app/groups/$groupId/settings' })
  const { t } = useTranslation()
  return (
    <GroupShell
      groupId={groupId}
      page={t('settings.title')}
      content={(group) => <GroupSettings group={group} />}
    />
  )
}

function GroupShell({
  groupId,
  page,
  content,
}: {
  groupId: string
  page?: string
  content: (group: GroupDetail) => ReactNode
}) {
  const { t } = useTranslation()
  const group = useQuery(groupQuery(groupId))
  const notFound = group.error instanceof ApiError && group.error.status === 404
  return (
    <section className="space-y-6">
      <GroupsBreadcrumb
        group={{ id: groupId, name: group.data?.name ?? t('groups.group') }}
        page={page}
      />
      <SlowLoading loading={group.isPending}>
        {notFound ? <p role="alert">{t('groups.notFound')}</p> : <QueryError query={group} />}
        {group.data && content(group.data)}
      </SlowLoading>
    </section>
  )
}

function useGroupChange() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      method,
      path,
      body,
    }: {
      method: 'POST' | 'PUT' | 'DELETE'
      path: string
      body?: unknown
    }) => api.send(method, path, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['groups'] }),
  })
}

function GroupContent({ group }: { group: GroupDetail }) {
  const { t } = useTranslation()
  const change = useGroupChange()
  return (
    <div className="space-y-8">
      <div className="flex items-start justify-between gap-4">
        <Heading level={1} className="min-w-0 break-words">
          {group.name}
        </Heading>
        <Button asChild variant="ghost" size="icon">
          <Link
            to="/app/groups/$groupId/settings"
            params={{ groupId: group.id }}
            aria-label={t('groups.settingsLink')}
          >
            <Settings aria-hidden="true" />
          </Link>
        </Button>
      </div>
      <LeaderboardSection groupId={group.id} sharing={group.shareTotals} />
      <MemberSection group={group} change={change} />
      <ErrorText error={change.error} />
    </div>
  )
}

function GroupSettings({ group }: { group: GroupDetail }) {
  const { t } = useTranslation()
  const change = useGroupChange()
  return (
    <div className="space-y-8">
      <Heading level={1}>{t('settings.title')}</Heading>
      <section aria-labelledby="sharing-heading" className="space-y-3">
        <Heading level={2} id="sharing-heading">
          {t('groups.sharing')}
        </Heading>
        <FormField label={t('groups.shareTotals')} layout="choice">
          {(id) => (
            <Checkbox
              id={id}
              checked={group.shareTotals}
              disabled={change.isPending}
              aria-describedby="share-totals-hint"
              onChange={(event) =>
                change.mutate({
                  method: 'PUT',
                  path: `/api/groups/${group.id}/me`,
                  body: { shareTotals: event.currentTarget.checked },
                })
              }
            />
          )}
        </FormField>
        <p id="share-totals-hint" className="text-metadata text-muted-foreground">
          {t('groups.shareTotalsHint')}
        </p>
        <ErrorText error={change.error} />
      </section>
      <InviteSection group={group} />
      {group.role === 'admin' && <AdminSection group={group} />}
      <LeaveSection group={group} />
    </div>
  )
}

function LeaderboardSection({ groupId, sharing }: { groupId: string; sharing: boolean }) {
  const { t, i18n } = useTranslation()
  const [sportGroup, setSportGroup] = useState('running')
  const [metric, setMetric] = useState<GoalMetric>('distance')
  // undefined = the current week in the group's time zone.
  const [week, setWeek] = useState<string>()
  const groups = useQuery(sportGroupsQuery)
  const board = useQuery({
    ...leaderboardQuery(groupId, sportGroup, metric, week),
    placeholderData: (previous) => previous,
  })
  const weekStart = board.data?.weekStart
  const weekLabel =
    weekStart &&
    new Intl.DateTimeFormat(i18n.resolvedLanguage, { day: 'numeric', month: 'short' }).formatRange(
      new Date(`${weekStart}T00:00:00Z`),
      new Date(`${shiftWeek(weekStart, 1)}T00:00:00Z`).getTime() - 86_400_000,
    )
  return (
    <section aria-labelledby="leaderboard-heading" className="space-y-4">
      <Heading level={2} id="leaderboard-heading">
        {t('groups.leaderboard')}
      </Heading>
      <div className="grid grid-cols-2 gap-4">
        <FormField label={t('activities.sport')} className="text-sm font-medium">
          {(id) => (
            <Select
              id={id}
              value={sportGroup}
              onChange={(e) => setSportGroup(e.currentTarget.value)}
            >
              <option value="">{t('activities.allSports')}</option>
              {(groups.data ?? (sportGroup ? [{ key: sportGroup, sports: [] }] : [])).map(
                ({ key }) => (
                  <option key={key} value={key}>
                    {sportGroupLabel(key, t)}
                  </option>
                ),
              )}
            </Select>
          )}
        </FormField>
        <FormField label={t('goals.metric')} className="text-sm font-medium">
          {(id) => (
            <Select
              id={id}
              value={metric}
              onChange={(e) => setMetric(e.currentTarget.value as GoalMetric)}
            >
              {goalMetrics.map((value) => (
                <option key={value} value={value}>
                  {t(`goals.metrics.${value}`)}
                </option>
              ))}
            </Select>
          )}
        </FormField>
      </div>
      <div className="flex items-center justify-between gap-3">
        <Button
          size="sm"
          variant="secondary-outline"
          disabled={!weekStart}
          onClick={() => weekStart && setWeek(shiftWeek(weekStart, -1))}
        >
          ← {t('groups.previousWeek')}
        </Button>
        <span className="text-sm font-medium" aria-live="polite">
          {weekLabel}
        </span>
        <Button
          size="sm"
          variant="secondary-outline"
          disabled={!week || !weekStart}
          onClick={() => weekStart && setWeek(shiftWeek(weekStart, 1))}
        >
          {t('groups.nextWeek')} →
        </Button>
      </div>
      {!sharing && (
        <p className="text-metadata text-muted-foreground">
          {t('groups.notSharing')}{' '}
          <Link
            to="/app/groups/$groupId/settings"
            params={{ groupId }}
            className="underline underline-offset-4"
          >
            {t('groups.settingsLink')}
          </Link>
        </p>
      )}
      <QueryError query={board} />
      {board.data?.rows.length === 0 && (
        <p className="text-body text-muted-foreground">{t('groups.nobodyShares')}</p>
      )}
      {!!board.data?.rows.length && (
        <table className="w-full text-sm">
          <thead className="text-left text-metadata text-muted-foreground">
            <tr>
              <th scope="col" className="w-10 py-2">
                #
              </th>
              <th scope="col" className="py-2">
                {t('groups.runner')}
              </th>
              <th scope="col" className="py-2 text-right">
                {t(`goals.metrics.${metric}`)}
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {board.data.rows.map((row, index) => (
              <tr key={row.userId}>
                <td className="tabular-nums">{index + 1}</td>
                <td>
                  <span className="flex min-w-0 items-center gap-1.5">
                    <Avatar name={row.displayName} avatarId={row.avatarId} />
                    <span className="break-words">{row.displayName}</span>
                  </span>
                </td>
                <td className="text-right tabular-nums">
                  {formatGoalAmount(metric, row.value, i18n.resolvedLanguage)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function MemberRow({
  group,
  member,
  change,
  request,
}: {
  group: GroupDetail
  member: GroupMember
  change: ReturnType<typeof useGroupChange>
  request: boolean
}) {
  const { t, i18n } = useTranslation()
  const admin = group.role === 'admin'
  const path = `/api/groups/${group.id}/members/${member.id}`
  return (
    <li className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
      <span className="flex min-w-0 flex-wrap items-center gap-1.5">
        <Avatar name={member.displayName} avatarId={member.avatarId} />
        <Link
          to="/app/runners/$runnerId"
          params={{ runnerId: member.id }}
          className="min-w-0 break-words font-medium underline underline-offset-4"
        >
          {member.displayName}
        </Link>
        {member.role === 'admin' && (
          <span className="text-metadata text-muted-foreground">{t('groups.admin')}</span>
        )}
        {member.previousName && (
          <span className="text-metadata text-muted-foreground">
            {t('social.previousName', { name: member.previousName })}
          </span>
        )}
        {member.joinedAt && (
          <span className="text-metadata text-muted-foreground">
            {t('social.joined', {
              date: new Intl.DateTimeFormat(i18n.language, { dateStyle: 'long' }).format(
                new Date(member.joinedAt),
              ),
            })}
          </span>
        )}
      </span>
      {member.similarTo && (
        <p role="status" className="basis-full text-metadata text-destructive">
          {t('social.similarName', { name: member.similarTo })}
        </p>
      )}
      {admin && (
        <div className="flex flex-wrap gap-2">
          {request && (
            <Button
              size="sm"
              disabled={change.isPending}
              onClick={() => change.mutate({ method: 'POST', path: `${path}/accept` })}
            >
              {t('social.approve')}
            </Button>
          )}
          {!request && member.role === 'member' && (
            <Button
              size="sm"
              variant="secondary-outline"
              disabled={change.isPending}
              onClick={() => change.mutate({ method: 'POST', path: `${path}/admin` })}
            >
              {t('groups.makeAdmin')}
            </Button>
          )}
          {!request && member.role === 'member' && (
            <Button
              size="sm"
              variant="secondary-outline"
              disabled={change.isPending}
              onClick={() => change.mutate({ method: 'DELETE', path })}
            >
              {t('groups.removeMember')}
            </Button>
          )}
          {request && (
            <Button
              size="sm"
              variant="secondary-outline"
              disabled={change.isPending}
              onClick={() => change.mutate({ method: 'DELETE', path })}
            >
              {t('social.decline')}
            </Button>
          )}
        </div>
      )}
    </li>
  )
}

function MemberSection({
  group,
  change,
}: {
  group: GroupDetail
  change: ReturnType<typeof useGroupChange>
}) {
  const { t } = useTranslation()
  const members = useInfiniteQuery(groupMembersQuery(group.id))
  return (
    <>
      {group.requests.length > 0 && (
        <section aria-labelledby="requests-heading" className="space-y-3">
          <Heading level={2} id="requests-heading">
            {t('groups.requests')}
          </Heading>
          <ul className={card({ padding: 'none', className: 'divide-y' })}>
            {group.requests.map((member) => (
              <MemberRow key={member.id} group={group} member={member} change={change} request />
            ))}
          </ul>
        </section>
      )}
      <section aria-labelledby="members-heading" className="space-y-3">
        <div>
          <Heading level={2} id="members-heading">
            {t('groups.members')}
          </Heading>
          <p className="text-metadata text-muted-foreground">
            {t('groups.memberCount', { count: group.memberCount })}
          </p>
        </div>
        <SlowLoading loading={members.isPending}>
          {!members.data && <QueryError query={members} />}
          {members.data && (
            <ul className={card({ padding: 'none', className: 'divide-y' })}>
              {members.data.pages.flat().map((member) => (
                <MemberRow
                  key={member.id}
                  group={group}
                  member={member}
                  change={change}
                  request={false}
                />
              ))}
            </ul>
          )}
        </SlowLoading>
        <SlowLoading loading={members.isFetchingNextPage}>
          {members.hasNextPage && (
            <Button
              variant="secondary-outline"
              disabled={members.isFetching}
              onClick={() => void members.fetchNextPage()}
            >
              {members.isFetchNextPageError ? t('groups.retryMembers') : t('groups.moreMembers')}
            </Button>
          )}
        </SlowLoading>
      </section>
    </>
  )
}

function InviteSection({ group }: { group: GroupDetail }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const link = inviteLink(group.inviteCode)
  const copy = useMutation({ mutationFn: () => navigator.clipboard.writeText(link) })
  const replace = useMutation({
    mutationFn: () => api.send('POST', `/api/groups/${group.id}/invite-code/replace`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['groups', group.id] })
      setConfirming(false)
    },
  })
  return (
    <section aria-labelledby="invite-heading" className="space-y-3 border-t pt-6">
      <Heading level={2} id="invite-heading">
        {t('groups.inviteLink')}
      </Heading>
      <p className="text-metadata text-muted-foreground">{t('groups.inviteHint')}</p>
      <FormField label={t('groups.inviteLink')} hideLabel>
        {(id) => (
          <Input id={id} value={link} readOnly onFocus={(event) => event.currentTarget.select()} />
        )}
      </FormField>
      <div className="flex flex-wrap gap-3">
        <Button size="sm" variant="secondary-outline" onClick={() => copy.mutate()}>
          {t('groups.copyLink')}
        </Button>
        {group.role === 'admin' && (
          <Button
            size="sm"
            variant="secondary-outline"
            onClick={() => {
              replace.reset()
              setConfirming(true)
            }}
          >
            {t('groups.newLink')}
          </Button>
        )}
      </div>
      {copy.isSuccess && (
        <p role="status" className="text-sm text-muted-foreground">
          {t('groups.linkCopied')}
        </p>
      )}
      <ErrorText error={copy.error} />
      <ConfirmationModal
        open={confirming}
        title={t('groups.newLink')}
        description={t('groups.newLinkConfirm')}
        confirmLabel={t('groups.newLink')}
        confirmVariant="primary-filled"
        cancelLabel={t('common.cancel')}
        pending={replace.isPending}
        error={replace.error}
        onConfirm={() => replace.mutate()}
        onClose={() => setConfirming(false)}
      />
    </section>
  )
}

function AdminSection({ group }: { group: GroupDetail }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [form, setForm] = useState<GroupForm>({
    name: group.name,
    visibility: group.visibility,
    place: group.place ?? '',
    sportGroup: group.sportGroup ?? '',
    description: group.description ?? '',
  })
  // Open clubs with a similar name, shown once before a group becomes an open club or an open club is renamed.
  const [similar, setSimilar] = useState<ClubSummary[] | null>(null)
  const [deleting, setDeleting] = useState(false)
  const change = (update: Partial<GroupForm>) => {
    setForm((current) => ({ ...current, ...update }))
    if ('name' in update || 'visibility' in update) setSimilar(null)
  }
  const save = useMutation({
    mutationFn: async () => {
      const checkNames =
        similar === null &&
        form.visibility === 'open' &&
        (group.visibility !== 'open' || form.name.trim() !== group.name)
      if (checkNames) {
        const found = await similarClubs(queryClient, form.name, group.id)
        if (found.length) {
          setSimilar(found)
          return 'similar'
        }
      }
      await api.send('PUT', `/api/groups/${group.id}`, groupBody(form))
      setSimilar(null)
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      return 'saved'
    },
  })
  const remove = useMutation({
    mutationFn: () => api.send('DELETE', `/api/groups/${group.id}`),
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: ['groups', group.id] })
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      await navigate({ to: '/app/groups' })
    },
  })
  return (
    <section aria-labelledby="group-settings-heading" className="space-y-3 border-t pt-6">
      <Heading level={2} id="group-settings-heading">
        {t('groups.settings')}
      </Heading>
      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault()
          save.mutate()
        }}
      >
        <fieldset disabled={save.isPending} className="space-y-4">
          <GroupFields form={form} onChange={change} />
        </fieldset>
        {similar && <SimilarClubs clubs={similar} />}
        <Button
          type="submit"
          variant="secondary-filled"
          disabled={save.isPending || !form.name.trim()}
        >
          {t(similar ? 'groups.saveAnyway' : 'groups.save')}
        </Button>
        {save.data === 'saved' && (
          <p role="status" className="text-sm text-muted-foreground">
            {t('groups.saved')}
          </p>
        )}
      </form>
      <ErrorText error={save.error} />
      <Button variant="destructive-outline" size="sm" onClick={() => setDeleting(true)}>
        {t('groups.delete')}
      </Button>
      <ConfirmationModal
        open={deleting}
        title={t('groups.delete')}
        description={t('groups.deleteConfirm', { name: group.name })}
        confirmLabel={t('groups.delete')}
        cancelLabel={t('common.cancel')}
        pending={remove.isPending}
        error={remove.error}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
    </section>
  )
}

function LeaveSection({ group }: { group: GroupDetail }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { data: me } = useQuery(meQuery)
  const [leaving, setLeaving] = useState(false)
  const leave = useMutation({
    mutationFn: () => api.send('DELETE', `/api/groups/${group.id}/members/${me?.id}`),
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: ['groups', group.id] })
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      await navigate({ to: '/app/groups' })
    },
  })
  return (
    <div className="border-t pt-6">
      <Button variant="secondary-outline" size="sm" onClick={() => setLeaving(true)}>
        {t('groups.leave')}
      </Button>
      <ConfirmationModal
        open={leaving}
        title={t('groups.leave')}
        description={t('groups.leaveConfirm')}
        confirmLabel={t('groups.leave')}
        confirmVariant="primary-filled"
        cancelLabel={t('common.cancel')}
        pending={leave.isPending}
        error={leave.error}
        onConfirm={() => leave.mutate()}
        onClose={() => setLeaving(false)}
      />
    </div>
  )
}

// An invite link (/app/g/$code) shows only the group's name and size until an admin approves the request.
export function JoinGroupPage() {
  const visitor = useVisitor()
  return visitor ? <VisitorNotice kind="group" /> : <JoinGroup />
}

function JoinGroup() {
  const { code } = useParams({ from: '/app/g/$code' })
  const path = `/api/group-invites/${encodeURIComponent(code)}`
  return (
    <JoinView
      queryKey={['group-invite', code]}
      path={path}
      joinPath={path}
      notFound="groups.inviteInvalid"
    />
  )
}

// An open club found in search: the club's own details and a request to join, never its members.
export function ClubPage() {
  const { clubId } = useParams({ from: '/app/clubs/$clubId' })
  const visitor = useVisitor()
  if (visitor) return <VisitorNotice kind="group" />
  return (
    <JoinView
      queryKey={['clubs', clubId]}
      path={`/api/clubs/${clubId}`}
      joinPath={`/api/clubs/${clubId}/join`}
      notFound="groups.clubNotFound"
    />
  )
}

function JoinView({
  queryKey,
  path,
  joinPath,
  notFound,
}: {
  queryKey: string[]
  path: string
  joinPath: string
  notFound: 'groups.inviteInvalid' | 'groups.clubNotFound'
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const group = useQuery({
    queryKey,
    queryFn: () => api.get<GroupInvite | ClubSummary>(path),
    retry: false,
  })
  const join = useMutation({
    mutationFn: () => api.send('POST', joinPath),
    onSuccess: () =>
      Promise.all(
        [queryKey, ['groups']].map((key) => queryClient.invalidateQueries({ queryKey: key })),
      ),
  })
  if (group.data?.status === 'member')
    return <Navigate to="/app/groups/$groupId" params={{ groupId: group.data.id }} replace />
  return (
    <SlowLoading loading={group.isPending}>
      {group.error && (
        <p role="alert">
          {group.error instanceof ApiError && group.error.status === 404 ? (
            t(notFound)
          ) : (
            <ErrorMessage error={group.error} />
          )}
        </p>
      )}
      {group.data && (
        <Card padding="spacious" className="space-y-4">
          <Heading level={1} className="break-words">
            {group.data.name}
          </Heading>
          {'createdAt' in group.data ? (
            <div>
              <ClubFacts club={group.data} />
            </div>
          ) : (
            <p className="text-metadata text-muted-foreground">
              {t('groups.memberCount', { count: group.data.memberCount })}
            </p>
          )}
          {'description' in group.data && group.data.description && (
            <p className="whitespace-pre-line break-words text-body">{group.data.description}</p>
          )}
          <p className="text-body text-muted-foreground">{t('groups.intro')}</p>
          {group.data.status === 'pending' ? (
            <p role="status" className="text-body">
              {t('groups.requestSent')}
            </p>
          ) : (
            <Button disabled={join.isPending} onClick={() => join.mutate()}>
              {t('groups.join')}
            </Button>
          )}
          <ErrorText error={join.error} />
        </Card>
      )}
    </SlowLoading>
  )
}
