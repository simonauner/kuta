public sealed record RacePrediction(int DistanceM, double ElapsedS);

public sealed record RacePredictions(StoredBestEffort? Basis, double? Vdot, IReadOnlyList<RacePrediction> Predictions);

// Jack Daniels and Jimmy Gilbert's formulas (Oxygen Power, 1979): oxygen uptake at a speed and the fraction of max capacity
// that can be sustained for a given time. VDOT = oxygen uptake the race required / the fraction; equal VDOT gives equivalent races.
public static class RacePredictor
{
    // The basis is recent weeks' form, not old personal bests.
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    // Short efforts from interval sessions overestimate endurance, so the prediction is based on 5 km and longer.
    public const int MinimumBasisDistanceM = 5000;

    // Above world-record level (~85) the effort is a measurement error, not form.
    public const double MaximumVdot = 90;

    public static double Vdot(double meters, double seconds)
    {
        var minutes = seconds / 60;
        var velocity = meters / minutes;
        var oxygen = -4.60 + 0.182258 * velocity + 0.000104 * velocity * velocity;
        var fraction = 0.8 + 0.1894393 * Math.Exp(-0.012778 * minutes) + 0.2989558 * Math.Exp(-0.1932605 * minutes);
        return oxygen / fraction;
    }

    // Threshold speed (m/s): the speed VDOT sustains for one hour, the runner's version of FTP that rTSS builds on.
    // The fraction of max capacity after 60 min is fixed, so the oxygen uptake gives the speed from the quadratic equation.
    public static double ThresholdSpeed(double vdot)
    {
        const double minutes = 60;
        var fraction = 0.8 + 0.1894393 * Math.Exp(-0.012778 * minutes) + 0.2989558 * Math.Exp(-0.1932605 * minutes);
        var oxygen = vdot * fraction;
        const double a = 0.000104, b = 0.182258;
        var metersPerMinute = (-b + Math.Sqrt(b * b + 4 * a * (oxygen + 4.60))) / (2 * a);
        return metersPerMinute / 60;
    }

    // VDOT falls as the time for a given distance increases, so the time can be found by bisection.
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

    // efforts: the person's efforts within the window. The one with the highest VDOT is the basis; equal VDOT picks the latest.
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
