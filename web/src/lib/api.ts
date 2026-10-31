// Alla anrop går till vårt API med sessionscookien. Skrivande anrop kräver CSRF-token (X-CSRF-TOKEN),
// som är knuten till användaren och därför hämtas om efter in- och utloggning.

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
    // Sessionen har gått ut: token hör till den gamla användaren
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
  uploadStream: async <T>(path: string, form: FormData, onMessage: (message: T) => void) => {
    const res = await response(path, {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': await getCsrfToken() },
      body: form,
    })
    if (!res.body) throw new ApiError(502, 'Missing import stream')
    const reader = res.body.pipeThrough(new TextDecoderStream()).getReader()
    let buffer = ''
    try {
      while (true) {
        const { value, done } = await reader.read()
        if (done) break
        buffer += value
        let newline: number
        while ((newline = buffer.indexOf('\n')) !== -1) {
          onMessage(JSON.parse(buffer.slice(0, newline)) as T)
          buffer = buffer.slice(newline + 1)
        }
      }
      if (buffer.length) throw new ApiError(502, 'Incomplete import stream')
    } finally {
      await reader.cancel()
      reader.releaseLock()
    }
  },
  get: <T>(path: string) => request<T>(path),
  send: async <T = void>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown) =>
    request<T>(path, {
      method,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await getCsrfToken() },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  // Filuppladdning: webbläsaren sätter själv Content-Type med multipart-gränsen
  upload: async <T>(path: string, form: FormData) =>
    request<T>(path, {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': await getCsrfToken() },
      body: form,
    }),
}
