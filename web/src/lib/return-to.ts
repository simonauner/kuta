// A shared link opened without an account: signing up goes through the confirmation email, which always lands on
// /app, so the link is remembered in this browser and opened after the first sign-in.
const key = 'kuta.returnTo'
const maxAgeMs = 24 * 60 * 60 * 1000

type Storage = Pick<globalThis.Storage, 'getItem' | 'setItem' | 'removeItem'>

export function rememberReturnTo(path: string, storage: Storage = localStorage, now = Date.now()) {
  try {
    storage.setItem(key, JSON.stringify({ path, at: now }))
  } catch {
    // Blocked storage only loses the convenience.
  }
}

// The remembered path once, if it is recent and inside the app.
export function takeReturnTo(storage: Storage = localStorage, now = Date.now()): string | null {
  try {
    const raw = storage.getItem(key)
    storage.removeItem(key)
    const { path, at } = JSON.parse(raw ?? 'null') ?? {}
    return typeof path === 'string' &&
      path.startsWith('/app/') &&
      !path.startsWith('//') &&
      typeof at === 'number' &&
      now - at < maxAgeMs
      ? path
      : null
  } catch {
    return null
  }
}
