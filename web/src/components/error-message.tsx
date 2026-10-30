import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'

export function ErrorMessage({ error }: { error: Error }) {
  const { t } = useTranslation()
  const key = error instanceof ApiError ? `errors.${error.code}` : 'errors.network'
  return t(key as 'errors.generic', { defaultValue: t('errors.generic') })
}
