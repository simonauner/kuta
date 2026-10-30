import assert from 'node:assert/strict'
import { afterEach, mock, test } from 'node:test'
import { api, ApiError, resetCsrfToken } from '../src/lib/api.ts'

afterEach(() => {
  mock.restoreAll()
  resetCsrfToken()
})

test('concurrent writes share a token and send JSON with the CSRF header', async () => {
  const fetch = mock.method(globalThis, 'fetch', async (path, init) => {
    if (path === '/api/antiforgery') return Response.json({ token: 'shared-token' })
    assert.equal(init.headers['X-CSRF-TOKEN'], 'shared-token')
    assert.equal(init.headers['Content-Type'], 'application/json')
    assert.equal(init.method, 'PUT')
    assert.deepEqual(JSON.parse(init.body), { displayName: 'Testlöpare' })
    return new Response(null, { status: 204 })
  })

  const results = await Promise.all([
    api.send('PUT', '/api/me', { displayName: 'Testlöpare' }),
    api.send('PUT', '/api/me', { displayName: 'Testlöpare' }),
  ])

  assert.deepEqual(results, [undefined, undefined])
  assert.equal(
    fetch.mock.calls.filter(({ arguments: [path] }) => path === '/api/antiforgery').length,
    1,
  )
})

test('a write can be retried after the token fetch loses its connection', async () => {
  const fetch = mock.method(globalThis, 'fetch', async () => {
    throw new TypeError('offline')
  })
  await assert.rejects(api.send('POST', '/api/auth/login'), /offline/)

  fetch.mock.mockImplementation(async (path) =>
    path === '/api/antiforgery'
      ? Response.json({ token: 'recovered-token' })
      : new Response(null, { status: 204 }),
  )

  await api.send('POST', '/api/auth/login')
  assert.equal(fetch.mock.callCount(), 3)
})

test('a failed HTTP token response does not poison later attempts', async () => {
  const fetch = mock.method(globalThis, 'fetch', async () =>
    Response.json({ error: 'Tillfälligt fel' }, { status: 503 }),
  )
  await assert.rejects(api.send('POST', '/api/auth/login'), {
    name: 'Error',
    status: 503,
    message: 'Tillfälligt fel',
  })

  fetch.mock.mockImplementation(async (path) =>
    path === '/api/antiforgery'
      ? Response.json({ token: 'recovered-token' })
      : new Response(null, { status: 204 }),
  )

  await api.send('POST', '/api/auth/login')
  assert.equal(fetch.mock.callCount(), 3)
})

test('a session change forces a fresh token for the next write', async () => {
  let tokens = 0
  mock.method(globalThis, 'fetch', async (path, init) => {
    if (path === '/api/antiforgery') return Response.json({ token: `token-${++tokens}` })
    assert.equal(init.headers['X-CSRF-TOKEN'], `token-${tokens}`)
    return new Response(null, { status: 204 })
  })

  await api.send('POST', '/api/auth/login')
  resetCsrfToken()
  await api.send('POST', '/api/auth/logout')
  assert.equal(tokens, 2)
})

test('an unauthorized response discards the previous session token', async () => {
  let tokens = 0
  mock.method(globalThis, 'fetch', async (path, init) => {
    if (path === '/api/antiforgery') return Response.json({ token: `token-${++tokens}` })
    assert.equal(init.headers['X-CSRF-TOKEN'], `token-${tokens}`)
    return tokens === 1
      ? Response.json({ error: 'Logga in igen' }, { status: 401 })
      : new Response(null, { status: 204 })
  })

  await assert.rejects(
    api.send('POST', '/api/auth/logout'),
    (error) =>
      error instanceof ApiError && error.status === 401 && error.message === 'Logga in igen',
  )
  await api.send('POST', '/api/auth/login')
  assert.equal(tokens, 2)
})

test('GET reads JSON without requesting a CSRF token', async () => {
  const fetch = mock.method(globalThis, 'fetch', async (path) => {
    assert.equal(path, '/api/me')
    return Response.json({ displayName: 'Testlöpare' })
  })

  assert.deepEqual(await api.get('/api/me'), { displayName: 'Testlöpare' })
  assert.equal(fetch.mock.callCount(), 1)
})

test('a non-JSON error response has a readable fallback message', async () => {
  mock.method(globalThis, 'fetch', async () => new Response('Bad gateway', { status: 502 }))

  await assert.rejects(
    api.get('/api/me'),
    (error) =>
      error instanceof ApiError &&
      error.status === 502 &&
      error.message === 'Något gick fel. Försök igen.',
  )
})
