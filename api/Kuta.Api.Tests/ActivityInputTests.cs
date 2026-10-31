using Xunit;

public class ActivityInputTests
{
    [Theory]
    [InlineData("private", true)]
    [InlineData("followers", true)]
    [InlineData("public", true)]
    [InlineData(null, false)]
    [InlineData("Public", false)]
    [InlineData("", false)]
    public void Visibility_accepts_only_known_levels(string? value, bool valid) =>
        Assert.Equal(valid, ActivityInput.IsValidPrivacy(value));

    static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    static readonly ManualActivityRequest Valid = new("running", "Kvällsrunda", Now.AddHours(-2), 1800, 5000, 40, 150);

    [Fact]
    public void Complete_activity_is_valid_and_optional_values_can_be_left_out()
    {
        Assert.Null(ActivityInput.Validate(Valid, Now));
        Assert.Null(ActivityInput.Validate(Valid with { Sport = "swimming" }, Now));
        Assert.Null(ActivityInput.Validate(Valid with { Title = null, ElevationGainM = null, AvgHr = null, DistanceM = 0 }, Now));
    }

    [Fact]
    public void Title_longer_than_100_characters_is_rejected()
    {
        Assert.Null(ActivityInput.Validate(Valid with { Title = new string('a', 100) }, Now));
        Assert.Equal("invalidTitle", ActivityInput.Validate(Valid with { Title = new string('a', 101) }, Now)?.Code);
    }

    [Fact]
    public void Description_length_is_checked_after_trimming()
    {
        Assert.Null(ActivityInput.Validate(Valid with { Description = " " + new string('a', 10000) + " " }, Now));
        Assert.Equal("invalidDescription", ActivityInput.Validate(Valid with { Description = new string('a', 10001) }, Now)?.Code);
    }

    [Theory, InlineData(null, null), InlineData(" \n ", null), InlineData(" First\nSecond ", "First\nSecond")]
    public void Description_is_optional_and_keeps_internal_line_breaks(string? input, string? expected) =>
        Assert.Equal(expected, ActivityInput.NormalizeDescription(input));

    public static TheoryData<ManualActivityRequest> Invalid => new()
    {
        Valid with { Sport = null },
        Valid with { Sport = "unknown" },
        Valid with { StartedAt = null },
        Valid with { StartedAt = Now.AddDays(2) },
        Valid with { DurationS = 0 },
        Valid with { DistanceM = -1 },
        Valid with { ElevationGainM = -1 },
        Valid with { AvgHr = 251 },
    };

    [Theory, MemberData(nameof(Invalid))]
    public void Missing_or_impossible_values_are_rejected(ManualActivityRequest request) =>
        Assert.Equal("invalidActivity", ActivityInput.Validate(request, Now)?.Code);

    [Fact]
    public void Tags_are_optional_known_keys_and_normalized_as_a_set()
    {
        Assert.Null(ActivityInput.ValidateTags(null));
        Assert.Null(ActivityInput.ValidateTags([]));
        Assert.Null(ActivityInput.Validate(Valid with { Tags = ActivityInput.Tags }, Now));
        Assert.Equal(new[] { "race", "workout" }, ActivityInput.NormalizeTags(["race", "workout", "race"]));
        Assert.Empty(ActivityInput.NormalizeTags(null));
    }

    [Theory, InlineData("unknown"), InlineData("Race"), InlineData(""), InlineData(null)]
    public void Unknown_tag_keys_are_rejected(string? tag) =>
        Assert.Equal("invalidTags", ActivityInput.Validate(Valid with { Tags = [tag!] }, Now)?.Code);

    [Theory, InlineData(null, null), InlineData("  ", null), InlineData(" Lopp ", "Lopp")]
    public void Blank_title_means_no_custom_title(string? input, string? expected) =>
        Assert.Equal(expected, ActivityInput.NormalizeTitle(input));
}
