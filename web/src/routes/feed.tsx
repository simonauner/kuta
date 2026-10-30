import { useTranslation } from 'react-i18next'
export function FeedPage() {
  const { t } = useTranslation()
  return (
    <section>
      <h1 className="text-2xl font-semibold tracking-tight">{t('nav.feed')}</h1>
      <p className="mt-2 text-muted-foreground">{t('feed.description')}</p>
    </section>
  )
}
