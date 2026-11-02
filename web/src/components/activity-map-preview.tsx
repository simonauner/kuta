import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { routePreviewLines } from '@/lib/route-preview'

const ActivityMap = lazy(() =>
  import('@/components/activity-map').then((m) => ({ default: m.ActivityMap })),
)

export function ActivityMapPreview({ geoJson }: { geoJson: string }) {
  const { t } = useTranslation()
  const container = useRef<HTMLDivElement>(null)
  const [visible, setVisible] = useState(false)
  const lines = useMemo(() => routePreviewLines(geoJson), [geoJson])
  const coordinates = useMemo(() => lines.flat(), [lines])
  const routes = useMemo(
    () => lines.map((coordinates) => ({ coordinates, color: '#e8590c' })),
    [lines],
  )

  useEffect(() => {
    const observer = new IntersectionObserver(([entry]) => setVisible(entry.isIntersecting), {
      rootMargin: '200px',
    })
    if (container.current) observer.observe(container.current)
    return () => observer.disconnect()
  }, [])

  if (lines.length === 0) return null
  return (
    // Edge to edge in the feed card: the negative margins cancel the card's padding, the card clips the corners
    <div ref={container} className="-mx-4 -mb-4 mt-4 h-60 bg-muted sm:-mx-5 sm:-mb-5 sm:h-72">
      {visible && (
        <Suspense fallback={null}>
          <ActivityMap
            coordinates={coordinates}
            routes={routes}
            label={t('activity.map')}
            thumbnail
          />
        </Suspense>
      )}
    </div>
  )
}
