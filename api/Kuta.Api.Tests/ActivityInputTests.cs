using Xunit;

public class ActivityInputTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    static readonly ManualActivityRequest Valid = new("running", "Kvällsrunda", Now.AddHours(-2), 1800, 5000, 40, 150);

    [Fact]
    public void Complete_activity_is_valid_and_optional_values_can_be_left_out()
    {
        Assert.Null(ActivityInput.Validate(Valid, Now));
        Assert.Null(ActivityInput.Validate(Valid with { Title = null, ElevationGainM = null, AvgHr = null, DistanceM = 0 }, Now));
    }

    [Fact]
    public void Title_longer_than_100_characters_is_rejected()
    {
        Assert.Null(ActivityInput.Validate(Valid with { Title = new string('a', 100) }, Now));
        Assert.Equal("invalidTitle", ActivityInput.Validate(Valid with { Title = new string('a', 101) }, Now)?.Code);
    }

    public static TheoryData<ManualActivityRequest> Invalid => new()
    {
        Valid with { Sport = null },
        Valid with { Sport = "swimming" },
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

    [Theory, InlineData(null, null), InlineData("  ", null), InlineData(" Lopp ", "Lopp")]
    public void Blank_title_means_no_custom_title(string? input, string? expected) =>
        Assert.Equal(expected, ActivityInput.NormalizeTitle(input));
}
