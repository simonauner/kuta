export function detectLanguage(
  saved: string | null,
  languages: readonly string[],
  isSwedishIp = false,
): 'sv' | 'en' {
  if (saved === 'sv' || saved === 'en') return saved
  if (isSwedishIp) return 'sv'
  for (const language of languages) {
    const base = language.toLowerCase().split('-')[0]
    if (base === 'sv' || base === 'en') return base
  }
  return 'en'
}
