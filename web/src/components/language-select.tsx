import { FormField } from '@/components/ui/form-field'
import { useTranslation } from 'react-i18next'

export function LanguageSelect() {
  const { t, i18n } = useTranslation()
  return (
    <FormField label={t('language')} hideLabel layout="inline" className="justify-end text-sm">
      {(id) => (
        <select
          id={id}
          className="rounded-md border bg-background px-2 py-1 text-foreground"
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
        </select>
      )}
    </FormField>
  )
}
