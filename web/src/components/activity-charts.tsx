import { useEffect, useRef } from 'react'
import uPlot from 'uplot'
import 'uplot/dist/uPlot.min.css'
import { formatDuration } from '@/lib/activity'

export type ChartSeries = {
  label: string
  values: (number | null)[]
  color: string
  // Tempo: lägre värde (snabbare) uppåt, och etiketter som mm:ss
  pace?: boolean
  fill?: boolean
}

// uPlot (canvas) klarar tiotusentals mätpunkter; ett diagram per kanal med gemensam x-axel (km) och synkad markör.
export function ActivityCharts({
  distanceKm,
  series,
}: {
  distanceKm: number[]
  series: ChartSeries[]
}) {
  const container = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const element = container.current
    if (!element) return
    const sync = uPlot.sync('activity')
    const width = element.clientWidth
    const charts = series.map((s) => {
      const chart = new uPlot(
        {
          width,
          height: 140,
          legend: { show: false },
          cursor: { sync: { key: sync.key }, drag: { x: false, y: false } },
          scales: { x: { time: false }, y: s.pace ? { dir: -1 } : {} },
          axes: [
            { values: (_, ticks) => ticks.map((v) => `${v} km`), stroke: 'currentColor' },
            {
              label: s.label,
              stroke: 'currentColor',
              size: 64,
              values: s.pace ? (_, ticks) => ticks.map((v) => formatDuration(v)) : undefined,
            },
          ],
          series: [
            {},
            {
              label: s.label,
              stroke: s.color,
              width: 1.5,
              fill: s.fill ? `${s.color}33` : undefined,
              spanGaps: true,
            },
          ],
        },
        [distanceKm, s.values],
        element,
      )
      return chart
    })

    const resize = new ResizeObserver(() =>
      charts.forEach((c) => c.setSize({ width: element.clientWidth, height: 140 })),
    )
    resize.observe(element)
    return () => {
      resize.disconnect()
      charts.forEach((c) => c.destroy())
    }
  }, [distanceKm, series])

  return <div ref={container} className="space-y-2 text-xs text-muted-foreground" />
}
