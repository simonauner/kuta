public static class RoutePrivacy
{
    // Same distance as the hidden start/finish zones on map previews.
    public const double HiddenEndpointM = 600;

    // Height near the start and finish can be matched against an elevation model to find where a shared activity
    // began. For other viewers, altitude within HiddenEndpointM of either end is removed; without distance, all of it.
    public static ActivityStreams HideEndpointAltitude(ActivityStreams streams)
    {
        if (streams.Alt is null) return streams;
        if (streams.Dist is null) return streams with { Alt = null };
        var total = streams.Dist.Max() ?? 0;
        var alt = streams.Alt.Select((value, i) =>
            i < streams.Dist.Length && streams.Dist[i] is { } d && d > HiddenEndpointM && d < total - HiddenEndpointM
                ? value : null).ToArray();
        return streams with { Alt = alt.Any(v => v is not null) ? alt : null };
    }

    // Projection only: callers must authorize the viewer separately before returning any activity data.
    public static ActivityStreams? ForViewer(ActivityStreams? streams, Guid ownerId, Guid viewerId, IReadOnlyList<PrivacyPlace> places)
    {
        if (streams is null || ownerId == viewerId || places.Count == 0) return streams;
        var lat = new double?[streams.T.Length];
        var lon = new double?[streams.T.Length];
        bool Valid(int i) => streams.Lat is not null && streams.Lon is not null
            && i < streams.Lat.Length && i < streams.Lon.Length
            && streams.Lat[i] is >= -90 and <= 90 && streams.Lon[i] is >= -180 and <= 180;
        var points = Enumerable.Range(0, streams.T.Length).Where(Valid).ToArray();
        if (points.Length == 0) return streams with { Lat = lat, Lon = lon };

        double Distance(int a, double latitude, double longitude)
        {
            const double radians = Math.PI / 180;
            var aLat = streams.Lat![a]!.Value * radians;
            var bLat = latitude * radians;
            var dLat = (bLat - aLat) / 2;
            var dLon = (longitude - streams.Lon![a]!.Value) * radians / 2;
            var h = Math.Pow(Math.Sin(dLat), 2) + Math.Cos(aLat) * Math.Cos(bLat) * Math.Pow(Math.Sin(dLon), 2);
            return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
        }

        var hidden = new bool[streams.T.Length];
        foreach (var place in places)
        {
            var distances = points.Select(i => Distance(i, place.Latitude, place.Longitude)).ToArray();
            for (var p = 0; p < points.Length; p++)
            {
                var i = points[p];
                if (distances[p] <= place.RadiusM) hidden[i] = true;
                // Conservatively break sparse segments that could cross a zone, even with both ends outside.
                if (p > 0 && points[p - 1] == i - 1
                    && distances[p - 1] > place.RadiusM && distances[p] > place.RadiusM
                    && distances[p - 1] + distances[p] <= Distance(points[p - 1], streams.Lat![i]!.Value, streams.Lon![i]!.Value) + 2 * place.RadiusM)
                    hidden[points[p - 1]] = hidden[i] = true;
            }
        }
        foreach (var i in points)
        {
            if (hidden[i]) continue;
            lat[i] = streams.Lat![i];
            lon[i] = streams.Lon![i];
        }
        return streams with { Lat = lat, Lon = lon };
    }
}
