using Xunit;

public class TrainingLoadTests
{
    static (int[] T, float?[] Dist) Steady(int seconds, double speed, int startT = 0, double startD = 0) =>
        (Enumerable.Range(0, seconds + 1).Select(s => startT + s).ToArray(),
         Enumerable.Range(0, seconds + 1).Select(s => (float?)(startD + s * speed)).ToArray());

    [Fact]
    public void An_hour_at_threshold_speed_gives_100()
    {
        var (t, dist) = Steady(3600, 3);
        Assert.Equal(3600 * 9, TrainingLoad.Intensity(t, dist), precision: 6);
        Assert.Equal(100, TrainingLoad.Load(TrainingLoad.Intensity(t, dist), 3), precision: 6);
        Assert.Equal(25, TrainingLoad.Load(TrainingLoad.Intensity(t, dist), 6), precision: 6);
    }

    [Fact]
    public void Gps_noise_is_smoothed_and_pauses_and_broken_samples_add_no_load()
    {
        // 2 and 4 m/s every other second: without smoothing v² averages 10, with 30 s segments 9.
        var t = Enumerable.Range(0, 601).ToArray();
        var dist = t.Select(s => (float?)(s * 3 + (s % 2 == 1 ? -1 : 0))).ToArray();
        Assert.Equal(600 * 9, TrainingLoad.Intensity(t, dist), precision: 0);

        // Auto pause: the watch stands still for two minutes between two equally long segments.
        var (t1, d1) = Steady(300, 3);
        var (t2, d2) = Steady(300, 3, 420, 900);
        Assert.Equal(600 * 9, TrainingLoad.Intensity([.. t1, .. t2], [.. d1, .. d2]), precision: 6);

        // A broken value breaks the stream instead of computing the speed across the gap.
        float?[] broken = [0, 300, null, 900, 1200];
        Assert.Equal(300d * 300 / 100 * 2, TrainingLoad.Intensity([0, 100, 200, 300, 400], broken), precision: 6);
        Assert.Equal(0, TrainingLoad.Intensity([0, 100], null));
    }

    [Fact]
    public void Hills_scale_the_speed_by_minettis_cost_of_running()
    {
        Assert.Equal(1, TrainingLoad.GradeFactor(0));
        Assert.Equal(5.968, TrainingLoad.GradeFactor(0.1) * 3.6, precision: 3);
        Assert.Equal(2.152, TrainingLoad.GradeFactor(-0.1) * 3.6, precision: 3);
        Assert.Equal(TrainingLoad.GradeFactor(0.45), TrainingLoad.GradeFactor(2));

        // 10 % uphill at 3 m/s; missing or incomplete elevation counts as flat ground.
        var (t, dist) = Steady(600, 3);
        var up = t.Select(s => (float?)(s * 0.3)).ToArray();
        var flat = 600 * 9d;
        var hill = flat * Math.Pow(TrainingLoad.GradeFactor(0.1), 2);
        Assert.Equal(hill, TrainingLoad.Intensity(t, dist, up), precision: 6);
        Assert.Equal(flat * Math.Pow(TrainingLoad.GradeFactor(-0.1), 2),
            TrainingLoad.Intensity(t, dist, up.Reverse().ToArray()), precision: 6);
        Assert.Equal(flat, TrainingLoad.Intensity(t, dist, up.Select(_ => (float?)null).ToArray()), precision: 6);
        Assert.Equal(flat, TrainingLoad.Intensity(t, dist, up[..10]), precision: 6);

        // The multisport parts clip elevation together with distance, even at interpolated boundaries.
        ActivityPart[] parts = [new(0, "cycling", 0, 295, 885, false), new(1, "running", 295, 600, 915, false)];
        Assert.Equal(hill * 305 / 600, TrainingLoad.RunIntensity("multisport", t, dist, up, parts), precision: 6);
    }

    [Fact]
    public void Only_running_counts_in_plain_and_multisport_activities()
    {
        var (t, dist) = Steady(600, 5);
        Assert.Equal(0, TrainingLoad.RunIntensity("cycling", t, dist, null, null));
        Assert.Equal(600 * 25, TrainingLoad.RunIntensity("trail run", t, dist, null, null), precision: 6);
        ActivityPart[] parts = [new(0, "cycling", 0, 300, 1500, false), new(1, "running", 300, 600, 1500, false)];
        Assert.Equal(300 * 25, TrainingLoad.RunIntensity("multisport", t, dist, null, parts), precision: 6);
    }

    [Fact]
    public void Threshold_speed_is_the_pace_vdot_sustains_for_an_hour()
    {
        // VDOT 50 runs 10 km in about 41:20, so the one-hour pace is just below: just under 4 m/s (4:13 /km).
        var speed = RacePredictor.ThresholdSpeed(50);
        Assert.InRange(speed, 3.9, 4.0);
        Assert.Equal(50, RacePredictor.Vdot(speed * 3600, 3600), precision: 6);
    }

    [Fact]
    public void Fitness_and_fatigue_follow_42_and_7_day_averages_and_warn_on_fast_ramps()
    {
        var start = new DateOnly(2026, 1, 1);
        var single = TrainingLoad.Series(new Dictionary<DateOnly, double> { [start] = 100 }, start.AddDays(1));
        Assert.Equal(new[] { 100 / 42d, 100 / 7d }, new[] { single[0].Fitness, single[0].Fatigue });
        Assert.Equal(0, single[1].Load);
        Assert.Equal(single[0].Fitness * 41 / 42, single[1].Fitness, precision: 9);
        Assert.Equal(single[1].Fitness - single[1].Fatigue, single[1].Form, precision: 9);
        Assert.Empty(TrainingLoad.Series(new Dictionary<DateOnly, double>(), start));

        // Steady training gives no warning; a doubling in the last week does, but not before 42 days of history.
        Dictionary<DateOnly, double> Loads(int days, Func<int, double> load) =>
            Enumerable.Range(0, days).ToDictionary(i => start.AddDays(i), load);
        var steady = TrainingLoad.Series(Loads(120, _ => 60), start.AddDays(119));
        Assert.True(TrainingLoad.RampPerWeek(steady) < TrainingLoad.RampWarningPerWeek);
        var spike = TrainingLoad.Series(Loads(120, i => i < 113 ? 60 : 180), start.AddDays(119));
        Assert.True(TrainingLoad.RampPerWeek(spike) > TrainingLoad.RampWarningPerWeek);
        Assert.Null(TrainingLoad.RampPerWeek(TrainingLoad.Series(Loads(30, _ => 300), start.AddDays(29))));
    }
}
