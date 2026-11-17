import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
  redirect,
} from '@tanstack/react-router'
import { Layout } from '@/components/layout'
import { EditActivityPage } from '@/routes/edit-activity'
import { ActivitiesPage } from '@/routes/activities'
import { FollowersPage } from '@/routes/followers'
import { ClubPage, GroupPage, GroupSettingsPage, GroupsPage, JoinGroupPage } from '@/routes/groups'
import { FeedPage } from '@/routes/feed'
import { NewActivityPage } from '@/routes/new-activity'
import { EquipmentPage } from '@/routes/equipment'
import { RunnerPage } from '@/routes/runner'
import { SharedProfilePage } from '@/routes/shared-profile'
import { ProfilePage } from '@/routes/profile'
import { DataSettingsPage, ProfileSettingsPage, SettingsPage } from '@/routes/settings'
import { ImportPage } from '@/routes/import'
import { AnalysisPage, StatsPage, StatsRecordsPage } from '@/routes/stats'
import { RoutesPage, routeSearch, savedRouteFilters } from '@/routes/my-routes'
import { YearReviewPage } from '@/routes/year-review'
import { RecordsPage, recordsSearch } from '@/routes/records'
import { AchievementsPage } from '@/routes/achievements'
import { AdminPage } from '@/routes/admin'
import { activityPageSearch, activitySearch } from '@/lib/activity'
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
  validateSearch: activityPageSearch,
  // Wait for the title before the snapshot; the query keeps the activity page's error handling.
  loader: ({ params }) => queryClient.prefetchQuery(activityQuery(params.activityId)),
  component: lazyRouteComponent(() => import('@/routes/activity'), 'ActivityPage'),
})
const editActivityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/activities/$activityId/edit',
  validateSearch: activityPageSearch,
  component: EditActivityPage,
})
const statsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats',
  component: StatsPage,
})
const analysisRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats/analysis',
  component: AnalysisPage,
})
const routesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats/routes',
  validateSearch: routeSearch,
  // Without filters in the URL, the page opens with the ones last chosen in this browser.
  beforeLoad: ({ search }) => {
    const saved = savedRouteFilters()
    if (
      search.year === undefined &&
      !search.sportGroup &&
      (saved.year !== undefined || saved.sportGroup)
    )
      throw redirect({ to: '/app/stats/routes', search: saved, replace: true })
  },
  component: RoutesPage,
})
const recordsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats/records',
  component: StatsRecordsPage,
})
const recordsByYearRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats/records/years',
  validateSearch: recordsSearch,
  component: RecordsPage,
})
const yearReviewRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/stats/year/$year',
  component: YearReviewPage,
})
const profileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/profile',
  component: ProfilePage,
})
const achievementsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/profile/achievements',
  validateSearch: recordsSearch,
  component: AchievementsPage,
})

const runnerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/runners/$runnerId',
  component: RunnerPage,
})

const sharedProfileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/r/$code',
  component: SharedProfilePage,
})

const groupsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/groups',
  component: GroupsPage,
})

const groupRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/groups/$groupId',
  component: GroupPage,
})

const groupSettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/groups/$groupId/settings',
  component: GroupSettingsPage,
})

const clubRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/clubs/$clubId',
  component: ClubPage,
})

const joinGroupRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/g/$code',
  component: JoinGroupPage,
})

const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings',
  component: SettingsPage,
})

const profileSettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/profile',
  component: ProfileSettingsPage,
})

const dataSettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/data',
  component: DataSettingsPage,
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

const notificationSettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/notifications',
  component: lazyRouteComponent(
    () => import('@/routes/notification-settings'),
    'NotificationSettingsPage',
  ),
})

const importRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/settings/import',
  component: ImportPage,
})

const adminRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/admin',
  component: AdminPage,
})

const routeTree = rootRoute.addChildren([
  adminRoute,
  feedRoute,
  activitiesRoute,
  newActivityRoute,
  activityRoute,
  editActivityRoute,
  statsRoute,
  yearReviewRoute,
  analysisRoute,
  routesRoute,
  recordsRoute,
  recordsByYearRoute,
  profileRoute,
  achievementsRoute,
  runnerRoute,
  sharedProfileRoute,
  groupsRoute,
  groupRoute,
  groupSettingsRoute,
  clubRoute,
  joinGroupRoute,
  equipmentRoute,
  settingsRoute,
  profileSettingsRoute,
  dataSettingsRoute,
  followersRoute,
  privacyPlacesRoute,
  notificationSettingsRoute,
  importRoute,
])

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  scrollRestoration: true,
  // The activity page's back link pushes a new entry, so key the list by URL to restore it there too.
  getScrollRestorationKey: (location) =>
    location.pathname === '/app/activities'
      ? location.href
      : (location.state.__TSR_key ?? location.href),
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
