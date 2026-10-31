import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import uPlot from 'uplot'
import 'uplot/dist/uPlot.min.css'
import { formatDuration, type ChartAxis } from '@/lib/activity'

export type ChartSeries = {
  label: string
  values: (number | null)[]
  color: string
  // Tempo: lägre värde (snabbare) uppåt, och etiketter som mm:ss
  pace?: boolean
  fill?: boolean
}

// Ett diagram per kanal med gemensam x-axel och synkad markör.
export function ActivityCharts({ axis, series }: { axis: ChartAxis; series: ChartSeries[] }) {
  const container = useRef<HTMLDivElement>(null)
  const { t } = useTranslation()

  useEffect(() => {
    const element = container.current
    if (!element) return
    const sync = uPlot.sync('activity')
    const width = element.clientWidth
    const axisColor = getComputedStyle(element).color
    const charts = series.map((s) => {
      const chart = new uPlot(
        {
          width,
          height: 140,
          legend: { show: false },
          cursor: { sync: { key: sync.key }, drag: { x: false, y: false } },
          scales: { x: { time: false }, y: s.pace ? { dir: -1 } : {} },
          axes: [
            {
              label: axis.elapsedTime ? t('activity.chartTime') : undefined,
              values: (_, ticks) =>
                ticks.map((v) => (axis.elapsedTime ? formatDuration(Math.round(v)) : `${v} km`)),
              stroke: axisColor,
            },
            {
              label: s.label,
              stroke: axisColor,
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
        [axis.values, s.values],
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
  }, [axis, series, t])

  return <div ref={container} className="space-y-2 text-xs text-muted-foreground" />
}
