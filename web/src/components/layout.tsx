import { Footer } from '@/components/footer'
import { LanguageSelect } from '@/components/language-select'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { Activity, BarChart3, Home, User } from 'lucide-react'
import { motion } from 'motion/react'
import { meQuery } from '@/lib/auth'
import { pendingRequests, followsQuery } from '@/lib/follows'
import { cn } from '@/lib/utils'
import { AuthPage, ChooseNamePage, ResetPasswordPage } from '@/routes/auth'

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
  const follows = useQuery({ ...followsQuery, enabled: !!me.data?.displayName })
  const requests = pendingRequests(follows.data).length

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
            className="hidden shrink-0 rounded-md focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring md:block"
          >
            <picture>
              <source media="(prefers-color-scheme: dark)" srcSet="/brand/logo-light.svg" />
              <img src="/brand/logo.svg" alt="Kuta" width="96" height="32" className="h-8 w-auto" />
            </picture>
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
                      <span className="relative">
                        <Icon aria-hidden="true" className="size-5" />
                        {to === '/app' && requests > 0 && (
                          <span
                            aria-hidden="true"
                            className="absolute -right-2 -top-1.5 flex min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold leading-4 text-primary-foreground"
                          >
                            {requests}
                          </span>
                        )}
                      </span>
                      {t(label)}
                      {to === '/app' && requests > 0 && (
                        <span className="sr-only">
                          {t('nav.followRequests', { count: requests })}
                        </span>
                      )}
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
          initial={pathname.startsWith('/app/activities/') ? false : { opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.18, ease: 'easeOut' }}
        >
          <Outlet />
        </motion.div>
      </main>

      <Footer
        variant="app"
        language={i18n.resolvedLanguage}
        labels={{
          privacy: t('footer.privacy'),
          terms: t('footer.terms'),
          openApp: t('footer.openApp'),
          links: t('footer.links'),
        }}
        version={t('footer.version', {
          version: import.meta.env.DEV ? t('footer.development') : import.meta.env.VITE_APP_VERSION,
        })}
      />
    </div>
  )
}
