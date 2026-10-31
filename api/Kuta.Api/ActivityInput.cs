// Manuellt pass från formuläret "Skapa ny aktivitet". Tider i UTC, SI-enheter.
public record ManualActivityRequest(
    string? Sport, string? Title, DateTimeOffset? StartedAt, int? DurationS, double? DistanceM,
    float? ElevationGainM, short? AvgHr, Guid? ShoeId = null);

// Det löparen själv fyller i, kontrollerat innan det sparas
public static class ActivityInput
{
    public static readonly string[] Sports = ["running", "cycling", "walking", "hiking", "other"];
    const int MaxTitleLength = 100;

    public static ApiError? ValidateTitle(string? title) =>
        title?.Trim().Length > MaxTitleLength ? new ApiError("Titeln får vara högst 100 tecken.", "invalidTitle") : null;

    // Tom titel = ingen egen titel (webben visar sport + tid på dygnet)
    public static string? NormalizeTitle(string? title) => string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    public static ApiError? ValidateSport(string? sport) =>
        sport is null || Sports.Contains(sport) ? null : InvalidActivity;

    public static ApiError? Validate(ManualActivityRequest r, DateTimeOffset now) =>
        ValidateTitle(r.Title)
        ?? (r.Sport is null ? InvalidActivity : ValidateSport(r.Sport))
        ?? (r.StartedAt is not { } startedAt || startedAt.Year < 1900 || startedAt > now.AddDays(1) ? InvalidActivity : null)
        ?? (r.DurationS is not (> 0 and <= 7 * 24 * 3600) ? InvalidActivity : null)
        ?? (r.DistanceM is not (>= 0 and <= 1_000_000) ? InvalidActivity : null)
        ?? (r.ElevationGainM is < 0 or > 20_000 ? InvalidActivity : null)
        ?? (r.AvgHr is < 30 or > 250 ? InvalidActivity : null);

    static readonly ApiError InvalidActivity = new("Kontrollera sport, datum, tid och distans.", "invalidActivity");
}
