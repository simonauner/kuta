import type { TFunction } from 'i18next'
import sv from '../locales/sv.json' with { type: 'json' }
import { knownSport, sports } from './sports.ts'

// From /api/activities/sport-groups: sports counted together in goals and statistics, e.g. running with
// trail and virtual running. Sports without a group are their own groups with the sport's key.
export type SportGroup = { key: string; sports: string[] }
type NamedSportGroup = keyof typeof sv.sportGroups

// Groups with several sports have their own names; other groups are named after their sport.
export function isNamedSportGroup(key: string): key is NamedSportGroup {
  return Object.hasOwn(sv.sportGroups, key)
}

export function sportGroupLabel(key: string, t: TFunction) {
  return isNamedSportGroup(key)
    ? t(`sportGroups.${key}`)
    : t(`newActivity.sports.${knownSport(key)}`)
}

// Sport names the web doesn't know (Garmin's raw names) are left out.
export function sportGroupMembers(group: SportGroup, t: TFunction) {
  return group.sports
    .filter((sport) => (sports as readonly string[]).includes(sport))
    .map((sport) => t(`newActivity.sports.${knownSport(sport)}`))
}
