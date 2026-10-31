import { LanguageSelect } from '@/components/language-select'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { Activity, BarChart3, Home, User } from 'lucide-react'
import { motion } from 'motion/react'
import { meQuery } from '@/lib/auth'
import { cn } from '@/lib/utils'
import { AuthPage, ChooseNamePage, ResetPasswordPage } from '@/routes/auth'

const copyrightYear = new Date().getFullYear()

const tabs = [
  { to: '/app', label: 'nav.feed', icon: Home },
  { to: '/app/activities', label: 'nav.activities', icon: Activity },
  { to: '/app/stats', label: 'nav.stats', icon: BarChart3 },
  { to: '/app/profile', label: 'nav.profile', icon: User },
] as const

export function Layout() {
  const { t, i18n } = useTranslation()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const me = useQuery(meQuery)

  if (new URLSearchParams(window.location.search).get('reset') === 'password')
    return <ResetPasswordPage />
  if (new URLSearchParams(window.location.search).get('reset') === 'failed') return <AuthPage />

  if (me.isPending) return null
  if (me.isError)
    return (
      <div className="mx-auto max-w-2xl p-6">
        <LanguageSelect />
        <p role="alert" className="mt-4 text-muted-foreground">
          {t('connectionError')}
        </p>
      </div>
    )
  if (!me.data) return <AuthPage />
  if (me.data.displayName === null) return <ChooseNamePage />

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="fixed inset-x-0 bottom-0 z-20 border-t bg-background/80 pb-[env(safe-area-inset-bottom)] backdrop-blur md:sticky md:bottom-auto md:top-0 md:border-b md:border-t-0 md:pb-0">
        <div className="mx-auto flex max-w-5xl items-center md:gap-4 md:px-4 md:py-3">
          <Link
            to="/app"
            activeOptions={{ exact: true }}
            className="hidden rounded-md text-2xl font-semibold tracking-tight text-primary focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring md:block"
          >
            Kuta
          </Link>
          <nav aria-label={t('nav.main')} className="flex-1">
            <ul className="flex justify-around md:justify-end md:gap-1">
              {tabs.map(({ to, label, icon: Icon }) => {
                const active =
                  to === '/app'
                    ? pathname === '/app' || pathname === '/app/'
                    : pathname.startsWith(to)
                return (
                  <li key={to}>
                    <Link
                      to={to}
                      activeOptions={{ exact: to === '/app' }}
                      aria-current={active ? 'page' : undefined}
                      className={cn(
                        'flex flex-col items-center gap-1 px-4 py-3 text-xs transition-colors hover:text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring md:flex-row md:gap-2 md:rounded-lg md:px-3 md:text-sm md:font-medium',
                        active ? 'text-primary md:bg-primary/10' : 'text-muted-foreground',
                      )}
                    >
                      <Icon aria-hidden="true" className="size-5" />
                      {t(label)}
                    </Link>
                  </li>
                )
              })}
            </ul>
          </nav>
        </div>
      </header>

      <main className="mx-auto w-full max-w-2xl flex-1 px-4 pb-8 pt-6">
        <motion.div
          key={pathname}
          initial={pathname.startsWith('/app/activities') ? false : { opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.18, ease: 'easeOut' }}
        >
          <Outlet />
        </motion.div>
      </main>

      <footer className="border-t pb-[calc(4rem+env(safe-area-inset-bottom))] md:pb-0">
        <div className="mx-auto flex max-w-2xl flex-wrap items-center justify-between gap-2 px-4 py-5 text-xs text-muted-foreground">
          <p>© {copyrightYear} Kuta</p>
          <a
            href={i18n.resolvedLanguage === 'en' ? '/privacy#english' : '/privacy'}
            className="underline underline-offset-4"
          >
            {t('footer.privacy')}
          </a>
          <p>
            {t('footer.version', {
              version: import.meta.env.DEV
                ? t('footer.development')
                : import.meta.env.VITE_APP_VERSION,
            })}
          </p>
        </div>
      </footer>
    </div>
  )
}
