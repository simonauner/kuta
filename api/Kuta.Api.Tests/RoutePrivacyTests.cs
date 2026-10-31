using Xunit;

public class RoutePrivacyTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();

    private static ActivityStreams Streams(double?[] lat, double?[]? lon = null) => new(
        Enumerable.Range(0, lat.Length).ToArray(), lat.Select((_, i) => (float?)(i * 100)).ToArray(),
        null, null, null, lat, lon ?? lat.Select(_ => (double?)0).ToArray(), null, null);

    [Fact]
    public void Owner_receives_original_streams_and_missing_streams_stay_missing()
    {
        var streams = Streams([0, .001, .01]);
        Assert.Same(streams, RoutePrivacy.ForViewer(streams, Owner, Owner));
        Assert.Null(RoutePrivacy.ForViewer(null, Owner, Viewer));
    }

    [Fact]
    public void Hides_both_zones_and_revisits_without_mutating_recording_or_other_channels()
    {
        var streams = Streams([0, .001, .003, .01, .003, .001, .003, .01, .017, .019, .02]);
        var original = streams.Lat!.ToArray();
        var result = RoutePrivacy.ForViewer(streams, Owner, Viewer)!;
        Assert.Equal(new double?[] { null, null, .003, .01, .003, null, .003, .01, .017, null, null }, result.Lat);
        Assert.Equal(original, streams.Lat);
        Assert.Same(streams.T, result.T);
        Assert.Same(streams.Dist, result.Dist);
        for (var i = 0; i < result.T.Length; i++)
            Assert.Equal(result.Lat![i] is null, result.Lon![i] is null);
    }

    [Fact]
    public void Sparse_segment_crossing_zone_is_broken_even_if_both_points_are_outside()
    {
        var result = RoutePrivacy.ForViewer(Streams([0, .01, -.01, -.02, -.03]), Owner, Viewer)!;
        Assert.Null(result.Lat![1]);
        Assert.Null(result.Lat[2]);
    }

    [Fact]
    public void Radius_is_200_meters_and_visible_points_next_to_hidden_ones_are_kept()
    {
        const double metersPerDegree = 6371000 * Math.PI / 180;
        var streams = Streams([0, 199 / metersPerDegree, 201 / metersPerDegree, .01, .02]);
        var result = RoutePrivacy.ForViewer(streams, Owner, Viewer)!;
        Assert.Null(result.Lat![1]);
        Assert.Equal(streams.Lat![2], result.Lat[2]);
    }

    [Fact]
    public void Loop_and_short_recording_never_reveal_endpoints()
    {
        var loop = RoutePrivacy.ForViewer(Streams([0, .01, .02, .01, 0]), Owner, Viewer)!;
        Assert.Null(loop.Lat![0]);
        Assert.Equal(.02, loop.Lat[2]);
        Assert.Null(loop.Lat[^1]);
        Assert.All(RoutePrivacy.ForViewer(Streams([0, .001]), Owner, Viewer)!.Lat!, Assert.Null);
        Assert.All(RoutePrivacy.ForViewer(Streams([0]), Owner, Viewer)!.Lat!, Assert.Null);
    }

    [Fact]
    public void Missing_invalid_and_unpaired_coordinates_are_hidden_and_not_used_as_centers()
    {
        var streams = Streams([null, double.NaN, 0, .01, .02, 100, .03], [0, 0, 0, 0, 0, 0]);
        var result = RoutePrivacy.ForViewer(streams, Owner, Viewer)!;
        Assert.Equal(new double?[] { null, null, null, .01, null, null, null }, result.Lat);
        Assert.Empty(RoutePrivacy.ForViewer(Streams([]), Owner, Viewer)!.Lat!);
        Assert.All(RoutePrivacy.ForViewer(streams with { Lon = null }, Owner, Viewer)!.Lat!, Assert.Null);
    }

    [Fact]
    public void Distance_wraps_at_the_date_line()
    {
        var result = RoutePrivacy.ForViewer(Streams([0, 0, 0, 0, 0], [179.9999, -179.9999, -179.99, -179.98, -179.97]), Owner, Viewer)!;
        Assert.Null(result.Lat![1]);
        Assert.Equal(0d, result.Lat[3]);
    }
}
