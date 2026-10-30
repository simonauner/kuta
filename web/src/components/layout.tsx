import { useQuery } from '@tanstack/react-query'
import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { Activity, BarChart3, Home, User } from 'lucide-react'
import { motion } from 'motion/react'
import { meQuery } from '@/lib/auth'
import { cn } from '@/lib/utils'
import { AuthPage, ChooseNamePage, ResetPasswordPage } from '@/routes/auth'

const tabs = [
  { to: '/app', label: 'Flöde', icon: Home },
  { to: '/app/pass', label: 'Pass', icon: Activity },
  { to: '/app/statistik', label: 'Statistik', icon: BarChart3 },
  { to: '/app/profil', label: 'Profil', icon: User },
] as const

export function Layout() {
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const me = useQuery(meQuery)

  if (new URLSearchParams(window.location.search).get('reset') === 'password') return <ResetPasswordPage />
  if (new URLSearchParams(window.location.search).get('reset') === 'failed') return <AuthPage />

  if (me.isPending) return null
  if (me.isError)
    return (
      <p role="alert" className="p-6 text-muted-foreground">
        Kunde inte nå Kuta. Kontrollera anslutningen och ladda om sidan.
      </p>
    )
  if (!me.data) return <AuthPage />
  if (me.data.displayName === null) return <ChooseNamePage />

  return (
    <div className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <main className="flex-1 px-4 pb-28 pt-6">
        <motion.div
          key={pathname}
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.18, ease: 'easeOut' }}
        >
          <Outlet />
        </motion.div>
      </main>

      <nav
        className="fixed inset-x-0 bottom-0 border-t bg-background/80 backdrop-blur"
        style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
      >
        <ul className="mx-auto flex max-w-2xl justify-around">
          {tabs.map(({ to, label, icon: Icon }) => {
            const active = to === '/app' ? pathname === '/app' || pathname === '/app/' : pathname.startsWith(to)
            return (
              <li key={to}>
                <Link
                  to={to}
                  className={cn(
                    'flex flex-col items-center gap-1 px-4 py-3 text-xs transition-colors',
                    active ? 'text-primary' : 'text-muted-foreground',
                  )}
                >
                  <Icon className="size-5" />
                  {label}
                </Link>
              </li>
            )
          })}
        </ul>
      </nav>
    </div>
  )
}
