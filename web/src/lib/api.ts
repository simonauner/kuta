// Alla anrop går till vårt API med sessionscookien. Skrivande anrop kräver CSRF-token (X-CSRF-TOKEN),
// som är knuten till användaren och därför hämtas om efter in- och utloggning.

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

let csrfToken: Promise<string> | null = null

export function resetCsrfToken() {
  csrfToken = null
}

function getCsrfToken() {
  csrfToken ??= request<{ token: string }>('/api/antiforgery').then((r) => r.token)
  return csrfToken
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, init)
  if (!res.ok) {
    // Sessionen har gått ut: token hör till den gamla användaren
    if (res.status === 401) resetCsrfToken()
    const body = await res.json().catch(() => null)
    throw new ApiError(res.status, body?.error ?? 'Något gick fel. Försök igen.')
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  send: async <T = void>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown) =>
    request<T>(path, {
      method,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await getCsrfToken() },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
}
