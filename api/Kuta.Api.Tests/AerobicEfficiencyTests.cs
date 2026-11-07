using Xunit;

public class AerobicEfficiencyTests
{
    // One sample per second
    static (int[] T, float?[] Dist, short?[] Hr) Run(int seconds, Func<int, double> speed, Func<int, int> hr)
    {
        var t = Enumerable.Range(0, seconds + 1).ToArray();
        var dist = new float?[t.Length];
        double d = 0;
        for (var s = 0; s <= seconds; s++)
        {
            dist[s] = (float)d;
            d += speed(s);
        }
        return (t, dist, t.Select(s => (short?)hr(s)).ToArray());
    }

    [Fact]
    public void Steady_run_gives_speed_and_heart_rate_after_the_warm_up()
    {
        var (t, dist, hr) = Run(3600, _ => 3, s => s < AerobicEfficiency.WarmUpS ? 100 : 150);
        var sample = AerobicEfficiency.Calculate(t, dist, null, hr)!;
        Assert.Equal(3, sample.SpeedMS, precision: 6);
        Assert.Equal(150, sample.Hr, precision: 6);
    }

    [Fact]
    public void Short_unsteady_or_unmeasured_runs_give_nothing()
    {
        var (t, dist, hr) = Run(29 * 60, _ => 3, _ => 150);
        Assert.Null(AerobicEfficiency.Calculate(t, dist, null, hr));

        // 2 min fast, 2 min slow
        var intervals = Run(3600, s => s / 120 % 2 == 1 ? 2 : 4.5, _ => 150);
        Assert.Null(AerobicEfficiency.Calculate(intervals.T, intervals.Dist, null, intervals.Hr));

        var steady = Run(3600, _ => 3, _ => 150);
        Assert.Null(AerobicEfficiency.Calculate(steady.T, steady.Dist, null, null));
        Assert.Null(AerobicEfficiency.ForActivity("cycling", steady.T, steady.Dist, null, steady.Hr, null));
    }

    [Fact]
    public void Missing_heart_rate_and_standing_still_are_left_out()
    {
        // A strap dropout and a five-minute walk: only the running with heart rate counts (a window can
        // straddle the start or end of the walk)
        var (t, dist, hr) = Run(3600, s => s is > 2000 and < 2300 ? 1 : 3, _ => 150);
        for (var s = 1000; s < 1200; s++) hr[s] = null;
        var sample = AerobicEfficiency.Calculate(t, dist, null, hr)!;
        Assert.InRange(sample.SpeedMS, 2.97, 3);
    }

    [Fact]
    public void Climbing_is_grade_adjusted_like_the_training_load()
    {
        var (t, dist, hr) = Run(3600, _ => 3, _ => 150);
        var alt = dist.Select(d => (float?)(d * 0.05)).ToArray();
        var sample = AerobicEfficiency.Calculate(t, dist, alt, hr)!;
        Assert.Equal(3 * TrainingLoad.GradeFactor(0.05), sample.SpeedMS, precision: 3);
    }
}
