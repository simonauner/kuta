import { detectLanguage } from './lib/language'

// The header flag links to the same page in the other language; following it is an explicit choice,
// saved under the app's key so the app opens in that language too.
const other = document.querySelector<HTMLAnchorElement>('.language-link')!
other.addEventListener('click', () => {
  try {
    localStorage.setItem('kuta.language', other.hreflang)
  } catch {
    /* Storage can be blocked; the link still works. */
  }
})

let saved: string | null = null
try {
  saved = localStorage.getItem('kuta.language')
} catch {
  /* Storage can be blocked. */
}

// shortcut: user-agent match for crawlers, so each language version stays indexable; upgrade if search traffic shows misses.
const crawler = /bot|crawl|spider|slurp/i.test(navigator.userAgent)

if (!crawler) {
  let isSwedishIp = false
  // A Swedish IP and a Swedish browser give the same answer, so the lookup is only needed otherwise.
  if (saved === null && detectLanguage(null, navigator.languages) !== 'sv') {
    try {
      const response = await fetch('/api/locale', {
        cache: 'no-store',
        signal: AbortSignal.timeout(1500),
      })
      if (response.ok) isSwedishIp = (await response.json()).isSwedishIp === true
    } catch {
      /* Offline or slow API: fall back to the browser language. */
    }
  }
  if (detectLanguage(saved, navigator.languages, isSwedishIp) !== document.documentElement.lang)
    location.replace(other.pathname + location.search + location.hash)
}
