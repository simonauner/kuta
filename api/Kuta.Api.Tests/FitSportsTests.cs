using Dynastream.Fit;
using Xunit;

public class FitSportsTests
{
    // Garmin reports gym activities as Training and puts the kind in the sub sport; the raw name "training" is not a sport of ours.
    [Theory]
    [InlineData(Sport.Training, SubSport.StrengthTraining, "weight training")]
    [InlineData(Sport.Training, SubSport.Generic, "workout")]
    [InlineData(Sport.FitnessEquipment, SubSport.Hiit, "hiit")]
    [InlineData(Sport.FitnessEquipment, SubSport.Treadmill, "running")]
    [InlineData(Sport.Running, SubSport.Trail, "trail run")]
    [InlineData(Sport.Cycling, SubSport.GravelCycling, "gravel ride")]
    [InlineData(Sport.CrossCountrySkiing, SubSport.SkateSkiing, "nordic ski")]
    [InlineData(Sport.Racket, SubSport.Padel, "padel")]
    [InlineData(Sport.Generic, SubSport.Generic, "other")]
    [InlineData(Sport.Flying, SubSport.Generic, "other")]
    public void Sport_and_sub_sport_map_to_our_sports(Sport sport, SubSport sub, string expected) =>
        Assert.Equal(expected, FitSports.From(sport, sub));

    [Fact]
    public void Every_fit_sport_maps_to_a_known_sport_and_parts_to_the_split_editors_sports()
    {
        foreach (var sport in Enum.GetValues<Sport>())
            foreach (var sub in Enum.GetValues<SubSport>().Cast<SubSport?>().Append(null))
            {
                var mapped = FitSports.From(sport, sub);
                Assert.True(mapped == "transition" || ActivitySports.All.Contains(mapped), $"{sport}/{sub} → {mapped}");
                Assert.Contains(FitSports.Part(mapped), Multisport.Sports);
            }
    }
}
