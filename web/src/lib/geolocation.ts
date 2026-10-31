type LocationErrorCode =
  'locationDenied' | 'locationUnavailable' | 'locationTimeout' | 'locationUnsupported'

export class LocationError extends Error {
  code: LocationErrorCode
  constructor(code: LocationErrorCode) {
    super(code)
    this.code = code
  }
}

export function currentLocation(geolocation: Pick<Geolocation, 'getCurrentPosition'> | undefined) {
  return new Promise<{ latitude: number; longitude: number }>((resolve, reject) => {
    if (!geolocation) {
      reject(new LocationError('locationUnsupported'))
      return
    }
    try {
      geolocation.getCurrentPosition(
        ({ coords }) => {
          const { latitude, longitude } = coords
          if (
            !Number.isFinite(latitude) ||
            Math.abs(latitude) > 90 ||
            !Number.isFinite(longitude) ||
            Math.abs(longitude) > 180
          ) {
            reject(new LocationError('locationUnavailable'))
            return
          }
          resolve({ latitude, longitude })
        },
        (error) => {
          reject(
            new LocationError(
              error.code === 1
                ? 'locationDenied'
                : error.code === 3
                  ? 'locationTimeout'
                  : 'locationUnavailable',
            ),
          )
        },
        { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 },
      )
    } catch {
      reject(new LocationError('locationUnavailable'))
    }
  })
}
