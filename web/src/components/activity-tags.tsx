import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { activityTagsQuery } from '@/lib/activity-query'
import type { ActivityTag } from '@/lib/activity'
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
