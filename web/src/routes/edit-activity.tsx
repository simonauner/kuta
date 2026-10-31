import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { EquipmentPicker } from '@/components/equipment'
import { TagPicker } from '@/components/activity-tags'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ApiError, api } from '@/lib/api'
import { activityQuery } from '@/lib/activity-query'
import { activityTitle, type ActivityDetail } from '@/lib/activity'
import { equipmentQuery, lastUsedShoeQuery } from '@/lib/equipment'

export function EditActivityPage() {
  const { activityId } = useParams({ from: '/app/activities/$activityId/edit' })
  const activity = useQuery(activityQuery(activityId))
  const { t } = useTranslation()
  if (activity.isPending) return null
  if (activity.isError)
    return (
      <p role="alert">
        {activity.error instanceof ApiError && activity.error.status === 404 ? (
          t('activity.notFound')
        ) : (
          <ErrorMessage error={activity.error} />
        )}
      </p>
    )
  return <EditActivityForm key={activityId} activity={activity.data} />
}

function EditActivityForm({ activity }: { activity: ActivityDetail }) {
  const { t } = useTranslation()
  const { year, tag } = useSearch({ from: '/app/activities/$activityId/edit' })
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [title, setTitle] = useState(activity.title ?? '')
  const [description, setDescription] = useState(activity.description ?? '')
  const [tags, setTags] = useState(activity.tags)
  const [equipmentIds, setEquipmentIds] = useState(activity.equipmentIds)
  const destination = {
    to: '/app/activities/$activityId' as const,
    params: { activityId: activity.id },
    search: { year, tag },
  }
  const save = useMutation({
    mutationFn: () =>
      api.send('PUT', `/api/activities/${activity.id}`, { title, description, tags, equipmentIds }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: activityQuery(activity.id).queryKey }),
        queryClient.invalidateQueries({ queryKey: ['activities'] }),
        queryClient.invalidateQueries({ queryKey: ['records'] }),
        queryClient.invalidateQueries({ queryKey: equipmentQuery.queryKey }),
        queryClient.invalidateQueries({ queryKey: lastUsedShoeQuery.queryKey }),
      ])
      await navigate(destination)
    },
  })
  return (
    <section>
      <h1 className="text-2xl font-semibold tracking-tight">{t('activity.edit')}</h1>
      <p className="mt-2 text-sm text-muted-foreground">{activityTitle(activity, t)}</p>
      <form
        className="mt-6 space-y-6"
        onSubmit={(event) => {
          event.preventDefault()
          if (!save.isPending) save.mutate()
        }}
      >
        <label className="block space-y-2 text-sm font-medium">
          <span>{t('newActivity.name')}</span>
          <Input
            value={title}
            maxLength={100}
            placeholder={activityTitle({ ...activity, title: null }, t)}
            onChange={(event) => setTitle(event.currentTarget.value)}
            disabled={save.isPending}
          />
        </label>
        <label className="block space-y-2 text-sm font-medium">
          <span>{t('newActivity.description')}</span>
          <textarea
            value={description}
            maxLength={10000}
            rows={3}
            onChange={(event) => setDescription(event.currentTarget.value)}
            disabled={save.isPending}
            className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
          />
        </label>
        <EquipmentPicker
          value={equipmentIds}
          initialIds={activity.equipmentIds}
          onChange={setEquipmentIds}
          disabled={save.isPending}
        />
        <TagPicker value={tags} onChange={setTags} disabled={save.isPending} />
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
        <div className="flex gap-3">
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? t('newActivity.saving') : t('activity.save')}
          </Button>
          {save.isPending ? (
            <Button type="button" variant="outline" disabled>
              {t('activity.cancel')}
            </Button>
          ) : (
            <Button asChild variant="outline">
              <Link {...destination}>{t('activity.cancel')}</Link>
            </Button>
          )}
        </div>
      </form>
    </section>
  )
}
