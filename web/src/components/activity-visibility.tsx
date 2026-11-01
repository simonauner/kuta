import { useTranslation } from 'react-i18next'
import { SharingHints } from '@/components/privacy-places'
import { FormField } from '@/components/ui/form-field'
import { Select } from '@/components/ui/select'

export type Visibility = 'private' | 'followers' | 'public'

// Who may see one activity, with the same hints when creating and editing.
export function VisibilityField({
  value,
  onChange,
  disabled,
}: {
  value: Visibility
  onChange: (value: Visibility) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  return (
    <FormField label={t('social.visibility')} className="text-sm font-medium">
      {(id) => (
        <>
          <Select
            id={id}
            value={value}
            disabled={disabled}
            onChange={(event) => onChange(event.target.value as Visibility)}
          >
            <option value="private">{t('social.private')}</option>
            <option value="followers">{t('social.followersVisibility')}</option>
            <option value="public">{t('social.public')}</option>
          </Select>
          {value !== 'private' && (
            <span className="mt-2 block text-xs font-normal text-muted-foreground">
              {t('social.sharingHint')}
            </span>
          )}
          <SharingHints privacy={value} />
        </>
      )}
    </FormField>
  )
}
