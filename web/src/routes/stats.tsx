import { useTranslation } from 'react-i18next'
export function StatsPage() {
  const { t } = useTranslation()
  return (
    <section>
      <h1 className="text-2xl font-semibold tracking-tight">{t('nav.stats')}</h1>
      <p className="mt-2 text-muted-foreground">{t('stats.description')}</p>
    </section>
  )
}
