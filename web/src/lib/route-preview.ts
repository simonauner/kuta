export type RouteLine = [number, number][]

// Keep clipped sections separate: joining them would draw a line through the hidden zone.
export function routePreviewLines(geoJson: string): RouteLine[] {
  const route = JSON.parse(geoJson) as
    | { type: 'LineString'; coordinates: RouteLine }
    | { type: 'MultiLineString'; coordinates: RouteLine[] }
  return (route.type === 'LineString' ? [route.coordinates] : route.coordinates).filter(
    (line) => line.length >= 2,
  )
}
