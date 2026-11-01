import { Reveal } from '@/components/reveal'
import { GarminDerivedNote } from '@/components/garmin'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { motion, useReducedMotion } from 'motion/react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import { activityPrivaciesQuery, sportGroupsQuery, refreshGoalQueries } from '@/lib/activity-query'
import { sportGroupLabel, sportGroupMembers } from '@/lib/sport-groups'
import {
  formatGoalAmount,
  goalDaysLeft,
  goalFraction,
  goalInput,
  goalMetrics,
  goalPeriodLabel,
  goalPeriods,
  goalPrivacies,
  parseGoalInput,
  type Goal,
  type GoalMetric,
  type GoalPeriod,
  type GoalPrivacy,
} from '@/lib/goals'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Modal } from '@/components/ui/modal'
import { ErrorMessage } from '@/components/error-message'
import { SportIcon } from '@/components/sport-icon'

// Under 'activities' so that new, edited and deleted activities also refresh the goals.
const goalsQueryKey = (timeZone: string) => ['activities', 'goals', timeZone]
function useGoalText() {
  const { t, i18n } = useTranslation()
  const amount = (metric: GoalMetric, value: number) =>
    metric === 'count'
      ? t('goals.sessions', { count: value })
      : formatGoalAmount(metric, value, i18n.resolvedLanguage)
  const title = (goal: Goal) => {
    const target = amount(goal.metric, goal.target)
    if (goal.recurring) return t(`goals.per.${goal.period}`, { amount: target })
    return t(`goals.once.${goal.period}`, {
      amount: target,
      ...goalPeriodLabel(goal.period, goal.current.start, i18n.resolvedLanguage),
    })
  }
  const privacyLabel = (privacy: GoalPrivacy) =>
    t(
      privacy === 'private'
        ? 'social.private'
        : privacy === 'followers'
          ? 'social.followersVisibility'
          : 'social.public',
    )
  const groupLabel = (key: string | null) =>
    key === null ? t('activities.allSports') : sportGroupLabel(key, t)
  return { t, i18n, amount, title, groupLabel, privacyLabel }
}

// Without runnerId: own goals, editable via onSelect. With runnerId: the runner's goals the visitor may see per
// each goal's visibility, without editing.
export function GoalList({
  onSelect,
  runnerId,
  viewAs,
}: {
  onSelect?: (goal: Goal) => void
  runnerId?: string
  viewAs?: 'followers' | 'public'
}) {
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const params = new URLSearchParams({ timeZone })
  if (viewAs) params.set('viewAs', viewAs)
  const goals = useQuery({
    // Under 'runner' so that follow/unfollow refetches the shared goals.
    queryKey: runnerId ? ['runner', runnerId, 'goals', timeZone, viewAs] : goalsQueryKey(timeZone),
    queryFn: () =>
      api.get<Goal[]>(
        runnerId ? `/api/runners/${runnerId}/goals?${params}` : `/api/goals?${params}`,
      ),
  })
  const groups = useQuery(sportGroupsQuery)
  const { t } = useTranslation()
  if (goals.error)
    return (
      <p role="alert" className="mt-4 text-sm text-destructive">
        <ErrorMessage error={goals.error} />
      </p>
    )
  if (!goals.data?.length) return null
  return (
    <Reveal>
      <section className={runnerId ? undefined : 'mt-6'} aria-labelledby="goals-heading">
        <Heading level={2} id="goals-heading" className="mb-3">
          {t('goals.heading')}
        </Heading>
        {!runnerId && <GarminDerivedNote className="-mt-2 mb-3" />}
        <ul className="grid gap-3 sm:grid-cols-2">
          {goals.data.map((goal) => (
            <li key={goal.id}>
              <GoalCard
                goal={goal}
                iconSport={
                  goal.sportGroup &&
                  (groups.data?.find((g) => g.key === goal.sportGroup)?.sports[0] ??
                    goal.sportGroup)
                }
                onSelect={onSelect && (() => onSelect(goal))}
              />
            </li>
          ))}
        </ul>
      </section>
    </Reveal>
  )
}

