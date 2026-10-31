export function privacyCircle(latitude: number, longitude: number, radiusM: number) {
  const radians = Math.PI / 180
  const lat = latitude * radians
  const lon = longitude * radians
  const distance = radiusM / 6371000
  const points = Array.from({ length: 64 }, (_, i): [number, number] => {
    const bearing = (i / 64) * 2 * Math.PI
    const pointLat = Math.asin(
      Math.sin(lat) * Math.cos(distance) + Math.cos(lat) * Math.sin(distance) * Math.cos(bearing),
    )
    const pointLon =
      lon +
      Math.atan2(
        Math.sin(bearing) * Math.sin(distance) * Math.cos(lat),
        Math.cos(distance) - Math.sin(lat) * Math.sin(pointLat),
      )
    return [pointLon / radians, pointLat / radians]
  })
  return [...points, points[0]]
}
