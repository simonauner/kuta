import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
} from '@tanstack/react-router'
import { Layout } from '@/components/layout'
import { EditActivityPage } from '@/routes/edit-activity'
import { ActivitiesPage } from '@/routes/activities'
import { FeedPage } from '@/routes/feed'
import { NewActivityPage } from '@/routes/new-activity'
import { EquipmentPage } from '@/routes/equipment'
import { ProfilePage } from '@/routes/profile'
import { SettingsPage } from '@/routes/settings'
import { StatsPage } from '@/routes/stats'
import { activitySearch } from '@/lib/activity'
import { activityQuery } from '@/lib/activity-query'
import { queryClient } from '@/lib/query-client'

const rootRoute = createRootRoute({ component: Layout })

const feedRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app',
  component: FeedPage,
})
const activitiesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities',
  validateSearch: activitySearch,
  component: ActivitiesPage,
})
const newActivityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities/new',
  component: NewActivityPage,
})
// Passsidan laddas först när den öppnas: kartan (MapLibre) och diagrammen är tunga
const activityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities/$activityId',
  validateSearch: activitySearch,
  // Vänta in titeln före ögonblicksbilden; queryn behåller passsidans felhantering.
  loader: ({ params }) => queryClient.prefetchQuery(activityQuery(params.activityId)),
  component: lazyRouteComponent(() => import('@/routes/activity'), 'ActivityPage'),
})
const editActivityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities/$activityId/edit',
  validateSearch: activitySearch,
  component: EditActivityPage,
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

const equipmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/profile/equipment',
  component: EquipmentPage,
})

const routeTree = rootRoute.addChildren([
  feedRoute,
  activitiesRoute,
  newActivityRoute,
  activityRoute,
  editActivityRoute,
  statsRoute,
  profileRoute,
  equipmentRoute,
  settingsRoute,
])

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  defaultViewTransition: {
    types: ({ fromLocation, toLocation }) => {
      const from = fromLocation?.pathname ?? ''
      const to = toLocation.pathname
      return !window.matchMedia('(prefers-reduced-motion: reduce)').matches &&
        ((from === '/app/activities' && to.startsWith('/app/activities/')) ||
          (to === '/app/activities' && from.startsWith('/app/activities/')))
        ? ['activity-title']
        : false
    },
  },
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
