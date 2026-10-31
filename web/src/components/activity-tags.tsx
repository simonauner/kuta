import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { activityTagsQuery, activityQuery } from '@/lib/activity-query'
import type { ActivityTag } from '@/lib/activity'
import { Button } from '@/components/ui/button'
import { ErrorMessage } from '@/components/error-message'

export function TagPicker({
  value,
  onChange,
  disabled = false,
}: {
  value: ActivityTag[]
  onChange: (tags: ActivityTag[]) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const options = useQuery(activityTagsQuery)
  return (
    <fieldset disabled={disabled}>
      <legend className="mb-2 text-sm font-medium">{t('tags.label')}</legend>
      <div className="flex flex-wrap gap-3">
        {options.data?.map((tag) => (
          <label
            key={tag}
            className="flex cursor-pointer items-center gap-2 rounded-md border px-3 py-2 text-sm"
          >
            <input
              type="checkbox"
              checked={value.includes(tag)}
              onChange={(e) =>
                onChange(e.currentTarget.checked ? [...value, tag] : value.filter((v) => v !== tag))
              }
            />
            {t(`tagNames.${tag}`)}
          </label>
        ))}
      </div>
      {options.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={options.error} />
        </p>
      )}
    </fieldset>
  )
}

export function ActivityTagEditor({
  activityId,
  initialTags,
}: {
  activityId: string
  initialTags: ActivityTag[]
}) {
  const { t } = useTranslation()
  const [tags, setTags] = useState(initialTags)
  const queryClient = useQueryClient()
  const save = useMutation({
    mutationFn: () => api.send('PUT', `/api/activities/${activityId}/tags`, { tags }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: activityQuery(activityId).queryKey }),
        queryClient.invalidateQueries({ queryKey: ['activities'] }),
        queryClient.invalidateQueries({ queryKey: ['records'] }),
      ])
    },
  })
  const changed =
    tags.length !== initialTags.length || tags.some((tag) => !initialTags.includes(tag))
  return (
    <form
      className="mt-5 space-y-3"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <TagPicker
        value={tags}
        onChange={(value) => {
          setTags(value)
          save.reset()
        }}
        disabled={save.isPending}
      />
      {changed && (
        <Button size="sm" disabled={save.isPending}>
          {t('tags.save')}
        </Button>
      )}
      {save.error && (
        <p role="alert" className="text-sm text-destructive">
          <ErrorMessage error={save.error} />
        </p>
      )}
    </form>
  )
}

export function TagLabels({ tags }: { tags: ActivityTag[] }) {
  const { t } = useTranslation()
  return (
    <div className="mt-2 flex flex-wrap gap-2">
      {tags.map((tag) => (
        <span key={tag} className="rounded-full bg-muted px-2 py-0.5 text-xs">
          {t(`tagNames.${tag}`)}
        </span>
      ))}
    </div>
  )
}
