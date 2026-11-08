// Who may see an activity, a goal or the supporter badge, from narrowest to widest.
export const privacies = ['private', 'followers', 'public'] as const
export type Privacy = (typeof privacies)[number]
