import type { TFunction } from 'i18next'
import sv from '../locales/sv.json' with { type: 'json' }
import { knownSport, sports } from './sports.ts'

// Från /api/activities/sport-groups: sporter som räknas tillsammans i mål och statistik, t.ex. löpning med
// terräng- och virtuell löpning. Sporter utan grupp är egna grupper med sportens nyckel.
export type SportGroup = { key: string; sports: string[] }
type NamedSportGroup = keyof typeof sv.sportGroups

// Grupper med flera sporter har egna namn; övriga grupper heter som sin sport.
export function isNamedSportGroup(key: string): key is NamedSportGroup {
  return Object.hasOwn(sv.sportGroups, key)
}

export function sportGroupLabel(key: string, t: TFunction) {
  return isNamedSportGroup(key)
    ? t(`sportGroups.${key}`)
    : t(`newActivity.sports.${knownSport(key)}`)
}

// Sportnamn som webben inte känner till (Garmins råa namn) utelämnas.
export function sportGroupMembers(group: SportGroup, t: TFunction) {
  return group.sports
    .filter((sport) => (sports as readonly string[]).includes(sport))
    .map((sport) => t(`newActivity.sports.${knownSport(sport)}`))
}
