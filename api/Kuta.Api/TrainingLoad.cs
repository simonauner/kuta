public sealed record TrainingLoadDay(DateOnly Day, double Load, double Fitness, double Fatigue, double Form);

// Pace-based training load for running (rTSS) and Banister's model in TrainingPeaks' simplification:
// fitness (CTL) and fatigue (ATL) are exponential averages of daily load over 42 and 7 days respectively,
// and form (TSB) is the difference. One hour at threshold pace gives 100.
public static class TrainingLoad
{
    public const int FitnessDays = 42;
    public const int FatigueDays = 7;

    // The warning applies to fitness rising faster than this per week, and only once the history is long enough
    // that fitness isn't just rising from zero.
    public const double RampWarningPerWeek = 7;
    public const int MinimumHistoryDays = FitnessDays;

    // Speed is smoothed over at least 30 s, so GPS noise doesn't inflate the square. Gaps longer than 10 s
    // (the watch's auto-pause) form their own segments with their own low speed.
    const int WindowS = 30;
    const int GapS = 10;

    // Minetti's energy cost of running (J/kg/m) as a function of grade, measured between −45 % and +45 %.
    // The ratio to flat ground (3.6) makes the speed grade-adjusted: uphill costs more, gentle downhill less.
    public static double GradeFactor(double grade)
    {
        var i = Math.Clamp(grade, -0.45, 0.45);
        return ((((155.4 * i - 30.4) * i - 43.3) * i + 46.3) * i + 19.5) * i / 3.6 + 1;
    }

    // ∫ v² dt (m²/s) as Σ (Δd · f)²/Δt per segment, where f is the segment's grade factor from the altitude stream.
    // Corrupt values and reset time or distance break the stream; missing altitude just means flat ground.
    public static double Intensity(int[] t, float?[]? dist, float?[]? alt = null)
    {
        if (dist is null || dist.Length != t.Length) return 0;
        if (alt?.Length != t.Length) alt = null;
        double sum = 0;
        (int T, double D, float? A)? start = null, previous = null;
        void Add((int T, double D, float? A) a, (int T, double D, float? A) b)
        {
            if (b.T <= a.T) return;
            var d = b.D - a.D;
            if (d > 0 && a.A is { } from && b.A is { } to) d *= GradeFactor((to - from) / d);
            sum += d * d / (b.T - a.T);
        }
        for (var i = 0; i < t.Length; i++)
        {
            var valid = t[i] >= 0 && dist[i] is { } d && float.IsFinite(d) && d >= 0;
            if (!valid || (previous is { } before && (t[i] <= before.T || dist[i]!.Value < before.D)))
            {
                if (start is { } open && previous is { } close) Add(open, close);
                start = previous = null;
                if (!valid) continue;
            }
            var point = (T: t[i], D: (double)dist[i]!.Value, A: alt?[i] is { } a && float.IsFinite(a) ? a : (float?)null);
            if (previous is { } last && point.T - last.T > GapS)
            {
                Add(start!.Value, last);
                Add(last, point);
                start = point;
            }
            else if (start is { } chunk && point.T - chunk.T >= WindowS)
            {
                Add(chunk, point);
                start = point;
            }
            start ??= point;
            previous = point;
        }
        if (start is { } first && previous is { } end) Add(first, end);
        return sum;
    }

    // Running in a regular activity, otherwise only the multisport activity's running legs.
    public static double RunIntensity(string sport, int[] t, float?[]? dist, float?[]? alt, ActivityPart[]? parts) =>
        parts is { Length: > 0 }
            ? parts.Where(p => ActivitySports.IsRunning(p.Sport)).Sum(p =>
            {
                var leg = Multisport.Slice(t, dist, p, alt);
                return Intensity(leg.T, leg.Dist, leg.Alt);
            })
            : ActivitySports.IsRunning(sport) ? Intensity(t, dist, alt) : 0;

    // rTSS = hours × (v / vT)² × 100 = ∫ v² dt / vT² / 36.
    public static double Load(double intensity, double thresholdSpeed) => intensity / (thresholdSpeed * thresholdSpeed) / 36;

    // Daily series from the first day with load until today. Days without activities have load 0.
    public static IReadOnlyList<TrainingLoadDay> Series(IReadOnlyDictionary<DateOnly, double> loads, DateOnly today)
    {
        if (loads.Count == 0) return [];
        var first = loads.Keys.Min();
        var days = new List<TrainingLoadDay>();
        double fitness = 0, fatigue = 0;
        for (var day = first; day <= today; day = day.AddDays(1))
        {
            var load = loads.GetValueOrDefault(day);
            fitness += (load - fitness) / FitnessDays;
            fatigue += (load - fatigue) / FatigueDays;
            days.Add(new TrainingLoadDay(day, load, fitness, fatigue, fitness - fatigue));
        }
        return days;
    }

    // Change in fitness over the last week; null until the history is long enough.
    public static double? RampPerWeek(IReadOnlyList<TrainingLoadDay> days) =>
        days.Count > MinimumHistoryDays ? days[^1].Fitness - days[^8].Fitness : null;
}
