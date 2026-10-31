public sealed record RacePrediction(int DistanceM, double ElapsedS);

public sealed record RacePredictions(StoredBestEffort? Basis, double? Vdot, IReadOnlyList<RacePrediction> Predictions);

// Jack Daniels och Jimmy Gilberts formler (Oxygen Power, 1979): syreupptag vid en fart och andel av maxkapaciteten
// som kan hållas under en viss tid. VDOT = syreupptaget loppet krävde / andelen; samma VDOT ger likvärdiga lopp.
public static class RacePredictor
{
    // Underlaget är de senaste veckornas form, inte gamla personbästa.
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    // Korta insatser ur intervallpass överskattar uthålligheten, så prognosen bygger på 5 km och längre.
    public const int MinimumBasisDistanceM = 5000;

    // Över världsrekordnivå (~85) är insatsen ett mätfel, inte form.
    public const double MaximumVdot = 90;

    public static double Vdot(double meters, double seconds)
    {
        var minutes = seconds / 60;
        var velocity = meters / minutes;
        var oxygen = -4.60 + 0.182258 * velocity + 0.000104 * velocity * velocity;
        var fraction = 0.8 + 0.1894393 * Math.Exp(-0.012778 * minutes) + 0.2989558 * Math.Exp(-0.1932605 * minutes);
        return oxygen / fraction;
    }

    // Tröskelfart (m/s): farten som VDOT räcker till i en timme, den löparversion av FTP som rTSS bygger på.
    // Andelen av maxkapaciteten efter 60 min är fast, så syreupptaget ger farten ur andragradsekvationen.
    public static double ThresholdSpeed(double vdot)
    {
        const double minutes = 60;
        var fraction = 0.8 + 0.1894393 * Math.Exp(-0.012778 * minutes) + 0.2989558 * Math.Exp(-0.1932605 * minutes);
        var oxygen = vdot * fraction;
        const double a = 0.000104, b = 0.182258;
        var metersPerMinute = (-b + Math.Sqrt(b * b + 4 * a * (oxygen + 4.60))) / (2 * a);
        return metersPerMinute / 60;
    }

    // VDOT sjunker när tiden för en given distans ökar, så tiden kan sökas med bisektion.
    public static double? Predict(double meters, double vdot)
    {
        double low = 60, high = 48 * 3600;
        if (!(Vdot(meters, low) >= vdot && Vdot(meters, high) <= vdot)) return null;
        for (var i = 0; i < 100 && high - low > 0.01; i++)
        {
            var middle = (low + high) / 2;
            if (Vdot(meters, middle) > vdot) low = middle;
            else high = middle;
        }
        return (low + high) / 2;
    }

    // efforts: personens insatser inom fönstret. Den med högst VDOT är underlaget; lika VDOT ger den senaste.
    public static RacePredictions Calculate(IEnumerable<StoredBestEffort> efforts)
    {
        var basis = efforts
            .Where(e => e.DistanceM >= MinimumBasisDistanceM && e.ElapsedS > 0)
            .Select(e => (Effort: e, Vdot: BestEffortCalculator.Meters(e.DistanceM) is { } m ? Vdot(m, e.ElapsedS) : double.NaN))
            .Where(e => double.IsFinite(e.Vdot) && e.Vdot > 0 && e.Vdot <= MaximumVdot)
            .OrderByDescending(e => e.Vdot).ThenByDescending(e => e.Effort.AchievedAt)
            .FirstOrDefault();
        if (basis.Effort is null) return new RacePredictions(null, null, []);
        var predictions = BestEffortCalculator.Distances
            .Select(d => Predict(d.Meters, basis.Vdot) is { } s ? new RacePrediction(d.Label, s) : null)
            .OfType<RacePrediction>().ToArray();
        return new RacePredictions(basis.Effort, basis.Vdot, predictions);
    }
}
