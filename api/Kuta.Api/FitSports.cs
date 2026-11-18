using Dynastream.Fit;

// Maps the FIT file's sport and sub sport to our sport keys (ActivitySports.All). Garmin reports gym activities as
// Training or FitnessEquipment and puts the kind (strength, HIIT, yoga…) in the sub sport. Unknown sports become "other".
public static class FitSports
{
    public static string From(Sport? sport, SubSport? sub) => sport switch
    {
        null => "running",
        Sport.Running => sub switch
        {
            SubSport.Trail => "trail run",
            SubSport.VirtualActivity => "virtual run",
            _ => "running",
        },
        Sport.Cycling => sub switch
        {
            SubSport.Mountain or SubSport.Downhill => "mountain bike ride",
            SubSport.GravelCycling or SubSport.Cyclocross => "gravel ride",
            SubSport.EBikeMountain => "e-mountain bike ride",
            SubSport.VirtualActivity => "virtual ride",
            SubSport.HandCycling or SubSport.IndoorHandCycling => "handcycle",
            _ => "cycling",
        },
        Sport.EBiking => sub is SubSport.Mountain or SubSport.EBikeMountain ? "e-mountain bike ride" : "e-bike ride",
        Sport.Transition => "transition",
        Sport.Swimming => "swimming",
        Sport.Walking => "walking",
        Sport.Hiking or Sport.Mountaineering => "hiking",
        Sport.Snowshoeing => "snowshoe",
        Sport.CrossCountrySkiing => sub == SubSport.Backcountry ? "backcountry ski" : "nordic ski",
        Sport.AlpineSkiing => sub == SubSport.Backcountry ? "backcountry ski" : "alpine ski",
        Sport.Snowboarding => "snowboard",
        Sport.Rowing => sub == SubSport.VirtualActivity ? "virtual rowing" : "rowing",
        // "Kanot" is also the Swedish umbrella term for paddling.
        Sport.Paddling or Sport.Canoeing => "canoe",
        Sport.Kayaking => "kayaking",
        Sport.StandUpPaddleboarding => "stand up paddling",
        Sport.Tennis or Sport.Racket => sub switch
        {
            SubSport.Pickleball => "pickleball",
            SubSport.Padel => "padel",
            SubSport.Squash => "squash",
            SubSport.Badminton => "badminton",
            SubSport.Racquetball => "racquetball",
            SubSport.TableTennis => "table tennis",
            _ => sport == Sport.Tennis ? "tennis" : "other",
        },
        Sport.Basketball => "basketball",
        Sport.Soccer => "soccer",
        Sport.Volleyball => "volleyball",
        Sport.Cricket => "cricket",
        Sport.Golf => "golf",
        Sport.Dance => "dance",
        Sport.InlineSkating => "inline skate",
        Sport.IceSkating => "ice skate",
        Sport.RockClimbing => "rock climb",
        Sport.Sailing => "sail",
        Sport.Surfing => "surfing",
        Sport.Windsurfing => "windsurf",
        Sport.Kitesurfing => "kitesurf",
        Sport.WheelchairPushWalk or Sport.WheelchairPushRun => "wheelchair",
        Sport.Hiit => "hiit",
        Sport.Training or Sport.FitnessEquipment or Sport.Generic => Gym(sub) ?? (sport == Sport.Generic ? "other" : "workout"),
        _ => "other",
    };

    static string? Gym(SubSport? sub) => sub switch
    {
        SubSport.StrengthTraining => "weight training",
        SubSport.Hiit or SubSport.Amrap or SubSport.Emom or SubSport.Tabata => "hiit",
        SubSport.Yoga => "yoga",
        SubSport.Pilates => "pilates",
        SubSport.CardioTraining or SubSport.FlexibilityTraining => "workout",
        SubSport.Elliptical => "elliptical",
        SubSport.StairClimbing => "stair-stepper",
        SubSport.IndoorRowing => "rowing",
        SubSport.Treadmill or SubSport.IndoorRunning => "running",
        SubSport.IndoorCycling or SubSport.Spin => "cycling",
        SubSport.IndoorWalking => "walking",
        _ => null,
    };

    // A part of a multisport activity uses the coarse sports that the split editor accepts (Multisport.Sports).
    public static string Part(string sport) => SportGroups.GroupOf(sport) switch
    {
        "running" => "running",
        "cycling" => "cycling",
        _ when Multisport.Sports.Contains(sport) => sport,
        _ => "other",
    };
}
