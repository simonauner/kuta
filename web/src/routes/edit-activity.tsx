import { FormField } from '@/components/ui/form-field'
import { MultisportEditor } from '@/components/multisport-editor'
import { multisportView, partsFromForm, type MultisportDraft } from '@/lib/multisport'
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { EquipmentPicker } from '@/components/equipment'
import { TagPicker } from '@/components/activity-tags'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { meQuery } from '@/lib/auth'
import { ApiError, api } from '@/lib/api'
import { activityQuery, refreshActivityQueries } from '@/lib/activity-query'
import { VisibilityField } from '@/components/activity-visibility'
import { activityTitle, type ActivityDetail } from '@/lib/activity'
import { Heading } from '@/components/ui/heading'

export function EditActivityPage() {
  const { activityId } = useParams({ from: '/app/activities/$activityId/edit' })
  const activity = useQuery(activityQuery(activityId))
  const { data: me } = useQuery(meQuery)
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
  if (me?.id !== activity.data.userId) return <p role="alert">{t('activity.notFound')}</p>
  return <EditActivityForm key={activityId} activity={activity.data} />
}

function EditActivityForm({ activity }: { activity: ActivityDetail }) {
  const { t } = useTranslation()
  const search = useSearch({ from: '/app/activities/$activityId/edit' })
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [split, setSplit] = useState<MultisportDraft>(undefined)
  const [title, setTitle] = useState(activity.title ?? '')
  const [description, setDescription] = useState(activity.description ?? '')
  const [tags, setTags] = useState(activity.tags)
  const [excludeFromRecords, setExcludeFromRecords] = useState(activity.excludeFromRecords)
  const [privacy, setPrivacy] = useState(activity.privacy)
  const [equipmentIds, setEquipmentIds] = useState(activity.equipmentIds)
  const destination = {
    to: '/app/activities/$activityId' as const,
    params: { activityId: activity.id },
    search,
  }
  const save = useMutation({
    mutationFn: (draft: MultisportDraft) =>
      api.send('PUT', `/api/activities/${activity.id}`, {
        privacy,
        excludeFromRecords,
        title,
        description,
        tags,
        equipmentIds,
        multisport: draft ?? undefined,
        restoreParts: draft === null,
      }),
    onSuccess: async () => {
      await refreshActivityQueries(queryClient)
      await navigate(destination)
    },
  })
  return (
    <section>
      <Heading level={1}>{t('activity.edit')}</Heading>
      <p className="mt-2 text-sm text-muted-foreground">{activityTitle(activity, t)}</p>
      <form
        className="mt-6 space-y-6"
        onSubmit={(event) => {
          event.preventDefault()
          if (!save.isPending) save.mutate(partsFromForm(split, event.currentTarget))
        }}
      >
        <FormField label={t('newActivity.name')} className="text-sm font-medium">
          {(id) => (
            <Input
              id={id}
              value={title}
              maxLength={100}
              placeholder={activityTitle({ ...activity, title: null }, t)}
              onChange={(event) => setTitle(event.currentTarget.value)}
              disabled={save.isPending}
            />
          )}
        </FormField>
        <FormField label={t('newActivity.description')} className="text-sm font-medium">
          {(id) => (
            <textarea
              id={id}
              value={description}
              maxLength={10000}
              rows={3}
              onChange={(event) => setDescription(event.currentTarget.value)}
              disabled={save.isPending}
              className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
            />
          )}
        </FormField>
        <VisibilityField value={privacy} onChange={setPrivacy} disabled={save.isPending} />
        {activity.streams && (
          <div className="space-y-2">
            <FormField label={t('activity.excludeFromRecords')} layout="choice">
              {(id) => (
                <input
                  id={id}
                  type="checkbox"
                  checked={excludeFromRecords}
                  onChange={(event) => setExcludeFromRecords(event.currentTarget.checked)}
                  disabled={save.isPending}
                  className="size-4 accent-primary"
                />
              )}
            </FormField>
            <p className="text-xs text-muted-foreground">{t('activity.excludeFromRecordsHint')}</p>
          </div>
        )}
        <MultisportEditor
          activity={multisportView(activity, split)}
          onChange={setSplit}
          disabled={save.isPending}
        />
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
            <Button type="button" variant="secondary-outline" disabled>
              {t('activity.cancel')}
            </Button>
          ) : (
            <Button asChild variant="secondary-outline">
              <Link {...destination}>{t('activity.cancel')}</Link>
            </Button>
          )}
        </div>
      </form>
    </section>
  )
}
