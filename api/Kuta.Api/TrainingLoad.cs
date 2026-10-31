public sealed record TrainingLoadDay(DateOnly Day, double Load, double Fitness, double Fatigue, double Form);

// Fartbaserad träningsbelastning för löpning (rTSS) och Banisters modell i TrainingPeaks förenkling:
// kondition (CTL) och trötthet (ATL) är exponentiella medelvärden av daglig belastning över 42 respektive 7 dagar,
// och form (TSB) är skillnaden. Ett pass i tröskelfart i en timme ger 100.
public static class TrainingLoad
{
    public const int FitnessDays = 42;
    public const int FatigueDays = 7;

    // Varningen gäller en kondition som ökar snabbare än så här per vecka, och först när historiken räcker
    // för att konditionen inte bara ska stiga från noll.
    public const double RampWarningPerWeek = 7;
    public const int MinimumHistoryDays = FitnessDays;

    // Farten jämnas ut över minst 30 s, så att GPS-brus inte blåser upp kvadraten. Uppehåll längre än 10 s
    // (klockans autopaus) bildar egna avsnitt med sin egen, låga fart.
    const int WindowS = 30;
    const int GapS = 10;

    // ∫ v² dt (m²/s) som Σ Δd²/Δt per avsnitt. Trasiga värden och återställd tid eller distans bryter strömmen.
    public static double Intensity(int[] t, float?[]? dist)
    {
        if (dist is null || dist.Length != t.Length) return 0;
        double sum = 0;
        (int T, double D)? start = null, previous = null;
        void Add((int T, double D) a, (int T, double D) b)
        {
            if (b.T > a.T) sum += (b.D - a.D) * (b.D - a.D) / (b.T - a.T);
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
            var point = (T: t[i], D: (double)dist[i]!.Value);
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

    // Löpning i ett vanligt pass, annars bara multisportpassets löpdelar.
    public static double RunIntensity(string sport, int[] t, float?[]? dist, ActivityPart[]? parts) =>
        parts is { Length: > 0 }
            ? parts.Where(p => ActivitySports.IsRunning(p.Sport)).Sum(p =>
            {
                var leg = Multisport.Slice(t, dist, p);
                return Intensity(leg.T, leg.Dist);
            })
            : ActivitySports.IsRunning(sport) ? Intensity(t, dist) : 0;

    // rTSS = timmar × (v / vT)² × 100 = ∫ v² dt / vT² / 36.
    public static double Load(double intensity, double thresholdSpeed) => intensity / (thresholdSpeed * thresholdSpeed) / 36;

    // Daglig serie från första dagen med belastning till idag. Dagar utan pass har belastning 0.
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

    // Konditionens förändring den senaste veckan; null innan historiken räcker.
    public static double? RampPerWeek(IReadOnlyList<TrainingLoadDay> days) =>
        days.Count > MinimumHistoryDays ? days[^1].Fitness - days[^8].Fitness : null;
}
