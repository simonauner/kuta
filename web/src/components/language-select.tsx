import { useId } from 'react'
import { useTranslation } from 'react-i18next'

export function LanguageSelect() {
  const { t, i18n } = useTranslation()
  const id = useId()
  return (
    <div className="flex items-center justify-end gap-2 text-sm">
      <label htmlFor={id}>{t('language')}</label>
      <select
        id={id}
        className="rounded-md border bg-background px-2 py-1 text-foreground"
        value={i18n.resolvedLanguage}
        onChange={(e) => {
          void i18n.changeLanguage(e.target.value)
        }}
      >
        <option value="sv" lang="sv">
          Svenska
        </option>
        <option value="en" lang="en">
          English
        </option>
      </select>
    </div>
  )
}
