import {
  createRootRoute,
  createRoute,
  createRouter,
} from '@tanstack/react-router'
import { Layout } from '@/components/layout'
import { ActivitiesPage } from '@/routes/activities'
import { FeedPage } from '@/routes/feed'
import { ProfilePage } from '@/routes/profile'
import { StatsPage } from '@/routes/stats'

const rootRoute = createRootRoute({ component: Layout })

const feedRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app',
  component: FeedPage,
})
const activitiesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/pass',
  component: ActivitiesPage,
})
const statsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/statistik',
  component: StatsPage,
})
const profileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/profil',
  component: ProfilePage,
})

const routeTree = rootRoute.addChildren([
  feedRoute,
  activitiesRoute,
  statsRoute,
  profileRoute,
])

export const router = createRouter({ routeTree, defaultPreload: 'intent' })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
