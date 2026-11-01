// All calls go to our API with the session cookie. Writing calls require a CSRF token (X-CSRF-TOKEN),
// which is bound to the user and therefore refetched after sign-in and sign-out.

export class ApiError extends Error {
  status: number
  code: string

  constructor(status: number, message: string, code = 'generic') {
    super(message)
    this.status = status
    this.code = code
  }
}

let csrfToken: Promise<string> | null = null

export function resetCsrfToken() {
  csrfToken = null
}

function getCsrfToken() {
  csrfToken ??= request<{ token: string }>('/api/antiforgery')
    .then((r) => r.token)
    .catch((error) => {
      resetCsrfToken()
      throw error
    })
  return csrfToken
}

async function response(path: string, init?: RequestInit): Promise<Response> {
  const res = await fetch(path, init)
  if (!res.ok) {
    // The session has expired: the token belongs to the old user
    if (res.status === 401) resetCsrfToken()
    const body = await res.json().catch(() => null)
    throw new ApiError(
      res.status,
      body?.error ?? 'Något gick fel. Försök igen.',
      body?.code ?? (res.status === 429 ? 'rateLimited' : 'generic'),
    )
  }
  return res
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await response(path, init)
  return res.status === 204 ? (undefined as T) : res.json()
}

export const api = {
  download: async (path: string, filename: string) => {
    const res = await response(path)
    const url = URL.createObjectURL(await res.blob())
    const link = document.createElement('a')
    link.href = url
    link.download = filename
    document.body.append(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  },
  get: <T>(path: string) => request<T>(path),
  send: async <T = void>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown) =>
    request<T>(path, {
      method,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await getCsrfToken() },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  // File upload: the browser sets Content-Type with the multipart boundary itself
  upload: async <T>(path: string, form: FormData) =>
    request<T>(path, {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': await getCsrfToken() },
      body: form,
    }),
}
