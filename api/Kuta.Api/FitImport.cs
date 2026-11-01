using Dynastream.Fit;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

// An activity from a FIT file: summary from the session message, streams (one array per channel) from records.
// A channel that is missing entirely becomes null, not an array full of null.
// New fields go last with a default value: the Strava import (GPX) constructs the type positionally.
public sealed record ParsedActivity(
    string Sport, System.DateTime StartedAt, int DurationS, int? MovingS, double DistanceM,
    short? AvgHr, short? MaxHr, float? AvgSpeedMps, float? ElevationGainM, LineString? Route,
    int[] T, float?[]? Dist, short?[]? Hr, float?[]? Speed, float?[]? Alt, double?[]? Lat, double?[]? Lon,
    string? Device = null, short?[]? Cadence = null, short?[]? Temp = null, ParsedLap[]? Laps = null, ActivityPart[]? Parts = null)
{
    // Without consent no heart rate (health data) is stored: neither summary, stream nor lap heart rate.
    public ParsedActivity WithoutHeartRate() => this with
    {
        AvgHr = null, MaxHr = null, Hr = null, Laps = Laps?.Select(l => l with { AvgHr = null, MaxHr = null }).ToArray(),
    };
}

public sealed record ParsedLap(
    int StartS, int DurationS, int? MovingS, double DistanceM, short? AvgHr, short? MaxHr,
    float? AscentM, float? DescentM, short? AvgCadence);

