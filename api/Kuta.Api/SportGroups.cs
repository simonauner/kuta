public record SportGroup(string Key, string[] Sports);

// Sports counted together in goals and statistics. The keys are our own group names; sports without a group are their own group.
// Garmin reports trail and open water as subtypes, so FIT activities already have "running" and "swimming";
// the other names come from Strava and from Garmin's sport names (the Sport enum in lower case).
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

    // Selectable groups: the named ones first, then remaining sports as their own groups.
    public static readonly SportGroup[] All =
    [
        .. Named,
        .. ActivityInput.Sports.Where(sport => !GroupBySport.ContainsKey(sport) && Named.All(g => g.Key != sport))
            .Select(sport => new SportGroup(sport, [sport])),
    ];

    public static bool IsValid(string? key) => key is not null && All.Any(g => g.Key == key);

    // SQL: the sport_groups CTE maps sport → group; unmapped sports are their own group via coalesce(sg.sport_group, sport).
    public const string Cte = "sport_groups as (select * from unnest(@groupedSports::text[], @sportGroupKeys::text[]) as m(sport, sport_group))";
    // SQL: the effective CTE (requires Cte and @userId) has one row per own activity (part = false) and one row per
    // other sport group in a multisport activity (part = true). The distance follows the current split.
    // Filter with "not part" for all sports and "sport_group = …" for one group; count(distinct id) counts activities.
    public const string EffectiveCte = """
        effective as (
            select a.id, a.started_at, coalesce(m.sport_group, a.sport) as sport_group,
                a.duration_s::double precision as duration_s,
                coalesce((select sum(p.distance_m) from activity_parts p
                    where p.activity_id = a.id and p.original = not a.parts_edited), a.distance_m) as distance_m,
                false as part
            from activities a left join sport_groups m on m.sport = a.sport
            where a.user_id = @userId
            union all
            select a.id, a.started_at, coalesce(pm.sport_group, p.sport),
                sum(p.end_s - p.start_s)::double precision, sum(p.distance_m), true
            from activities a
            join activity_parts p on p.activity_id = a.id and p.original = not a.parts_edited
            left join sport_groups am on am.sport = a.sport
            left join sport_groups pm on pm.sport = p.sport
            where a.user_id = @userId and coalesce(pm.sport_group, p.sport) <> coalesce(am.sport_group, a.sport)
            group by a.id, a.started_at, coalesce(pm.sport_group, p.sport)
        )
        """;
    public static readonly string[] GroupedSports = Named.SelectMany(g => g.Sports).ToArray();
    public static readonly string[] GroupKeys = Named.SelectMany(g => g.Sports.Select(_ => g.Key)).ToArray();
}
