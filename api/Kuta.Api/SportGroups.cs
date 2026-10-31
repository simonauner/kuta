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
    // SQL: CTE:n effective (kräver Cte och @userId) har en rad per eget pass (part = false) och en rad per
    // annan sportgrupp i ett multisportpass (part = true). Distansen följer den gällande uppdelningen.
    // Filtrera med "not part" för alla sporter och "sport_group = …" för en grupp; count(distinct id) räknar pass.
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
