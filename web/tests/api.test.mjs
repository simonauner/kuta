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

test('streamed upload reports messages before completion across byte and line boundaries', async () => {
  let controller
  const stream = new ReadableStream({
    start(value) {
      controller = value
    },
  })
  mock.method(globalThis, 'fetch', async (path, init) => {
    if (path === '/api/antiforgery') return Response.json({ token: 'stream-token' })
    assert.equal(init.method, 'POST')
    assert.equal(init.headers['X-CSRF-TOKEN'], 'stream-token')
    assert.ok(init.body instanceof FormData)
    return new Response(stream)
  })
  const messages = []
  let report
  const reported = new Promise((resolve) => {
    report = resolve
  })
  const upload = api.uploadStream('/import', new FormData(), (message) => {
    messages.push(message)
    report()
  })
  const bytes = new TextEncoder().encode('{"name":"löpare"}\n{"processed":1}\n{"processed":2}\n')
  const split = bytes.indexOf(0xc3) + 1
  controller.enqueue(bytes.slice(0, split))
  controller.enqueue(bytes.slice(split, bytes.indexOf(10) + 1))
  await reported
  assert.deepEqual(messages, [{ name: 'löpare' }])
  controller.enqueue(bytes.slice(bytes.indexOf(10) + 1))
  controller.close()
  await upload
  assert.deepEqual(messages, [{ name: 'löpare' }, { processed: 1 }, { processed: 2 }])
})

test('streamed upload rejects truncated responses and preserves HTTP validation errors', async () => {
  const fetch = mock.method(globalThis, 'fetch', async (path) =>
    path === '/api/antiforgery' ? Response.json({ token: 'token' }) : new Response('{"processed":'),
  )
  await assert.rejects(
    api.uploadStream('/import', new FormData(), () => {}),
    { status: 502 },
  )
  fetch.mock.mockImplementation(async () =>
    Response.json({ error: 'Invalid ZIP', code: 'invalidStravaArchive' }, { status: 400 }),
  )
  await assert.rejects(
    api.uploadStream('/import', new FormData(), () => {}),
    { status: 400, code: 'invalidStravaArchive' },
  )
})
