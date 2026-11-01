using Xunit;

public class DisplayNameTests
{
    [Theory]
    [InlineData("  Anna   Svensson ", "Anna Svensson")]
    [InlineData("Anna\nSvensson", "Anna Svensson")]
    [InlineData("Ａｎｎａ", "Anna")] // full-width letters fold to the ordinary ones
    [InlineData("Åsa Öberg 🏃", "Åsa Öberg 🏃")]
    [InlineData("José", "José")]
    public void Names_are_normalized(string name, string expected) =>
        Assert.Equal(expected, MeEndpoints.NormalizeDisplayName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("An\u200Bna")] // zero-width space
    [InlineData("Anna\u202Eanna")] // right-to-left override
    [InlineData("Anna ✓")]
    [InlineData("🥇 Anna")]
    [InlineData("\uD800Anna")] // lone surrogate
    public void Invisible_characters_and_badge_lookalikes_are_rejected(string name) =>
        Assert.Null(MeEndpoints.NormalizeDisplayName(name));

    [Fact]
    public void Names_are_at_most_100_characters()
    {
        Assert.NotNull(MeEndpoints.NormalizeDisplayName(new string('a', 100)));
        Assert.Null(MeEndpoints.NormalizeDisplayName(new string('a', 101)));
    }
}
