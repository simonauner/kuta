using Xunit;

public class RoutePrivacyTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();
    private static PrivacyPlace Place(double lat, double lon = 0, int radius = 200) =>
        new(Guid.NewGuid(), "Synthetic place", lat, lon, radius);
    private static ActivityStreams Streams(double?[] lat, double?[]? lon = null) => new(
        Enumerable.Range(0, lat.Length).ToArray(), lat.Select((_, i) => (float?)(i * 100)).ToArray(),
        null, null, null, lat, lon ?? lat.Select(_ => (double?)0).ToArray(), null, null);

    [Fact]
    public void Owner_and_viewers_without_places_receive_original_streams()
    {
        var streams = Streams([0, .001, .01]);
        Assert.Same(streams, RoutePrivacy.ForViewer(streams, Owner, Owner, [Place(0)]));
        Assert.Same(streams, RoutePrivacy.ForViewer(streams, Owner, Viewer, []));
        Assert.Null(RoutePrivacy.ForViewer(null, Owner, Viewer, [Place(0)]));
    }

    [Fact]
    public void Multiple_places_hide_nearby_points_and_revisits_but_keep_distant_start_and_finish()
    {
        var streams = Streams([-.01, 0, .003, .01, .003, 0, .003, .01, .016, .02, .024, .03, .04]);
        var original = streams.Lat!.ToArray();
        var result = RoutePrivacy.ForViewer(streams, Owner, Viewer, [Place(0), Place(.02, radius: 500)])!;
        Assert.Null(result.Lat![1]);
        Assert.Null(result.Lat[5]);
        Assert.Null(result.Lat[8]);
        Assert.Null(result.Lat[9]);
        Assert.Null(result.Lat[10]);
        Assert.Equal(-.01, result.Lat[0]);
        Assert.Equal(.003, result.Lat[2]);
        Assert.Equal(.04, result.Lat[^1]);
        Assert.Equal(original, streams.Lat);
        Assert.Same(streams.T, result.T);
        Assert.Same(streams.Dist, result.Dist);
        for (var i = 0; i < result.T.Length; i++)
            Assert.Equal(result.Lat[i] is null, result.Lon![i] is null);
    }

    [Fact]
    public void Distant_routes_are_unchanged_and_editing_places_affects_the_same_recording()
    {
        var streams = Streams([0, .01, .02]);
        var distant = RoutePrivacy.ForViewer(streams, Owner, Viewer, [Place(1)])!;
        Assert.Equal(streams.Lat, distant.Lat);
        Assert.Equal(streams.Lon, distant.Lon);
        Assert.Null(RoutePrivacy.ForViewer(streams, Owner, Viewer, [Place(0)])!.Lat![0]);
        var moved = RoutePrivacy.ForViewer(streams, Owner, Viewer, [Place(.02)])!;
        Assert.Equal(0d, moved.Lat![0]);
        Assert.Null(moved.Lat[^1]);
    }

    [Fact]
    public void Sparse_segment_crossing_place_is_broken_even_if_both_points_are_outside()
    {
        var result = RoutePrivacy.ForViewer(Streams([-.01, .01]), Owner, Viewer, [Place(0)])!;
        Assert.All(result.Lat!, Assert.Null);
    }

    [Fact]
    public void Default_radius_is_200_meters_and_visible_points_next_to_hidden_ones_are_kept()
    {
        const double metersPerDegree = 6371000 * Math.PI / 180;
        var streams = Streams([0, 199 / metersPerDegree, 201 / metersPerDegree, .01, .02]);
        var result = RoutePrivacy.ForViewer(streams, Owner, Viewer, [Place(0)])!;
        Assert.Null(result.Lat![1]);
        Assert.Equal(streams.Lat![2], result.Lat[2]);
        Assert.Equal(.02, result.Lat[^1]);
    }

    [Fact]
    public void Short_recording_inside_a_place_has_no_visible_route()
    {
        Assert.All(RoutePrivacy.ForViewer(Streams([0, .001]), Owner, Viewer, [Place(0)])!.Lat!, Assert.Null);
        Assert.All(RoutePrivacy.ForViewer(Streams([0]), Owner, Viewer, [Place(0)])!.Lat!, Assert.Null);
    }

    [Fact]
    public void Missing_invalid_and_unpaired_coordinates_are_hidden()
    {
        var streams = Streams([null, double.NaN, 0, .01, .02, 100, .03], [0, 0, 0, 0, 0, 0]);
        var result = RoutePrivacy.ForViewer(streams, Owner, Viewer, [Place(0), Place(.02)])!;
        Assert.Equal(new double?[] { null, null, null, .01, null, null, null }, result.Lat);
        Assert.Empty(RoutePrivacy.ForViewer(Streams([]), Owner, Viewer, [Place(0)])!.Lat!);
        Assert.All(RoutePrivacy.ForViewer(streams with { Lon = null }, Owner, Viewer, [Place(0)])!.Lat!, Assert.Null);
    }

    [Fact]
    public void Distance_wraps_at_the_date_line()
    {
        var result = RoutePrivacy.ForViewer(Streams([0, 0, 0], [-179.9999, -179.99, -179.98]), Owner, Viewer, [Place(0, 179.9999)])!;
        Assert.Null(result.Lat![0]);
        Assert.Equal(0d, result.Lat[1]);
    }

    [Fact]
    public void Altitude_is_hidden_within_600_m_of_start_and_finish()
    {
        float?[] dist = [0, 600, 601, 1000, 1399, 1400, 2000];
        var streams = new ActivityStreams(Enumerable.Range(0, dist.Length).ToArray(), dist, null, null,
            [10, 11, 12, 13, 14, 15, 16], null, null, null, null);
        Assert.Equal([null, null, 12, 13, 14, null, null], RoutePrivacy.HideEndpointAltitude(streams).Alt);
        Assert.Null(RoutePrivacy.HideEndpointAltitude(streams with { Dist = null }).Alt);
        Assert.Null(RoutePrivacy.HideEndpointAltitude(streams with { Dist = [0, 100, 200, 300, 400, 500, 1000] }).Alt);
    }
}