public static class FitImport
{
    private const double DegreesPerSemicircle = 180.0 / 2147483648.0;
    // About 5 meters: enough to draw the route, and far fewer points than one per second
    private const double RouteToleranceDegrees = 0.00005;
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);
    // Just under 6 days at one point per second. The cap protects memory: a sample can take only a few bytes in the file.
    public const int MaxRecords = 500_000;
    private const int MaxMessages = 10_000;

    // Only the values we use from a record; the SDK's message objects take several hundred bytes each.
    private readonly record struct Sample(System.DateTime? Time, int? Lat, int? Lon, float? Dist, byte? Hr, float? Speed,
        float? Alt, byte? Cadence, float? FractionalCadence, sbyte? Temp)
    {
        public static Sample From(RecordMesg r) => new(r.GetTimestamp()?.GetDateTime(), r.GetPositionLat(), r.GetPositionLong(),
            r.GetDistance(), r.GetHeartRate(), r.GetEnhancedSpeed() ?? r.GetSpeed(), r.GetEnhancedAltitude() ?? r.GetAltitude(),
            r.GetCadence(), r.GetFractionalCadence(), r.GetTemperature());
    }

    private sealed class TooLargeException : Exception;

    // Null when the file is not a FIT file containing an activity (session).
    public static ParsedActivity? Parse(byte[] file)
    {
        using var stream = new MemoryStream(file);
        var decoder = new Decode();
        if (!decoder.IsFIT(stream) || !decoder.CheckIntegrity(stream)) return null;

        var broadcaster = new MesgBroadcaster();
        var sessions = new List<SessionMesg>();
        var records = new List<Sample>();
        var laps = new List<LapMesg>();
        var devices = new List<DeviceInfoMesg>();
        FileIdMesg? fileId = null;
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;
        void Add<T>(List<T> list, T item, int max)
        {
            if (list.Count >= max) throw new TooLargeException();
            list.Add(item);
        }
        broadcaster.SessionMesgEvent += (_, e) => Add(sessions, (SessionMesg)e.mesg, MaxMessages);
        broadcaster.RecordMesgEvent += (_, e) => Add(records, Sample.From((RecordMesg)e.mesg), MaxRecords);
        broadcaster.LapMesgEvent += (_, e) => Add(laps, (LapMesg)e.mesg, MaxMessages);
        broadcaster.DeviceInfoMesgEvent += (_, e) => Add(devices, (DeviceInfoMesg)e.mesg, MaxMessages);
        broadcaster.FileIdMesgEvent += (_, e) => fileId ??= (FileIdMesg)e.mesg;
        stream.Position = 0;
        try { if (!decoder.Read(stream) || sessions.Count == 0) return null; }
        catch (TooLargeException) { return null; }

        if (sessions.Count == 1) return ParseSession(sessions[0], records, laps, fileId, devices);
        if (sessions.Count > 200) return null;
        var ordered = sessions.OrderBy(s => s.GetStartTime()?.GetDateTime()).ToArray();
        var legs = ordered.Select(session =>
        {
            var from = session.GetStartTime()?.GetDateTime();
            var until = session.GetTimestamp()?.GetDateTime();
            return ParseSession(session,
                records.Where(r => r.Time >= from && (r.Time < until || session == ordered[^1] && r.Time == until)).ToList(),
                laps.Where(l => l.GetStartTime()?.GetDateTime() >= from && l.GetStartTime()?.GetDateTime() < until).ToList(), fileId, devices);
        }).OfType<ParsedActivity>().ToArray();
        if (legs.Length != sessions.Count || legs.Any(l => l.DurationS <= 0)) return null;
        var first = legs[0];
        var parts = legs.Select((l, i) => new ActivityPart(i, l.Sport,
            (int)Math.Round((l.StartedAt - first.StartedAt).TotalSeconds),
            (int)Math.Round((l.StartedAt - first.StartedAt).TotalSeconds) + l.DurationS,
            l.DistanceM, l.Sport is "running" or "walking" or "hiking")).ToArray();
        if (parts.Zip(parts.Skip(1)).Any(p => p.First.EndS > p.Second.StartS)) return null;
        var sporting = parts.Where(p => p.Sport != "transition").Select(p => p.Sport).ToArray();
        var kind = sporting.SequenceEqual(new[] { "swimming", "cycling", "running" }) ? "triathlon"
            : sporting.Contains("swimming") && sporting.Contains("running") && sporting.All(s => s is "running" or "swimming")
                ? "swimrun" : "multisport";
        if (kind == "swimrun") parts = parts.Select(p => p with { UseEquipment = p.Sport != "transition" }).ToArray();
        // FIT distance may restart for each session, or already include preceding sessions.
        var samples = new List<(ParsedActivity Leg, int I, int T, float? D)>();
        double offset = 0;
        foreach (var (leg, part) in legs.Zip(parts))
        {
            var lastDistance = leg.Dist?.LastOrDefault(d => d is not null) ?? 0;
            var baseline = lastDistance > leg.DistanceM + 1 ? offset : 0;
            if (leg.T.Length == 0 || leg.T[0] > 0)
                samples.Add((leg, -1, part.StartS, (float)offset));
            for (var i = 0; i < leg.T.Length; i++)
            {
                var time = leg.T[i] + part.StartS;
                if (samples.Count > 0 && time <= samples[^1].T)
                {
                    if (time == part.StartS && time == samples[^1].T) continue;
                    return null;
                }
                samples.Add((leg, i, time, leg.Dist?[i] is { } d ? (float)(offset + d - baseline) : null));
            }
            offset += part.DistanceM;
            // Session summaries supply exact sport boundaries even when smart recording omits a record there.
            if (samples.Count == 0 || samples[^1].T < part.EndS)
                samples.Add((leg, -1, part.EndS, (float)offset));
        }
        T?[]? Merge<T>(Func<ParsedActivity, T?[]?> channel) where T : struct
        {
            var values = samples.Select(s => s.I < 0 ? null : channel(s.Leg)?[s.I]).ToArray();
            return values.Any(v => v is not null) ? values : null;
        }
        var latitude = Merge(l => l.Lat);
        var longitude = Merge(l => l.Lon);
        return first with
        {
            Sport = kind, DurationS = parts[^1].EndS, MovingS = legs.Sum(l => l.MovingS ?? l.DurationS),
            DistanceM = legs.Sum(l => l.DistanceM), AvgSpeedMps = null,
            AvgHr = null, MaxHr = legs.Max(l => l.MaxHr),
            ElevationGainM = legs.Any(l => l.ElevationGainM is not null) ? legs.Sum(l => l.ElevationGainM ?? 0) : null,
            T = samples.Select(s => s.T).ToArray(), Dist = samples.Select(s => s.D).ToArray(),
            Hr = Merge(l => l.Hr), Speed = Merge(l => l.Speed), Alt = Merge(l => l.Alt),
            Lat = latitude, Lon = longitude, Route = Route(latitude, longitude),
            Cadence = Merge(l => l.Cadence), Temp = Merge(l => l.Temp),
            Laps = legs.Zip(parts).SelectMany(p => (p.First.Laps ?? []).Select(l => l with { StartS = l.StartS + p.Second.StartS })).ToArray(),
            Parts = parts,
        };
    }

    private static ParsedActivity? ParseSession(SessionMesg session, List<Sample> records, List<LapMesg> laps,
        FileIdMesg? fileId, List<DeviceInfoMesg> devices)
    {
        var start = session.GetStartTime()?.GetDateTime() ?? (records.Count > 0 ? records[0].Time : null);
        if (start is not { } startedAt) return null;

        records = records.Where(r => r.Time is not null).ToList();
        var lat = Channel(records, r => r.Lat is { } v ? v * DegreesPerSemicircle : (double?)null);
        var lon = Channel(records, r => r.Lon is { } v ? v * DegreesPerSemicircle : (double?)null);
        var dist = Channel(records, r => r.Dist);
        // FIT gives running and walking cadence per leg (rpm); steps/min is double that. Cycling is already rpm.
        var stepsPerCycle = session.GetSport() is Sport.Running or Sport.Walking or Sport.Hiking ? 2 : 1;
        int Seconds(System.DateTime? time) => time is { } t ? (int)Math.Round((t - startedAt).TotalSeconds) : 0;
        short? Cadence(byte? cadence, float? fractional) =>
            cadence is { } c ? (short)Math.Round((c + (fractional ?? 0)) * stepsPerCycle) : null;

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
            Device: Device(fileId, devices),
            T: records.Select(r => Seconds(r.Time)).ToArray(),
            Dist: dist,
            Hr: Channel(records, r => (short?)r.Hr),
            Speed: Channel(records, r => r.Speed),
            Alt: Channel(records, r => r.Alt),
            Lat: lat,
            Lon: lon,
            Cadence: Channel(records, r => Cadence(r.Cadence, r.FractionalCadence)),
            Temp: Channel(records, r => (short?)r.Temp),
            Laps: laps.Select(l => new ParsedLap(
                StartS: Seconds(l.GetStartTime()?.GetDateTime()),
                DurationS: (int)Math.Round(l.GetTotalElapsedTime() ?? 0),
                MovingS: l.GetTotalTimerTime() is { } lapTimer ? (int)Math.Round(lapTimer) : null,
                DistanceM: l.GetTotalDistance() ?? 0,
                AvgHr: l.GetAvgHeartRate(),
                MaxHr: l.GetMaxHeartRate(),
                AscentM: l.GetTotalAscent(),
                DescentM: l.GetTotalDescent(),
                AvgCadence: Cadence(l.GetAvgCadence(), l.GetAvgFractionalCadence()))).ToArray());
    }

    // The watch name: the product name if the watch writes it (device_info for the file's creator), otherwise
    // manufacturer + Garmin's product code, e.g. "Garmin Fenix 5s".
    private static string? Device(FileIdMesg? fileId, List<DeviceInfoMesg> devices)
    {
        var named = devices
            .Where(d => d.GetDeviceIndex() == DeviceIndex.Creator)
            .Select(d => d.GetProductNameAsString())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        if (named is not null) return named;
        if (fileId?.GetManufacturer() is not { } manufacturer) return null;
        var maker = ConstantName(typeof(Manufacturer), manufacturer);
        var product = manufacturer == Manufacturer.Garmin && fileId.GetGarminProduct() is { } code
            ? ConstantName(typeof(GarminProduct), code)
            : null;
        return product is null ? maker : $"{maker} {product}";
    }

    // The SDK's code lists are classes with constants; the name as text with spaces before digits ("Fenix5s" -> "Fenix 5s").
    private static string? ConstantName(Type constants, ushort value) =>
        constants.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(f => f.IsLiteral && f.FieldType == typeof(ushort) && (ushort)f.GetRawConstantValue()! == value)
            ?.Name is { } name
            ? System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[0-9])", " ")
            : null;

    private static T?[]? Channel<T>(List<Sample> records, Func<Sample, T?> read) where T : struct
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
