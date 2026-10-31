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
        // 2 och 4 m/s varannan sekund: utan utjämning blir v² i snitt 10, med 30 s-avsnitt 9.
        var t = Enumerable.Range(0, 601).ToArray();
        var dist = t.Select(s => (float?)(s * 3 + (s % 2 == 1 ? -1 : 0))).ToArray();
        Assert.Equal(600 * 9, TrainingLoad.Intensity(t, dist), precision: 0);

        // Autopaus: klockan står still i två minuter mellan två lika långa avsnitt.
        var (t1, d1) = Steady(300, 3);
        var (t2, d2) = Steady(300, 3, 420, 900);
        Assert.Equal(600 * 9, TrainingLoad.Intensity([.. t1, .. t2], [.. d1, .. d2]), precision: 6);

        // Ett trasigt värde bryter strömmen i stället för att räkna farten över luckan.
        float?[] broken = [0, 300, null, 900, 1200];
        Assert.Equal(300d * 300 / 100 * 2, TrainingLoad.Intensity([0, 100, 200, 300, 400], broken), precision: 6);
        Assert.Equal(0, TrainingLoad.Intensity([0, 100], null));
    }

    [Fact]
    public void Only_running_counts_in_plain_and_multisport_activities()
    {
        var (t, dist) = Steady(600, 5);
        Assert.Equal(0, TrainingLoad.RunIntensity("cycling", t, dist, null));
        Assert.Equal(600 * 25, TrainingLoad.RunIntensity("trail run", t, dist, null), precision: 6);
        ActivityPart[] parts = [new(0, "cycling", 0, 300, 1500, false), new(1, "running", 300, 600, 1500, false)];
        Assert.Equal(300 * 25, TrainingLoad.RunIntensity("multisport", t, dist, parts), precision: 6);
    }

    [Fact]
    public void Threshold_speed_is_the_pace_vdot_sustains_for_an_hour()
    {
        // VDOT 50 klarar 10 km på cirka 41:20, så timfarten ligger strax under: knappt 4 m/s (4:13 /km).
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

        // Jämn träning ger ingen varning; en fördubbling den sista veckan gör det, men inte före 42 dagars historik.
        Dictionary<DateOnly, double> Loads(int days, Func<int, double> load) =>
            Enumerable.Range(0, days).ToDictionary(i => start.AddDays(i), load);
        var steady = TrainingLoad.Series(Loads(120, _ => 60), start.AddDays(119));
        Assert.True(TrainingLoad.RampPerWeek(steady) < TrainingLoad.RampWarningPerWeek);
        var spike = TrainingLoad.Series(Loads(120, i => i < 113 ? 60 : 180), start.AddDays(119));
        Assert.True(TrainingLoad.RampPerWeek(spike) > TrainingLoad.RampWarningPerWeek);
        Assert.Null(TrainingLoad.RampPerWeek(TrainingLoad.Series(Loads(30, _ => 300), start.AddDays(29))));
    }
}
