import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { Activity, BarChart3, Home, User } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { cn } from '@/lib/utils'

const tabs = [
  { to: '/', label: 'Flöde', icon: Home },
  { to: '/pass', label: 'Pass', icon: Activity },
  { to: '/statistik', label: 'Statistik', icon: BarChart3 },
  { to: '/profil', label: 'Profil', icon: User },
] as const

export function Layout() {
  const pathname = useRouterState({ select: (s) => s.location.pathname })

  return (
    <div className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <main className="flex-1 px-4 pb-28 pt-6">
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={pathname}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }}
            transition={{ duration: 0.18, ease: 'easeOut' }}
          >
            <Outlet />
          </motion.div>
        </AnimatePresence>
      </main>

      <nav
        className="fixed inset-x-0 bottom-0 border-t bg-background/80 backdrop-blur"
        style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
      >
        <ul className="mx-auto flex max-w-2xl justify-around">
          {tabs.map(({ to, label, icon: Icon }) => {
            const active = to === '/' ? pathname === '/' : pathname.startsWith(to)
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
