// Push notifications, imported by the service worker that vite-plugin-pwa generates (workbox.importScripts).
self.addEventListener('push', (event) => {
  const message = event.data?.json() ?? {}
  event.waitUntil(
    self.registration.showNotification(message.title ?? 'Kuta', {
      body: message.body,
      tag: message.tag,
      icon: '/icons/icon-192.png',
      data: { url: message.url },
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  // Only paths in the app, never another origin.
  const url = new URL(event.notification.data?.url ?? '/app', self.location.origin)
  if (url.origin !== self.location.origin || !url.pathname.startsWith('/app')) return
  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window' })
      const open = windows.find((client) => new URL(client.url).origin === self.location.origin)
      if (!open) return self.clients.openWindow(url.href)
      await open.focus()
      return open.navigate(url.href)
    })(),
  )
})
