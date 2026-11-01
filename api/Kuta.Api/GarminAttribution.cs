// Garmin requires "Garmin [device model]" wherever data from the Garmin API is shown. The watch is private,
// so others see only "Garmin" (Garmin's rule when the model is missing, as for deviceName 'unknown').
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
