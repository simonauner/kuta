import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'
import { useVisitor } from '@/lib/visitor'

// For a visitor without an account on a profile or activity that is not open to them.
export function VisitorNotice({ kind }: { kind: 'runner' | 'activity' }) {
  const { t } = useTranslation()
  const visitor = useVisitor()
  return (
    <Card padding="spacious" className="mt-6">
      <Heading level={2}>{t('visitor.closedTitle')}</Heading>
      <p className="mt-2 text-body text-muted-foreground">{t(`visitor.closed.${kind}`)}</p>
      <div className="mt-6 flex flex-wrap gap-3">
        <Button onClick={() => visitor?.signIn('signup')}>{t('auth.signup')}</Button>
        <Button variant="secondary-outline" onClick={() => visitor?.signIn('login')}>
          {t('auth.login')}
        </Button>
      </div>
    </Card>
  )
}
