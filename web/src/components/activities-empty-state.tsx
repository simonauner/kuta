import {
  ChartNoAxesCombined,
  FileArchive,
  Flag,
  Footprints,
  NotebookPen,
  ShieldCheck,
  Trophy,
  UserRound,
  Users,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { AddActivityButton, StravaImportLink } from '@/components/onboarding-actions'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'

const features = {
  stats: [
    [ChartNoAxesCombined, 'stats.onboarding.volume'],
    [Flag, 'stats.onboarding.compare'],
    [Trophy, 'stats.onboarding.records'],
  ],
  activities: [
    [NotebookPen, 'activities.onboarding.history'],
    [FileArchive, 'activities.onboarding.import'],
    [Footprints, 'activities.onboarding.equipment'],
  ],
  profile: [
    [UserRound, 'profile.onboarding.identity'],
    [Users, 'profile.onboarding.connections'],
    [ShieldCheck, 'profile.onboarding.privacy'],
  ],
} as const

export function ActivitiesEmptyState({ context }: { context: 'stats' | 'activities' | 'profile' }) {
  const { t } = useTranslation()

  return (
    <Card padding="none" className="mt-6 overflow-hidden">
      <div className="px-6 py-10 text-center sm:px-10 sm:py-12">
        <svg
          viewBox="0 0 320 140"
          className="mx-auto mb-6 w-full max-w-72 text-primary"
          aria-hidden="true"
        >
          <path d="M24 116H296M24 76H296M24 36H296" className="stroke-border" fill="none" />
          <path
            d="M32 116C64 116 60 88 96 88S132 102 160 72S200 84 224 48S260 52 288 20"
            fill="none"
            stroke="currentColor"
            strokeWidth="3"
            strokeDasharray="5 7"
            opacity="0.4"
          />
          <circle cx="32" cy="116" r="12" fill="currentColor" opacity="0.15" />
          <circle cx="32" cy="116" r="5" fill="currentColor" />
          <path
            d="M288 20V4L306 10L288 16"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinejoin="round"
          />
        </svg>
        <p className="text-xs font-medium tracking-widest text-primary uppercase">
          {t('onboarding.eyebrow')}
        </p>
        <Heading level={2} className="mt-3">
          {t(`${context}.onboarding.title`)}
        </Heading>
        <p className="mx-auto mt-3 max-w-md text-sm leading-relaxed text-muted-foreground">
          {t(`${context}.onboarding.description`)}
        </p>
        <div className="mt-6 flex flex-col items-center gap-3">
          <AddActivityButton />
          <StravaImportLink />
        </div>
      </div>
      <div className="grid gap-6 border-t bg-muted/20 p-6 sm:grid-cols-3 sm:gap-5 sm:p-8">
        {features[context].map(([Icon, key]) => (
          <div key={key}>
            <Icon className="mb-3 size-5 text-primary" aria-hidden="true" />
            <Heading level={3}>{t(`${key}Title`)}</Heading>
            <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
              {t(`${key}Description`)}
            </p>
          </div>
        ))}
      </div>
    </Card>
  )
}
