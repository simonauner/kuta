using Xunit;

public class RacePredictorTests
{
    private static readonly DateTime Day = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Vdot_and_predictions_match_daniels_table()
    {
        // Daniels' table for VDOT 50: 5 km 19:57, 10 km 41:21, half marathon 1:31:35, marathon 3:10:49.
        Assert.Equal(49.8, RacePredictor.Vdot(5000, 20 * 60), precision: 1);
        Assert.InRange(RacePredictor.Predict(5000, 50)!.Value, 1197 - 5, 1197 + 5);
        Assert.InRange(RacePredictor.Predict(10000, 50)!.Value, 2481 - 10, 2481 + 10);
        Assert.InRange(RacePredictor.Predict(21097.5, 50)!.Value, 5495 - 20, 5495 + 20);
        Assert.InRange(RacePredictor.Predict(42195, 50)!.Value, 11449 - 30, 11449 + 30);
        // The prediction for the basis's own distance returns the basis's time.
        Assert.Equal(1500, RacePredictor.Predict(5000, RacePredictor.Vdot(5000, 1500))!.Value, precision: 1);
    }

    [Fact]
    public void Basis_is_the_strongest_long_effort_and_ignores_short_and_impossible_efforts()
    {
        var activity = Guid.NewGuid();
        var result = RacePredictor.Calculate([
            new(activity, 1000, 150, 0, Day),          // short interval: never a basis
            new(activity, 5000, 600, 0, Day),          // 5 km in 10 min: measurement error above world-record level
            new(activity, 10000, 2481, 0, Day),        // VDOT ≈ 50
            new(activity, 21097, 5800, 0, Day),        // weaker half marathon
        ]);
        Assert.Equal(10000, result.Basis!.DistanceM);
        Assert.Equal(50, result.Vdot!.Value, precision: 0);
        Assert.Equal(new[] { 1000, 1609, 5000, 10000, 21097, 42195 }, result.Predictions.Select(p => p.DistanceM));
        Assert.True(result.Predictions.Zip(result.Predictions.Skip(1)).All(p => p.First.ElapsedS < p.Second.ElapsedS));

        // Equal VDOT picks the most recent effort.
        var later = new StoredBestEffort(Guid.NewGuid(), 10000, 2481, 0, Day.AddDays(1));
        Assert.Same(later, RacePredictor.Calculate([new(activity, 10000, 2481, 0, Day), later]).Basis);

        var none = RacePredictor.Calculate([new(activity, 1609, 300, 0, Day)]);
        Assert.Null(none.Basis);
        Assert.Null(none.Vdot);
        Assert.Empty(none.Predictions);
    }
}
