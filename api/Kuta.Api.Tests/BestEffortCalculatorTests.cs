using Xunit;

public class BestEffortCalculatorTests
{
    [Fact]
    public void Constant_pace_uses_exact_distances_and_earliest_tie()
    {
        var t = Enumerable.Range(0, 15001).ToArray();
        var dist = t.Select(s => (float?)(s * 3)).ToArray();
        var efforts = BestEffortCalculator.Calculate(t, dist);
        var meters = new[] { 1000d, 1609.344, 5000, 10000, 21097.5, 42195 };
        Assert.Equal(new[] { 1000, 1609, 5000, 10000, 21097, 42195 }, efforts.Select(e => e.DistanceM));
        for (var i = 0; i < efforts.Count; i++)
        {
            Assert.Equal(meters[i] / 3, efforts[i].ElapsedS, precision: 8);
            Assert.Equal(0, efforts[i].StartOffsetS);
        }
    }

    [Fact]
    public void Fastest_window_can_start_between_samples()
    {
        var effort = Assert.Single(BestEffortCalculator.Calculate([0, 100, 150, 300], [0, 700, 1200, 1500]));
        Assert.Equal(100d * 200 / 700, effort.StartOffsetS, precision: 8);
        Assert.Equal(150 - effort.StartOffsetS, effort.ElapsedS, precision: 8);
    }

    [Fact]
    public void Fastest_window_can_end_between_samples()
    {
        var effort = BestEffortCalculator.Calculate([0, 100, 150, 300], [0, 300, 800, 1700])[0];
        Assert.Equal(100, effort.StartOffsetS);
        Assert.Equal(50 + 150d * 500 / 900, effort.ElapsedS, precision: 8);
    }

    [Fact]
    public void Pauses_inside_window_count_but_boundary_pauses_do_not()
    {
        var paused = Assert.Single(BestEffortCalculator.Calculate([0, 100, 200, 300], [0, 500, 500, 1000]));
        Assert.Equal(300, paused.ElapsedS);
        var boundary = Assert.Single(BestEffortCalculator.Calculate([0, 100, 200, 300], [0, 0, 1000, 1000]));
        Assert.Equal(100, boundary.StartOffsetS);
        Assert.Equal(100, boundary.ElapsedS);
    }

    [Fact]
    public void Does_not_extrapolate_a_missing_start_or_finish()
    {
        Assert.Empty(BestEffortCalculator.Calculate([30, 300], [100, 1099]));
        var effort = Assert.Single(BestEffortCalculator.Calculate([30, 300], [100, 1100]));
        Assert.Equal(30, effort.StartOffsetS);
        Assert.Equal(270, effort.ElapsedS);
        Assert.Empty(BestEffortCalculator.Calculate([0, 300], [0, 999.99f]));
        Assert.Single(BestEffortCalculator.Calculate([0, 300], [0, 1609.2f])); // Not a full mile.
    }

    [Theory]
    [InlineData(null)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void Invalid_distance_splits_stream_without_bridging(float? invalid)
    {
        Assert.Empty(BestEffortCalculator.Calculate([0, 100, 200], [0, invalid, 1000]));
        var effort = Assert.Single(BestEffortCalculator.Calculate([0, 100, 200, 500], [0, invalid, 1000, 2000]));
        Assert.Equal(200, effort.StartOffsetS);
        Assert.Equal(300, effort.ElapsedS);
    }

    [Fact]
    public void Clock_and_distance_resets_split_stream()
    {
        Assert.Empty(BestEffortCalculator.Calculate([0, 100, 100, 200], [0, 500, 600, 1100]));
        Assert.Empty(BestEffortCalculator.Calculate([0, 100, 50, 200], [0, 500, 600, 1100]));
        Assert.Empty(BestEffortCalculator.Calculate([0, 100, 200, 300], [0, 800, 700, 1600]));
        Assert.Empty(BestEffortCalculator.Calculate([-10, 300], [0, 1000]));
    }

    [Fact]
    public void Missing_short_or_mismatched_streams_have_no_efforts()
    {
        Assert.Empty(BestEffortCalculator.Calculate([], []));
        Assert.Empty(BestEffortCalculator.Calculate([0], [0]));
        Assert.Empty(BestEffortCalculator.Calculate([0, 1], null));
        Assert.Empty(BestEffortCalculator.Calculate([0, 1], [0]));
        Assert.Empty(BestEffortCalculator.Calculate([0, 1, 2], [0, 0, 0]));
    }

    [Fact]
    public void Sliding_windows_match_exhaustive_search_on_irregular_streams()
    {
        var random = new Random(8134);
        for (var trial = 0; trial < 100; trial++)
        {
            var t = new int[50];
            var dist = new float?[50];
            dist[0] = 0;
            for (var i = 1; i < t.Length; i++)
            {
                t[i] = t[i - 1] + random.Next(1, 100);
                dist[i] = dist[i - 1] + random.Next(0, 1000);
            }
            foreach (var effort in BestEffortCalculator.Calculate(t, dist))
            {
                var length = effort.DistanceM switch { 1609 => 1609.344, 21097 => 21097.5, _ => (double)effort.DistanceM };
                var expected = double.PositiveInfinity;
                // Exhaust every pair of segments that could hold the two window boundaries.
                for (var a = 0; a < t.Length - 1; a++)
                for (var b = a; b < t.Length - 1; b++)
                {
                    var da = dist[a]!.Value;
                    var db = dist[b]!.Value;
                    var va = dist[a + 1]!.Value - da;
                    var vb = dist[b + 1]!.Value - db;
                    if (va <= 0 || vb <= 0) continue;
                    var low = Math.Max(da, db - length);
                    var high = Math.Min(da + va, db + vb - length);
                    if (low > high) continue;
                    foreach (var d in new[] { low, high })
                    {
                        var start = t[a] + (t[a + 1] - t[a]) * (d - da) / va;
                        var end = t[b] + (t[b + 1] - t[b]) * (d + length - db) / vb;
                        expected = Math.Min(expected, end - start);
                    }
                }
                Assert.Equal(expected, effort.ElapsedS, precision: 8);
            }
        }
    }
}
