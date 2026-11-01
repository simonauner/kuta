import { FormField } from '@/components/ui/form-field'
import { useTranslation } from 'react-i18next'
import { Select } from '@/components/ui/select'

export function LanguageSelect() {
  const { t, i18n } = useTranslation()
  return (
    <FormField label={t('language')} hideLabel layout="inline" className="justify-end text-sm">
      {(id) => (
        <Select
          id={id}
          size="sm"
          className="w-fit"
          value={i18n.resolvedLanguage}
          onChange={(e) => {
            void i18n.changeLanguage(e.target.value)
          }}
        >
          <option value="sv" lang="sv">
            🇸🇪 Svenska
          </option>
          <option value="en" lang="en">
            🇬🇧 English
          </option>
        </Select>
      )}
    </FormField>
  )
}
