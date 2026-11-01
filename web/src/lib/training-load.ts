import { parseDuration } from './activity.ts'

export type TrainingLoadDay = {
  day: string
  load: number
  fitness: number
  fatigue: number
  form: number
}

export type TrainingLoadSummary = {
  thresholdSpeedMS: number | null
  thresholdSource: 'manual' | 'prediction' | null
  predictedThresholdSpeedMS: number | null
  days: TrainingLoadDay[]
  rampPerWeek: number | null
  warning: boolean
}

export const trainingLoadQueryKey = (timeZone: string) => ['activities', 'training-load', timeZone]

// Same limits as the API: 1:40–60:00 per km.
const fastest = 100
const slowest = 3600

export function paceSeconds(speedMS: number) {
  return Math.round(1000 / speedMS)
}

// "4:30" → m/s, null if the text isn't a pace within the limits.
export function parseThresholdPace(text: string) {
  const seconds = text.includes(':') ? parseDuration(text) : null
  if (seconds === null || seconds < fastest || seconds > slowest) return null
  return 1000 / seconds
}

// Chart: fitness and fatigue as lines and daily load as bars, with a shared scale from 0.
export function loadChart(
  days: TrainingLoadDay[],
  { width, height, padding }: { width: number; height: number; padding: number },
) {
  if (days.length === 0) return null
  const maximum = Math.max(1, ...days.flatMap((d) => [d.load, d.fitness, d.fatigue]))
  const step = (width - 2 * padding) / days.length
  const x = (index: number) => padding + (index + 0.5) * step
  const y = (value: number) => height - padding - (value / maximum) * (height - 2 * padding)
  const line = (key: 'fitness' | 'fatigue') => days.map((d, i) => `${x(i)},${y(d[key])}`).join(' ')
  return {
    maximum,
    fitness: line('fitness'),
    fatigue: line('fatigue'),
    bars: days.map((d, i) => ({
      x: x(i) - step * 0.35,
      width: step * 0.7,
      y: y(d.load),
      height: y(0) - y(d.load),
      day: d.day,
      load: d.load,
    })),
    baseline: y(0),
  }
}
