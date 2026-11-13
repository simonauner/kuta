const steps = [5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200]

// Round gridline values (seconds) covering fastest..slowest with at most five lines; lo and hi are the axis ends.
export function durationTicks(fastest: number, slowest: number) {
  const step = steps.find((s) => (slowest - fastest) / s <= 3) ?? steps[steps.length - 1]
  const lo = Math.floor(fastest / step) * step
  const hi = Math.max(Math.ceil(slowest / step) * step, lo + step)
  const ticks: number[] = []
  for (let v = lo; v <= hi; v += step) ticks.push(v)
  return { lo, hi, ticks }
}
