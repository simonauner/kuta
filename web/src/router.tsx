import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
} from '@tanstack/react-router'
import { Layout } from '@/components/layout'
import { EditActivityPage } from '@/routes/edit-activity'
import { ActivitiesPage } from '@/routes/activities'
import { FollowersPage } from '@/routes/followers'
import { FeedPage } from '@/routes/feed'
import { NewActivityPage } from '@/routes/new-activity'
import { EquipmentPage } from '@/routes/equipment'
import { RunnerPage } from '@/routes/runner'
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
  validateSearch: activitySearch,
  component: NewActivityPage,
})
// The activity page loads only when opened: the map (MapLibre) and charts are heavy
const activityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities/$activityId',
  validateSearch: activitySearch,
  // Wait for the title before the snapshot; the query keeps the activity page's error handling.
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

const runnerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/runners/$runnerId',
  component: RunnerPage,
})

const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings',
  component: SettingsPage,
})

const followersRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/followers',
  component: FollowersPage,
})

const equipmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/equipment',
  component: EquipmentPage,
})

const privacyPlacesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/privacy-places',
  component: lazyRouteComponent(() => import('@/routes/privacy-places'), 'PrivacyPlacesPage'),
})

const routeTree = rootRoute.addChildren([
  feedRoute,
  activitiesRoute,
  newActivityRoute,
  activityRoute,
  editActivityRoute,
  statsRoute,
  profileRoute,
  runnerRoute,
  equipmentRoute,
  settingsRoute,
  followersRoute,
  privacyPlacesRoute,
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
