import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from '../locales/en.json'
import sv from '../locales/sv.json'
import { detectLanguage } from './language'

export async function initializeI18n() {
  let savedLanguage: string | null = null
  try {
    savedLanguage = localStorage.getItem('kuta.language')
  } catch {
    /* Storage can be blocked. */
  }

  let isSwedishIp = false
  if (savedLanguage !== 'sv' && savedLanguage !== 'en') {
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

  i18n.on('languageChanged', (language) => {
    document.documentElement.lang = language
  })
  await i18n.use(initReactI18next).init({
    resources: { en: { translation: en }, sv: { translation: sv } },
    lng: detectLanguage(savedLanguage, navigator.languages, isSwedishIp),
    supportedLngs: ['sv', 'en'],
    fallbackLng: 'en',
    initAsync: false,
    interpolation: { escapeValue: false }, // React escapes translated text.
  })

  // Persist explicit choices only, so automatic detection can follow a changed IP or browser language.
  i18n.on('languageChanged', (language) => {
    try {
      localStorage.setItem('kuta.language', language)
    } catch {
      /* Keep the in-memory choice. */
    }
  })
}

export default i18n

declare module 'i18next' {
  interface CustomTypeOptions {
    resources: { translation: typeof en }
  }
}
