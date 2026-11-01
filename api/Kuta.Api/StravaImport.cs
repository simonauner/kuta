using System.Globalization;
using System.IO.Compression;
using System.Xml;
using Microsoft.VisualBasic.FileIO;

public sealed record StravaActivity(string Id, string? Title, string Filename, ParsedActivity Summary,
    string? GearId = null, string? GearName = null, string? Description = null, string[]? Tags = null);

// Reads only activity data, never the export's profile, contacts, private notes or media.
public static class StravaImport
{
    public const int MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxActivities = 20000;
    // Strava's activities.csv has about 100 columns; the cap keeps a crafted header from making every row expensive.
    public const int MaxColumns = 500;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static List<StravaActivity> ReadManifest(Stream csv)
    {
        using var parser = new TextFieldParser(csv) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException("Missing CSV header.");
        if (header.Length > MaxColumns) throw new InvalidDataException("Too many CSV columns.");
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < header.Length; i++) columns[header[i]] = i; // The last distance/time columns use SI.
        int Index(string name) => columns.GetValueOrDefault(name, -1);
        foreach (var required in new[] { "Activity ID", "Activity Date", "Activity Name", "Activity Type", "Filename", "Elapsed Time", "Distance" })
            if (Index(required) < 0) throw new InvalidDataException("Missing CSV column.");
        if (Index("Distance") == Array.IndexOf(header, "Distance"))
            throw new InvalidDataException("Missing SI distance column.");
        var result = new List<StravaActivity>();
        while (!parser.EndOfData)
        {
            if (result.Count >= MaxActivities) throw new InvalidDataException("Too many activities.");
            var row = parser.ReadFields()!;
            string Field(string name) => Index(name) is var i && i >= 0 && i < row.Length ? row[i] : "";
            double? Number(string name)
            {
                var value = Field(name);
                if (value.Length == 0) return null;
                if (!double.TryParse(value, NumberStyles.Float, Invariant, out var n) || !double.IsFinite(n) || n < 0)
                    throw new InvalidDataException("Invalid number.");
                return n;
            }
            int Seconds(string name) => checked((int)Math.Round(Number(name) ?? 0));
            var id = Field("Activity ID");
            if (id.Length is 0 or > 30 || !id.All(char.IsAsciiDigit)) throw new InvalidDataException("Invalid activity ID.");
            if (!DateTime.TryParseExact(Field("Activity Date"), ["MMM d, yyyy, h:mm:ss tt", "yyyy-MM-dd HH:mm:ss"],
                    Invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                throw new InvalidDataException("Invalid activity date.");
            var filename = Field("Filename");
            if (filename.Length > 500 || filename.Contains('\\') || filename.Split('/').Any(s => s is ".." or ".") || filename.StartsWith('/'))
                throw new InvalidDataException("Invalid filename.");
            var title = Field("Activity Name");
            if (title.Length > 500) throw new InvalidDataException("Title too long.");
            var description = Field("Activity Description");
            if (ActivityInput.ValidateDescription(description) is not null)
                throw new InvalidDataException("Description too long.");
            var sport = ActivitySports.FromStrava(Field("Activity Type"));
            if (sport.Length is 0 or > 100) throw new InvalidDataException("Invalid sport.");
            var duration = Seconds("Elapsed Time");
            var moving = Number("Moving Time") is { } m ? checked((int)Math.Round(m)) : (int?)null;
            short? HeartRate(string name) => Number(name) is { } hr ? checked((short)Math.Round(hr)) : null;
            float? Single(string name) => Number(name) is { } n && n <= float.MaxValue ? (float)n : null;
            result.Add(new StravaActivity(id, title.Length == 0 ? null : title, filename,
                new ParsedActivity(sport, start, duration, moving, Number("Distance") ?? 0,
                    HeartRate("Average Heart Rate"), HeartRate("Max Heart Rate"), Single("Average Speed"), Single("Elevation Gain"),
                    null, [], null, null, null, null, null, null),
                ShoeId(Field("Gear")), string.IsNullOrWhiteSpace(Field("Activity Gear")) ? null : Field("Activity Gear").Trim(),
                ActivityInput.NormalizeDescription(description),
                // Strava's other tags (running, riding, offroad, commute) describe the sport or have no Kuta tag.
                [.. Field("Tag").Split('|').Intersect(["race", "long_run"])]));
        }
        if (result.Select(r => r.Id).Distinct().Count() != result.Count) throw new InvalidDataException("Duplicate activity IDs.");
        return result;
    }

    private static string? ShoeId(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        if (value.StartsWith('g')) value = value[1..];
        if (!decimal.TryParse(value, NumberStyles.Float, Invariant, out var id) || id <= 0 || decimal.Truncate(id) != id)
            throw new InvalidDataException("Invalid shoe ID.");
        return "g" + id.ToString("0", Invariant);
    }

    public static List<StravaShoe> ReadShoes(Stream? csv, IReadOnlyList<StravaActivity> activities)
    {
        static string Label(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var metadata = new List<(string Label, string Name, string? Brand)>();
        if (csv is not null)
        {
            using var parser = new TextFieldParser(csv) { HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            var header = parser.ReadFields() ?? throw new InvalidDataException("Missing shoe header.");
            var columns = new[] { "Shoe Brand", "Shoe Model", "Shoe Name" }.Select(n => Array.IndexOf(header, n)).ToArray();
            if (columns.Any(i => i < 0)) throw new InvalidDataException("Missing shoe column.");
            while (!parser.EndOfData)
            {
                if (metadata.Count >= MaxActivities) throw new InvalidDataException("Too many shoes.");
                var row = parser.ReadFields()!;
                var parts = columns.Select(i => i < row.Length ? row[i].Trim() : "").ToArray();
                var label = Label(string.Join(' ', parts.Where(p => p.Length > 0)));
                if (label.Length == 0) continue;
                var name = parts[2].Length > 0 ? parts[2] : parts[1].Length > 0 ? parts[1] : parts[0];
                metadata.Add((label, name, parts[0].Length == 0 ? null : parts[0]));
            }
        }
        var byLabel = metadata.GroupBy(m => m.Label, StringComparer.Ordinal).ToDictionary(
            g => g.Key, g => g.Select(m => (m.Name, m.Brand)).Distinct().ToArray(), StringComparer.Ordinal);
        var result = new List<StravaShoe>();
        // shoes.csv has no IDs: only shoes referenced from activities can be identified reliably.
        foreach (var group in activities.Where(a => a.GearId is not null).GroupBy(a => a.GearId!))
        {
            var label = group.OrderByDescending(a => a.Summary.StartedAt).FirstOrDefault(a => a.GearName is not null)?.GearName
                ?? $"Strava {group.Key}";
            var matches = byLabel.GetValueOrDefault(Label(label));
            var shoe = matches is { Length: 1 }
                ? new StravaShoe(group.Key, matches[0].Name, matches[0].Brand)
                : new StravaShoe(group.Key, label, null);
            if (EquipmentEndpoints.Validate(new EquipmentRequest(shoe.Name, shoe.Brand, "shoe")) is not null)
                throw new InvalidDataException("Invalid shoe details.");
            result.Add(shoe);
        }
        return result;
    }

    public sealed class ExpansionBudget(long maxBytes)
    {
        private long remainingBytes = maxBytes;
        public bool Exhausted => remainingBytes <= 0;

        public void Consume(int bytes)
        {
            remainingBytes -= bytes;
            if (remainingBytes < 0) throw new InvalidDataException("Archive expands too much.");
        }
    }

    public static byte[] ReadFile(ZipArchiveEntry entry, ExpansionBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (entry.Length > MaxFileBytes) throw new InvalidDataException("File too large.");
        using var input = entry.Open();
        if (!entry.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return ReadBounded(input, budget, ct);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return ReadBounded(gzip, budget, ct);
    }

    private static byte[] ReadBounded(Stream input, ExpansionBudget budget, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = input.Read(buffer);
            if (read == 0) break;
            // Bytes from files that are later rejected also consume the archive's shared budget.
            budget.Consume(read);
            if (output.Length + read > MaxFileBytes) throw new InvalidDataException("File too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public static ParsedActivity MergeFit(ParsedActivity summary, ParsedActivity fit)
    {
        // Multisport summaries and session boundaries belong to the same FIT clock and must stay together.
        if (fit.Parts is { Length: > 0 }) return fit;
        var offset = checked((int)Math.Round((fit.StartedAt - summary.StartedAt).TotalSeconds));
        return summary with
        {
            T = fit.T.Select(t => checked(t + offset)).ToArray(),
            Dist = fit.Dist, Hr = fit.Hr, Speed = fit.Speed, Alt = fit.Alt,
            Lat = fit.Lat, Lon = fit.Lon, Route = fit.Route,
            Device = fit.Device, Cadence = fit.Cadence, Temp = fit.Temp,
            Laps = fit.Laps?.Select(l => l with { StartS = checked(l.StartS + offset) }).ToArray(),
        };
    }

    public static ParsedActivity ParseGpx(byte[] bytes, ParsedActivity summary)
    {
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFileBytes,
        });
        // Streamed: building a tree first would let a small compressed file with millions of elements exhaust memory.
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "gpx") throw new InvalidDataException("Not GPX.");
        var t = new List<int>();
        var lat = new List<double?>();
        var lon = new List<double?>();
        var alt = new List<float?>();
        var hr = new List<short?>();
        var dist = new List<float?>();
        var speed = new List<float?>();
        double distance = 0;
        int segment = 0, previousSegment = -1;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName == "trkseg") segment++;
            if (reader.LocalName != "trkpt") continue;
            if (t.Count >= 500000) throw new InvalidDataException("Too many points.");
            var latText = reader.GetAttribute("lat");
            var lonText = reader.GetAttribute("lon");
            string? time = null, elevation = null, pulse = null;
            using (var point = reader.ReadSubtree())
            {
                point.Read();
                point.Read();
                while (!point.EOF)
                {
                    if (point.NodeType == XmlNodeType.Element && (point.Depth == 1 && point.LocalName is "time" or "ele" || point.LocalName == "hr"))
                    {
                        var name = point.LocalName;
                        var value = point.ReadElementContentAsString();
                        if (name == "time") time ??= value;
                        else if (name == "ele") elevation ??= value;
                        else pulse ??= value;
                        continue;
                    }
                    point.Read();
                }
            }
            if (!DateTime.TryParse(time, Invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var stamp))
                throw new InvalidDataException("Missing GPX time.");
            var seconds = checked((int)Math.Round((stamp - summary.StartedAt).TotalSeconds));
            if (seconds < 0 || t.Count > 0 && seconds < t[^1]) throw new InvalidDataException("Unordered GPX times.");
            static double Coordinate(string? text, double max)
            {
                if (!double.TryParse(text, NumberStyles.Float, Invariant, out var value) ||
                    !double.IsFinite(value) || Math.Abs(value) > max) throw new InvalidDataException("Invalid GPX coordinate.");
                return value;
            }
            var latitude = Coordinate(latText, 90);
            var longitude = Coordinate(lonText, 180);
            // Don't count distance across the gap between two GPX segments.
            float? velocity = null;
            if (lat.Count > 0 && segment == previousSegment)
            {
                var step = Distance(lat[^1]!.Value, lon[^1]!.Value, latitude, longitude);
                distance += step;
                if (seconds > t[^1]) velocity = (float)(step / (seconds - t[^1]));
            }
            speed.Add(velocity);
            previousSegment = segment;
            if (elevation is not null && (!float.TryParse(elevation, NumberStyles.Float, Invariant, out var a) || !float.IsFinite(a)))
                throw new InvalidDataException("Invalid elevation.");
            if (pulse is not null && (!short.TryParse(pulse, out var h) || h < 0)) throw new InvalidDataException("Invalid heart rate.");
            t.Add(seconds); lat.Add(latitude); lon.Add(longitude); dist.Add((float)distance);
            alt.Add(elevation is null ? null : float.Parse(elevation, Invariant));
            hr.Add(pulse is null ? null : short.Parse(pulse, Invariant));
        }
        return summary with
        {
            T = t.ToArray(), Lat = lat.Count == 0 ? null : lat.ToArray(), Lon = lon.Count == 0 ? null : lon.ToArray(),
            Dist = dist.Count == 0 ? null : dist.ToArray(), Alt = alt.Any(a => a is not null) ? alt.ToArray() : null,
            Hr = hr.Any(h => h is not null) ? hr.ToArray() : null,
            Speed = speed.Any(v => v is not null) ? speed.ToArray() : null,
            Route = FitImport.Route(lat.ToArray(), lon.ToArray()),
        };
    }

    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2) +
            Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
