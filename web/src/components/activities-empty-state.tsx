import { ChartNoAxesCombined, NotebookPen, UserRound } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { AddActivityButton, StravaImportLink } from '@/components/onboarding-actions'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'

const icons = { activities: NotebookPen, stats: ChartNoAxesCombined, profile: UserRound }

// The full welcome lives in the feed; the other tabs only say what will appear and offer the same first step.
export function ActivitiesEmptyState({ context }: { context: 'stats' | 'activities' | 'profile' }) {
  const { t } = useTranslation()
  const Icon = icons[context]
  return (
    <Card padding="spacious" className="mt-6 text-center">
      <Icon aria-hidden="true" className="mx-auto mb-4 size-6 text-primary" />
      <Heading level={2}>{t(`emptyState.${context}.title`)}</Heading>
      <p className="mx-auto mt-2 max-w-md text-body text-muted-foreground">
        {t(`emptyState.${context}.description`)}
      </p>
      <div className="mt-6 flex flex-col items-center gap-3">
        <AddActivityButton />
        <StravaImportLink />
      </div>
    </Card>
  )
}
