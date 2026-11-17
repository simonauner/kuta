// Garmin's API Brand Guidelines require "Garmin [device model]" wherever data from the Garmin API is shown, for every
// viewer; only "Garmin" when the model is missing or unknown (deviceName 'unknown').
public static class GarminAttribution
{
    public static string Sql(string a) => $"""
        case when {a}.source = 'garmin' then
            case when {a}.device is not null and {a}.device <> 'unknown'
                then case when {a}.device ilike 'garmin%' then {a}.device else 'Garmin ' || {a}.device end
                else 'Garmin' end
        end
        """;
}
