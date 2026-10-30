using Dynastream.Fit;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

// Ett pass ur en FIT-fil: sammanfattning från session-meddelandet, tidsserier (en array per kanal) från records.
// En kanal som saknas helt blir null, inte en array full av null.
public sealed record ParsedActivity(
    string Sport, System.DateTime StartedAt, int DurationS, int? MovingS, double DistanceM,
    short? AvgHr, short? MaxHr, float? AvgSpeedMps, float? ElevationGainM, LineString? Route,
    int[] T, float?[]? Dist, short?[]? Hr, float?[]? Speed, float?[]? Alt, double?[]? Lat, double?[]? Lon)
{
    // Utan samtycke sparas inte puls (hälsodata), varken sammanfattning eller tidsserie.
    public ParsedActivity WithoutHeartRate() => this with { AvgHr = null, MaxHr = null, Hr = null };
}

public static class FitImport
{
    private const double DegreesPerSemicircle = 180.0 / 2147483648.0;
    // Ungefär 5 meter: nog för att rita rutten, och mycket färre punkter än en per sekund
    private const double RouteToleranceDegrees = 0.00005;
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    // Null när filen inte är en FIT-fil med ett pass (session) i.
    // shortcut: tar första sessionen; multisport-filer (flera sessioner) blir ett pass, dela upp dem när det behövs.
    public static ParsedActivity? Parse(byte[] file)
    {
        using var stream = new MemoryStream(file);
        var decoder = new Decode();
        if (!decoder.IsFIT(stream) || !decoder.CheckIntegrity(stream)) return null;

        var broadcaster = new MesgBroadcaster();
        var sessions = new List<SessionMesg>();
        var records = new List<RecordMesg>();
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;
        broadcaster.SessionMesgEvent += (_, e) => sessions.Add((SessionMesg)e.mesg);
        broadcaster.RecordMesgEvent += (_, e) => records.Add((RecordMesg)e.mesg);
        stream.Position = 0;
        if (!decoder.Read(stream) || sessions.Count == 0) return null;

        var session = sessions[0];
        var start = session.GetStartTime()?.GetDateTime() ?? records.FirstOrDefault()?.GetTimestamp()?.GetDateTime();
        if (start is not { } startedAt) return null;

        records = records.Where(r => r.GetTimestamp() is not null).ToList();
        var lat = Channel(records, r => r.GetPositionLat() is { } v ? v * DegreesPerSemicircle : (double?)null);
        var lon = Channel(records, r => r.GetPositionLong() is { } v ? v * DegreesPerSemicircle : (double?)null);
        var dist = Channel(records, r => r.GetDistance());

        return new ParsedActivity(
            Sport: session.GetSport()?.ToString().ToLowerInvariant() ?? "running",
            StartedAt: System.DateTime.SpecifyKind(startedAt, DateTimeKind.Utc),
            DurationS: (int)Math.Round(session.GetTotalElapsedTime() ?? 0),
            MovingS: session.GetTotalTimerTime() is { } timer ? (int)Math.Round(timer) : null,
            DistanceM: session.GetTotalDistance() ?? dist?.LastOrDefault(d => d is not null) ?? 0,
            AvgHr: session.GetAvgHeartRate(),
            MaxHr: session.GetMaxHeartRate(),
            AvgSpeedMps: session.GetEnhancedAvgSpeed() ?? session.GetAvgSpeed(),
            ElevationGainM: session.GetTotalAscent(),
            Route: Route(lat, lon),
            T: records.Select(r => (int)Math.Round((r.GetTimestamp()!.GetDateTime() - startedAt).TotalSeconds)).ToArray(),
            Dist: dist,
            Hr: Channel(records, r => (short?)r.GetHeartRate()),
            Speed: Channel(records, r => r.GetEnhancedSpeed() ?? r.GetSpeed()),
            Alt: Channel(records, r => r.GetEnhancedAltitude() ?? r.GetAltitude()),
            Lat: lat,
            Lon: lon);
    }

    private static T?[]? Channel<T>(List<RecordMesg> records, Func<RecordMesg, T?> read) where T : struct
    {
        var values = records.Select(read).ToArray();
        return values.Any(v => v is not null) ? values : null;
    }

    public static LineString? Route(double?[]? lat, double?[]? lon)
    {
        if (lat is null || lon is null) return null;
        var points = lat.Zip(lon)
            .Where(p => p.First is not null && p.Second is not null)
            .Select(p => new Coordinate(p.Second!.Value, p.First!.Value))
            .ToArray();
        if (points.Length < 2) return null;
        var simplified = DouglasPeuckerSimplifier.Simplify(Wgs84.CreateLineString(points), RouteToleranceDegrees);
        return simplified as LineString;
    }
}
