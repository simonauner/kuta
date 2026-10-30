using Dynastream.Fit;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

// Ett pass ur en FIT-fil: sammanfattning från session-meddelandet, tidsserier (en array per kanal) från records.
// En kanal som saknas helt blir null, inte en array full av null.
// Nya fält läggs sist med standardvärde: Strava-importen (GPX) skapar typen positionellt.
public sealed record ParsedActivity(
    string Sport, System.DateTime StartedAt, int DurationS, int? MovingS, double DistanceM,
    short? AvgHr, short? MaxHr, float? AvgSpeedMps, float? ElevationGainM, LineString? Route,
    int[] T, float?[]? Dist, short?[]? Hr, float?[]? Speed, float?[]? Alt, double?[]? Lat, double?[]? Lon,
    string? Device = null, short?[]? Cadence = null, short?[]? Temp = null, ParsedLap[]? Laps = null)
{
    // Utan samtycke sparas inte puls (hälsodata): varken sammanfattning, tidsserie eller varvens puls.
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
        var laps = new List<LapMesg>();
        var devices = new List<DeviceInfoMesg>();
        FileIdMesg? fileId = null;
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;
        broadcaster.SessionMesgEvent += (_, e) => sessions.Add((SessionMesg)e.mesg);
        broadcaster.RecordMesgEvent += (_, e) => records.Add((RecordMesg)e.mesg);
        broadcaster.LapMesgEvent += (_, e) => laps.Add((LapMesg)e.mesg);
        broadcaster.DeviceInfoMesgEvent += (_, e) => devices.Add((DeviceInfoMesg)e.mesg);
        broadcaster.FileIdMesgEvent += (_, e) => fileId ??= (FileIdMesg)e.mesg;
        stream.Position = 0;
        if (!decoder.Read(stream) || sessions.Count == 0) return null;

        var session = sessions[0];
        var start = session.GetStartTime()?.GetDateTime() ?? records.FirstOrDefault()?.GetTimestamp()?.GetDateTime();
        if (start is not { } startedAt) return null;

        records = records.Where(r => r.GetTimestamp() is not null).ToList();
        var lat = Channel(records, r => r.GetPositionLat() is { } v ? v * DegreesPerSemicircle : (double?)null);
        var lon = Channel(records, r => r.GetPositionLong() is { } v ? v * DegreesPerSemicircle : (double?)null);
        var dist = Channel(records, r => r.GetDistance());
        // FIT anger löp- och gångkadens per ben (varv/min); steg/min är det dubbla. Cykling är redan varv/min.
        var stepsPerCycle = session.GetSport() is Sport.Running or Sport.Walking or Sport.Hiking ? 2 : 1;
        int Seconds(Dynastream.Fit.DateTime? time) =>
            time is null ? 0 : (int)Math.Round((time.GetDateTime() - startedAt).TotalSeconds);
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
            T: records.Select(r => Seconds(r.GetTimestamp())).ToArray(),
            Dist: dist,
            Hr: Channel(records, r => (short?)r.GetHeartRate()),
            Speed: Channel(records, r => r.GetEnhancedSpeed() ?? r.GetSpeed()),
            Alt: Channel(records, r => r.GetEnhancedAltitude() ?? r.GetAltitude()),
            Lat: lat,
            Lon: lon,
            Cadence: Channel(records, r => Cadence(r.GetCadence(), r.GetFractionalCadence())),
            Temp: Channel(records, r => (short?)r.GetTemperature()),
            Laps: laps.Select(l => new ParsedLap(
                StartS: Seconds(l.GetStartTime()),
                DurationS: (int)Math.Round(l.GetTotalElapsedTime() ?? 0),
                MovingS: l.GetTotalTimerTime() is { } lapTimer ? (int)Math.Round(lapTimer) : null,
                DistanceM: l.GetTotalDistance() ?? 0,
                AvgHr: l.GetAvgHeartRate(),
                MaxHr: l.GetMaxHeartRate(),
                AscentM: l.GetTotalAscent(),
                DescentM: l.GetTotalDescent(),
                AvgCadence: Cadence(l.GetAvgCadence(), l.GetAvgFractionalCadence()))).ToArray());
    }

    // Klockans namn: produktnamnet om klockan skriver det (device_info för den som skapade filen), annars
    // tillverkare + Garmins produktkod, t.ex. "Garmin Fenix 5s".
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

    // SDK:ns kodlistor är klasser med konstanter; namnet som text med mellanslag före siffror ("Fenix5s" -> "Fenix 5s").
    private static string? ConstantName(Type constants, ushort value) =>
        constants.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(f => f.IsLiteral && f.FieldType == typeof(ushort) && (ushort)f.GetRawConstantValue()! == value)
            ?.Name is { } name
            ? System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[0-9])", " ")
            : null;

    private static T?[]? Channel<T>(List<RecordMesg> records, Func<RecordMesg, T?> read) where T : struct
    {
        var values = records.Select(read).ToArray();
        return values.Any(v => v is not null) ? values : null;
    }

    private static LineString? Route(double?[]? lat, double?[]? lon)
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
