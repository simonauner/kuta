// Garmin kräver "Garmin [klockmodell]" överallt där data från Garmin-API:t visas. Klockan är privat,
// så andra ser bara "Garmin" (Garmins regel när modellen saknas, liksom deviceName 'unknown').
public static class GarminAttribution
{
    public static string Sql(string a) => $"""
        case when {a}.source = 'garmin' then
            case when {a}.user_id = @userId and {a}.device is not null and {a}.device <> 'unknown'
                then case when {a}.device ilike 'garmin%' then {a}.device else 'Garmin ' || {a}.device end
                else 'Garmin' end
        end
        """;
}
