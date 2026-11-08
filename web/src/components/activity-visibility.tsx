import { useTranslation } from 'react-i18next'
import { SharingHints } from '@/components/privacy-places'
import { FormField } from '@/components/ui/form-field'
import { PrivacySelect } from '@/components/privacy-select'
import type { Privacy } from '@/lib/privacy'

// Who may see one activity, with the same hints when creating and editing.
export function VisibilityField({
  value,
  onChange,
  disabled,
}: {
  value: Privacy
  onChange: (value: Privacy) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  return (
    <FormField label={t('social.visibility')} className="text-sm font-medium">
      {(id) => (
        <>
          <PrivacySelect id={id} value={value} disabled={disabled} onChange={onChange} />
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
