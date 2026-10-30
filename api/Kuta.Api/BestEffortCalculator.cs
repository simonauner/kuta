public sealed record BestEffort(int DistanceM, double ElapsedS, double StartOffsetS);

public static class BestEffortCalculator
{
    private static readonly (int Label, double Meters)[] Distances =
        [(1000, 1000), (1609, 1609.344), (5000, 5000), (10000, 10000), (21097, 21097.5), (42195, 42195)];

    public static IReadOnlyList<BestEffort> Calculate(int[] t, float?[]? dist)
    {
        if (dist is null || dist.Length != t.Length) return [];
        var best = new Dictionary<int, BestEffort>();
        var points = new List<(double T, double D)>();
        for (var i = 0; i < t.Length; i++)
        {
            var valid = t[i] >= 0 && dist[i] is { } d && float.IsFinite(d) && d >= 0;
            // Never bridge missing/corrupt samples or a clock/distance reset to invent a record.
            if (!valid || (points.Count > 0 && (t[i] <= points[^1].T || dist[i]!.Value < points[^1].D)))
            {
                Scan(points, best);
                points.Clear();
            }
            if (valid) points.Add((t[i], dist[i]!.Value));
        }
        Scan(points, best);
        return best.Values.OrderBy(e => e.DistanceM).ToArray();
    }

    private static void Scan(List<(double T, double D)> p, Dictionary<int, BestEffort> best)
    {
        foreach (var (label, meters) in Distances)
        {
            var end = 0;
            var start = 0;
            for (var i = 0; i < p.Count; i++)
            {
                // For piecewise linear streams a minimum touches a sample at either end.
                // Check both ends: checking only sample-aligned starts misses faster windows.
                var endDistance = p[i].D + meters;
                while (end < p.Count && p[end].D < endDistance) end++;
                if (end < p.Count)
                    Consider(label, Interpolate(p[end - 1], p[end], endDistance) - p[i].T, p[i].T, best);

                var startDistance = p[i].D - meters;
                if (startDistance < p[0].D) continue;
                while (start + 1 < i && p[start + 1].D <= startDistance) start++;
                var startTime = Interpolate(p[start], p[start + 1], startDistance);
                Consider(label, p[i].T - startTime, startTime, best);
            }
        }
    }

    private static double Interpolate((double T, double D) a, (double T, double D) b, double distance) =>
        a.T + (b.T - a.T) * (distance - a.D) / (b.D - a.D);

    private static void Consider(int label, double elapsed, double start, Dictionary<int, BestEffort> best)
    {
        if (!best.TryGetValue(label, out var previous) || elapsed < previous.ElapsedS - 1e-9 ||
            (Math.Abs(elapsed - previous.ElapsedS) <= 1e-9 && start < previous.StartOffsetS))
            best[label] = new BestEffort(label, elapsed, start);
    }
}
