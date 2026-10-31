public static class ActivitySports
{
    private static readonly Dictionary<string, string> StravaNames = new()
    {
        [Normalize("Run")] = "running",
        [Normalize("Trail Run")] = "trail run",
        [Normalize("Ride")] = "cycling",
        [Normalize("Swim")] = "swimming",
        [Normalize("Walk")] = "walking",
        [Normalize("Hike")] = "hiking",
        [Normalize("Mountain Bike Ride")] = "mountain bike ride",
        [Normalize("Gravel Ride")] = "gravel ride",
        [Normalize("E-Bike Ride")] = "e-bike ride",
        [Normalize("E-Mountain Bike Ride")] = "e-mountain bike ride",
        [Normalize("Alpine Ski")] = "alpine ski",
        [Normalize("Badminton")] = "badminton",
        [Normalize("Backcountry Ski")] = "backcountry ski",
        [Normalize("Basketball")] = "basketball",
        [Normalize("Canoe")] = "canoe",
        [Normalize("Canoeing")] = "canoe",
        [Normalize("Crossfit")] = "crossfit",
        [Normalize("Cricket")] = "cricket",
        [Normalize("Dance")] = "dance",
        [Normalize("Elliptical")] = "elliptical",
        [Normalize("Golf")] = "golf",
        [Normalize("Ice Skate")] = "ice skate",
        [Normalize("Inline Skate")] = "inline skate",
        [Normalize("Handcycle")] = "handcycle",
        [Normalize("HIIT")] = "hiit",
        [Normalize("HighIntensityIntervalTraining")] = "hiit",
        [Normalize("Kayaking")] = "kayaking",
        [Normalize("Kayak")] = "kayaking",
        [Normalize("Kitesurf")] = "kitesurf",
        [Normalize("Nordic Ski")] = "nordic ski",
        [Normalize("Padel")] = "padel",
        [Normalize("Pickleball")] = "pickleball",
        [Normalize("Pilates")] = "pilates",
        [Normalize("Physical Therapy")] = "physical therapy",
        [Normalize("Racquetball")] = "racquetball",
        [Normalize("Rock Climb")] = "rock climb",
        [Normalize("RockClimbing")] = "rock climb",
        [Normalize("Roller Ski")] = "roller ski",
        [Normalize("Rowing")] = "rowing",
        [Normalize("Sail")] = "sail",
        [Normalize("Sailing")] = "sail",
        [Normalize("Skateboard")] = "skateboard",
        [Normalize("Snowboard")] = "snowboard",
        [Normalize("Snowshoe")] = "snowshoe",
        [Normalize("Football (Soccer)")] = "soccer",
        [Normalize("Soccer")] = "soccer",
        [Normalize("Squash")] = "squash",
        [Normalize("Stand Up Paddling")] = "stand up paddling",
        [Normalize("Stair-Stepper")] = "stair-stepper",
        [Normalize("Surfing")] = "surfing",
        [Normalize("Surf")] = "surfing",
        [Normalize("Table Tennis")] = "table tennis",
        [Normalize("Tennis")] = "tennis",
        [Normalize("Velomobile")] = "velomobile",
        [Normalize("Virtual Ride")] = "virtual ride",
        [Normalize("Virtual Run")] = "virtual run",
        [Normalize("Virtual Rowing")] = "virtual rowing",
        [Normalize("VirtualRow")] = "virtual rowing",
        [Normalize("Volleyball")] = "volleyball",
        [Normalize("Weight Training")] = "weight training",
        [Normalize("Windsurf")] = "windsurf",
        [Normalize("Wheelchair")] = "wheelchair",
        [Normalize("Workout")] = "workout",
        [Normalize("Yoga")] = "yoga",
        [Normalize("Other")] = "other",
    };

    public static readonly string[] All = StravaNames.Values.Distinct().ToArray();

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    public static string FromStrava(string value) =>
        StravaNames.GetValueOrDefault(Normalize(value), value.ToLowerInvariant());

    public static bool IsRunning(string sport) => sport is "running" or "trail run" or "virtual run";

}
