import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Trophy } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { GarminDerivedNote } from '@/components/garmin'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'
import { Modal } from '@/components/ui/modal'
import { achievementText, achievementsQuery, type Achievement } from '@/lib/achievements'
import { api } from '@/lib/api'

const timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone

function AchievementItem({ achievement }: { achievement: Achievement }) {
  const { t, i18n } = useTranslation()
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const { title, detail } = achievementText(achievement, t, i18n.resolvedLanguage)
  return (
    <li className="flex gap-3">
      <span className="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
        <Trophy aria-hidden="true" className="size-5" />
      </span>
      <div className="min-w-0">
        <p className="font-semibold">{title}</p>
        <p className="text-sm text-muted-foreground">{detail}</p>
        <p className="mt-1 text-metadata text-muted-foreground">
          {date.format(new Date(achievement.achievedAt))} ·{' '}
          <Link
            to="/app/activities/$activityId"
            params={{ activityId: achievement.activityId }}
            search={{ returnTo: window.location.pathname }}
            className="underline underline-offset-4"
          >
            {t('achievements.showActivity')}
          </Link>
        </p>
      </div>
    </li>
  )
}

// Shown once when the app is opened after something new was reached; closing marks the shown ones as seen.
export function AchievementTeaser() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const query = achievementsQuery(timeZone())
  const achievements = useQuery(query)
  const unseen = achievements.data?.filter((a) => !a.seen) ?? []
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const seen = useMutation({
    mutationFn: (keys: string[]) =>
      api.send('POST', '/api/achievements/seen', { keys, timeZone: timeZone() }),
  })
  const open = unseen.length > 0 && !seen.isPending && !seen.isSuccess

  useEffect(() => {
    if (open && !dialog.current?.open) dialog.current?.showModal()
  }, [open])

  const close = () => {
    dialog.current?.close()
    const keys = unseen.map((a) => a.key)
    // Marked as seen here too, so the teaser doesn't come back if the request fails before the next start.
    queryClient.setQueryData(query.queryKey, (list) =>
      list?.map((a) => (keys.includes(a.key) ? { ...a, seen: true } : a)),
    )
    seen.mutate(keys)
  }

  if (unseen.length === 0) return null
  return (
    <Modal
      ref={dialog}
      aria-labelledby={titleId}
      onCancel={(event) => {
        event.preventDefault()
        close()
      }}
    >
      <Heading level={2} id={titleId}>
        {t('achievements.teaserTitle', { count: unseen.length })}
      </Heading>
      <GarminDerivedNote className="mt-1" />
      <ul className="mt-4 space-y-4">
        {unseen.map((a) => (
          <AchievementItem key={a.key} achievement={a} />
        ))}
      </ul>
      <div className="mt-6 flex justify-end">
        {/* Focus on the button rather than the first activity link */}
        <Button type="button" autoFocus onClick={close}>
          {t('achievements.close')}
        </Button>
      </div>
    </Modal>
  )
}

const shownAtFirst = 5

// All achievements on the own profile, newest first
export function AchievementList() {
  const { t } = useTranslation()
  const achievements = useQuery(achievementsQuery(timeZone()))
  const [all, setAll] = useState(false)
  if (!achievements.data?.length) return null
  const shown = all ? achievements.data : achievements.data.slice(0, shownAtFirst)
  return (
    <section aria-labelledby="achievements-title">
      <Heading level={2} id="achievements-title">
        {t('achievements.title')}
      </Heading>
      <GarminDerivedNote className="mt-1" />
      <Card className="mt-3">
        <ul className="space-y-4">
          {shown.map((a) => (
            <AchievementItem key={a.key} achievement={a} />
          ))}
        </ul>
        {shown.length < achievements.data.length && (
          <Button variant="link" className="mt-3" onClick={() => setAll(true)}>
            {t('achievements.showAll', { count: achievements.data.length })}
          </Button>
        )}
      </Card>
    </section>
  )
}