function GoalCard({
  goal,
  iconSport,
  onSelect,
}: {
  goal: Goal
  iconSport: string | null
  onSelect?: () => void
}) {
  const { t, amount, title, groupLabel, privacyLabel } = useGoalText()
  const reduceMotion = useReducedMotion()
  const { current, previous } = goal
  const fraction = goalFraction(current.value, goal.target)
  const [today] = useState(() => new Date())
  const daysLeft = goalDaysLeft(current.end, today)
  const goalTitle = title(goal)
  const Root = onSelect ? 'button' : 'div'
  return (
    <Root
      {...(onSelect && {
        type: 'button' as const,
        onClick: onSelect,
        'aria-label': t('goals.editLabel', { title: goalTitle }),
      })}
      className={cn(
        'w-full rounded-xl border bg-card p-4 text-left text-card-foreground shadow-xs',
        onSelect &&
          'transition-colors hover:bg-accent/50 focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none',
        goal.ended && 'opacity-70',
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="flex items-center gap-2 font-medium">
            {iconSport && <SportIcon sport={iconSport} />}
            <span className="break-words">{goalTitle}</span>
          </p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {groupLabel(goal.sportGroup)}
            {' · '}
            {goal.ended ? t('goals.ended') : t('goals.daysLeft', { count: daysLeft })}
            {onSelect && ` · ${privacyLabel(goal.privacy)}`}
          </p>
        </div>
        {current.achieved && (
          <motion.span
            initial={reduceMotion ? false : { scale: 0.4, rotate: -12, opacity: 0 }}
            animate={{ scale: 1, rotate: 0, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 400, damping: 12 }}
            className="shrink-0 rounded-full bg-emerald-500/15 px-2.5 py-1 text-xs font-semibold text-emerald-700 dark:text-emerald-300"
          >
            <span aria-hidden="true">🎉 </span>
            {t('goals.hooray')}
          </motion.span>
        )}
      </div>
      <div
        role="progressbar"
        aria-label={goalTitle}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(fraction * 100)}
        className="mt-3 h-2.5 overflow-hidden rounded-full bg-muted"
      >
        <motion.div
          className={cn('h-full rounded-full', current.achieved ? 'bg-emerald-500' : 'bg-primary')}
          initial={reduceMotion ? false : { width: 0 }}
          animate={{ width: `${fraction * 100}%` }}
          transition={{ duration: reduceMotion ? 0 : 0.6, ease: 'easeOut' }}
        />
      </div>
      <p className="mt-2 text-sm tabular-nums">
        {t('goals.progress', {
          value: amount(goal.metric, current.value),
          target: amount(goal.metric, goal.target),
        })}
        {goal.ended && !current.achieved && (
          <span className="text-muted-foreground"> · {t('goals.missed')}</span>
        )}
      </p>
      {goal.recurring && (previous?.achieved || goal.streak > 1) && (
        <p className="mt-1 text-xs text-muted-foreground">
          {previous?.achieved &&
            t(`goals.previous.${goal.period}`, { amount: amount(goal.metric, previous.value) })}
          {previous?.achieved && goal.streak > 1 && ' · '}
          {goal.streak > 1 && (
            <span className="font-medium text-foreground">
              <span aria-hidden="true">🔥 </span>
              {t(`goals.streak.${goal.period}`, { count: goal.streak })}
            </span>
          )}
        </p>
      )}
    </Root>
  )
}

export function GoalDialog({ goal, onClose }: { goal: Goal | null; onClose: () => void }) {
  const { t, i18n, title, groupLabel, privacyLabel } = useGoalText()
  const groups = useQuery(sportGroupsQuery)
  const queryClient = useQueryClient()
  const dialog = useRef<HTMLDialogElement>(null)
  const [sportGroup, setSportGroup] = useState(goal ? (goal.sportGroup ?? '') : 'running')
  // Show which sports a group counts.
  const selectedGroup = groups.data?.find((g) => g.key === sportGroup)
  const included = selectedGroup && sportGroupMembers(selectedGroup, t)
  const [metric, setMetric] = useState<GoalMetric>(goal?.metric ?? 'distance')
  const [target, setTarget] = useState(goal ? goalInput(goal.metric, goal.target) : '')
  const [period, setPeriod] = useState<GoalPeriod>(goal?.period ?? 'week')
  const [recurring, setRecurring] = useState(goal?.recurring ?? true)
  const [privacy, setPrivacy] = useState<GoalPrivacy>(goal?.privacy ?? 'private')
  // Progress counts all own activities, so a visible goal reveals when activities the viewer can't see happen.
  const activityPrivacies = useQuery(activityPrivaciesQuery)
  const revealsHiddenActivities = activityPrivacies.data?.some(
    (value) => goalPrivacies.indexOf(value) < goalPrivacies.indexOf(privacy),
  )
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const parsedTarget = parseGoalInput(metric, target)

  useEffect(() => dialog.current?.showModal(), [])

  const refresh = () => refreshGoalQueries(queryClient)
  const save = useMutation({
    mutationFn: () =>
      api.send(goal ? 'PUT' : 'POST', goal ? `/api/goals/${goal.id}` : '/api/goals', {
        sportGroup: sportGroup || null,
        metric,
        period,
        target: parsedTarget,
        recurring,
        privacy,
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      }),
    onSuccess: async () => {
      await refresh()
      dialog.current?.close()
    },
  })
  const remove = useMutation({
    mutationFn: () => api.send('DELETE', `/api/goals/${goal!.id}`),
    onSuccess: async () => {
      await refresh()
      dialog.current?.close()
    },
  })
  const busy = save.isPending || remove.isPending

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (!busy && parsedTarget !== null) save.mutate()
  }

  const selectClass = 'h-9 w-full rounded-md border bg-background px-3 text-sm'
  return (
    <Modal
      ref={dialog}
      aria-labelledby="goal-form-title"
      onClose={onClose}
      onCancel={(event) => {
        if (busy) event.preventDefault()
      }}
    >
      <h2 id="goal-form-title" className="text-xl font-semibold">
        {goal ? t('goals.editTitle') : t('goals.new')}
      </h2>
      {goal && <p className="mt-1 break-words text-sm text-muted-foreground">{title(goal)}</p>}
      <form onSubmit={onSubmit}>
        <fieldset disabled={busy} className="mt-5 grid grid-cols-2 gap-4">
          <FormField label={t('activities.sport')} className="text-sm font-medium col-span-2">
            {(id) => (
              <>
                <select
                  id={id}
                  className={selectClass}
                  value={sportGroup}
                  aria-describedby={
                    included && included.length > 1 ? 'goal-sport-included' : undefined
                  }
                  onChange={(e) => setSportGroup(e.currentTarget.value)}
                >
                  <option value="">{groupLabel(null)}</option>
                  {(groups.data ?? (sportGroup ? [{ key: sportGroup, sports: [] }] : [])).map(
                    ({ key }) => (
                      <option key={key} value={key}>
                        {groupLabel(key)}
                      </option>
                    ),
                  )}
                </select>
                {included && included.length > 1 && (
                  <span
                    id="goal-sport-included"
                    className="mt-2 block text-xs font-normal text-muted-foreground"
                  >
                    {t('goals.includes', {
                      sports: new Intl.ListFormat(i18n.resolvedLanguage, {
                        type: 'conjunction',
                      }).format(included),
                    })}
                  </span>
                )}
              </>
            )}
          </FormField>
          {groups.error && (
            <p role="alert" className="col-span-2 text-sm text-destructive">
              <ErrorMessage error={groups.error} />
            </p>
          )}
          <FormField label={t('goals.metric')} className="text-sm font-medium">
            {(id) => (
              <select
                id={id}
                className={selectClass}
                value={metric}
                onChange={(e) => setMetric(e.currentTarget.value as GoalMetric)}
              >
                {goalMetrics.map((value) => (
                  <option key={value} value={value}>
                    {t(`goals.metrics.${value}`)}
                  </option>
                ))}
              </select>
            )}
          </FormField>
          <FormField label={t(`goals.targetLabel.${metric}`)} className="text-sm font-medium">
            {(id) => (
              <Input
                id={id}
                autoFocus={!goal}
                required
                inputMode={metric === 'count' ? 'numeric' : 'decimal'}
                pattern={metric === 'count' ? '\\d+' : '\\d+([.,]\\d+)?'}
                aria-invalid={target !== '' && parsedTarget === null}
                value={target}
                onChange={(e) => setTarget(e.currentTarget.value)}
              />
            )}
          </FormField>
          <FormField label={t('goals.period')} className="text-sm font-medium">
            {(id) => (
              <select
                id={id}
                className={selectClass}
                value={period}
                onChange={(e) => setPeriod(e.currentTarget.value as GoalPeriod)}
              >
                {goalPeriods.map((value) => (
                  <option key={value} value={value}>
                    {t(`goals.periods.${value}`)}
                  </option>
                ))}
              </select>
            )}
          </FormField>
          <FormField label={t('goals.repeat')} className="text-sm font-medium">
            {(id) => (
              <select
                id={id}
                className={selectClass}
                value={recurring ? 'recurring' : 'once'}
                onChange={(e) => setRecurring(e.currentTarget.value === 'recurring')}
              >
                <option value="recurring">{t(`goals.recurring.${period}`)}</option>
                <option value="once">
                  {goal && !goal.recurring
                    ? t('goals.onceSaved')
                    : t(`goals.onceCurrent.${period}`)}
                </option>
              </select>
            )}
          </FormField>
          <FormField label={t('goals.visibility')} className="text-sm font-medium col-span-2">
            {(id) => (
              <>
                <select
                  id={id}
                  className={selectClass}
                  value={privacy}
                  aria-describedby="goal-visibility-help"
                  onChange={(e) => setPrivacy(e.currentTarget.value as GoalPrivacy)}
                >
                  {goalPrivacies.map((value) => (
                    <option key={value} value={value}>
                      {privacyLabel(value)}
                    </option>
                  ))}
                </select>
                <span
                  id="goal-visibility-help"
                  className="mt-2 block text-xs font-normal text-muted-foreground"
                >
                  {t('goals.visibilityHelp')}
                </span>
                {revealsHiddenActivities && (
                  <span role="status" className="mt-2 block text-xs font-normal text-destructive">
                    {t('goals.visibilityRevealsActivities')}
                  </span>
                )}
              </>
            )}
          </FormField>
          {(save.error || remove.error) && (
            <p role="alert" className="col-span-2 text-sm text-destructive">
              <ErrorMessage error={(save.error ?? remove.error)!} />
            </p>
          )}
          {confirmingDelete ? (
            <div
              role="group"
              aria-labelledby="goal-delete-question"
              className="col-span-2 rounded-lg border border-destructive/40 bg-destructive/5 p-3"
            >
              <p id="goal-delete-question" className="text-sm">
                {t('goals.confirmDelete')}
              </p>
              <div className="mt-3 flex flex-wrap justify-end gap-3">
                <Button type="button" variant="outline" onClick={() => setConfirmingDelete(false)}>
                  {t('goals.cancel')}
                </Button>
                <Button type="button" variant="destructive" onClick={() => remove.mutate()}>
                  {remove.isPending ? t('goals.deleting') : t('goals.deleteConfirmed')}
                </Button>
              </div>
            </div>
          ) : (
            <div className="col-span-2 flex flex-wrap items-center gap-3 pt-2">
              {goal && (
                <Button
                  type="button"
                  variant="ghost"
                  className="text-destructive"
                  onClick={() => setConfirmingDelete(true)}
                >
                  {t('goals.delete')}
                </Button>
              )}
              <div className="ml-auto flex gap-3">
                <Button type="button" variant="outline" onClick={() => dialog.current?.close()}>
                  {t('goals.cancel')}
                </Button>
                <Button type="submit" disabled={parsedTarget === null}>
                  {save.isPending ? t('goals.saving') : t('goals.save')}
                </Button>
              </div>
            </div>
          )}
        </fieldset>
      </form>
    </Modal>
  )
}
