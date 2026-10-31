export function scheduleSlowLoading(
  loading: boolean,
  shownAt: number | null,
  update: (shownAt: number | null) => void,
) {
  if (loading && shownAt === null) {
    const timer = setTimeout(() => update(Date.now()), 2000)
    return () => clearTimeout(timer)
  }
  if (!loading && shownAt !== null) {
    const timer = setTimeout(() => update(null), Math.max(0, 500 - (Date.now() - shownAt)))
    return () => clearTimeout(timer)
  }
}
