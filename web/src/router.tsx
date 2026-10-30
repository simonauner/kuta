import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
} from '@tanstack/react-router'
import { Layout } from '@/components/layout'
import { ActivitiesPage } from '@/routes/activities'
import { FeedPage } from '@/routes/feed'
import { ProfilePage } from '@/routes/profile'
import { SettingsPage } from '@/routes/settings'
import { StatsPage } from '@/routes/stats'

const rootRoute = createRootRoute({ component: Layout })

const feedRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app',
  component: FeedPage,
})
const activitiesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities',
  component: ActivitiesPage,
})
// Passsidan laddas först när den öppnas: kartan (MapLibre) och diagrammen är tunga
const activityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities/$activityId',
  component: lazyRouteComponent(() => import('@/routes/activity'), 'ActivityPage'),
})
const statsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats',
  component: StatsPage,
})
const profileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/profile',
  component: ProfilePage,
})

const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings',
  component: SettingsPage,
})

const routeTree = rootRoute.addChildren([
  feedRoute,
  activitiesRoute,
  activityRoute,
  statsRoute,
  profileRoute,
  settingsRoute,
])

export const router = createRouter({ routeTree, defaultPreload: 'intent' })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
