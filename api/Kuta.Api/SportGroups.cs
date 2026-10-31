public record SportGroup(string Key, string[] Sports);

// Sporter som räknas tillsammans i mål och statistik. Nycklarna är egna gruppnamn; sporter utan grupp är sin egen grupp.
// Garmin anger terräng och öppet vatten som undertyper, så FIT-pass har redan "running" och "swimming";
// de övriga namnen kommer från Strava och från Garmins sportnamn (Sport-enumen i gemener).
public static class SportGroups
{
    public static readonly SportGroup[] Named =
    [
        new("running", ["running", "trail run", "virtual run"]),
        new("cycling", ["cycling", "mountain bike ride", "gravel ride", "e-bike ride", "e-mountain bike ride",
            "virtual ride", "velomobile", "handcycle", "ebiking"]),
        new("swimming", ["swimming"]),
        new("walking", ["walking", "hiking", "snowshoe"]),
        new("cross-country skiing", ["nordic ski", "backcountry ski", "roller ski", "crosscountryskiing"]),
        new("paddling", ["canoe", "kayaking", "stand up paddling", "paddling", "standuppaddleboarding"]),
        new("rowing", ["rowing", "virtual rowing"]),
    ];

    static readonly Dictionary<string, string> GroupBySport =
        Named.SelectMany(g => g.Sports.Select(sport => (sport, g.Key))).ToDictionary(x => x.sport, x => x.Key);

    public static string GroupOf(string sport) => GroupBySport.GetValueOrDefault(sport, sport);

    // Valbara grupper: de namngivna först, sedan övriga sporter som egna grupper.
    public static readonly SportGroup[] All =
    [
        .. Named,
        .. ActivityInput.Sports.Where(sport => !GroupBySport.ContainsKey(sport) && Named.All(g => g.Key != sport))
            .Select(sport => new SportGroup(sport, [sport])),
    ];

    public static bool IsValid(string? key) => key is not null && All.Any(g => g.Key == key);

    // SQL: CTE:n sport_groups mappar sport → grupp; omappade sporter är sin egen grupp via coalesce(sg.sport_group, sport).
    public const string Cte = "sport_groups as (select * from unnest(@groupedSports::text[], @sportGroupKeys::text[]) as m(sport, sport_group))";
    public static readonly string[] GroupedSports = Named.SelectMany(g => g.Sports).ToArray();
    public static readonly string[] GroupKeys = Named.SelectMany(g => g.Sports.Select(_ => g.Key)).ToArray();
}
