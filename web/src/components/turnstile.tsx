import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

// Cloudflare Turnstile for the sign-up and password forms, which send email. The API decides whether the check is
// on: without a site key no widget is shown and nothing is loaded from Cloudflare.
const origin = 'https://challenges.cloudflare.com'

type TurnstileApi = {
  render: (
    element: HTMLElement,
    options: {
      sitekey: string
      language: string
      callback: (token: string) => void
      'expired-callback': () => void
      'error-callback': () => void
    },
  ) => string
  remove: (widgetId: string) => void
}

declare global {
  interface Window {
    turnstile?: TurnstileApi
  }
}

let script: Promise<TurnstileApi> | null = null

function loadScript() {
  script ??= new Promise<TurnstileApi>((resolve, reject) => {
    const element = document.createElement('script')
    element.src = `${origin}/turnstile/v0/api.js?render=explicit`
    element.async = true
    element.onload = () => (window.turnstile ? resolve(window.turnstile) : reject(new Error()))
    element.onerror = () => {
      script = null
      element.remove()
      reject(new Error())
    }
    document.head.append(element)
  })
  return script
}

// A token is single-use: remount the widget (change its key) after each submit that fails.
export function Turnstile({
  siteKey,
  onToken,
}: {
  siteKey: string
  onToken: (token: string | null) => void
}) {
  const { t, i18n } = useTranslation()
  const container = useRef<HTMLDivElement>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let widgetId: string | null = null
    let active = true
    loadScript().then(
      (turnstile) => {
        if (!active || !container.current) return
        widgetId = turnstile.render(container.current, {
          sitekey: siteKey,
          language: i18n.language,
          callback: (token) => onToken(token),
          'expired-callback': () => onToken(null),
          'error-callback': () => onToken(null),
        })
      },
      () => active && setFailed(true),
    )
    return () => {
      active = false
      onToken(null)
      if (widgetId) window.turnstile?.remove(widgetId)
    }
  }, [siteKey, i18n.language, onToken])

  return (
    <div>
      <div ref={container} className="min-h-[65px]" />
      {failed && (
        <p role="alert" className="text-sm text-destructive">
          {t('errors.turnstileUnavailable')}
        </p>
      )}
    </div>
  )
}
