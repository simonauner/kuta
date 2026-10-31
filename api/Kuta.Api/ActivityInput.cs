// Manuellt pass från formuläret "Skapa ny aktivitet". Tider i UTC, SI-enheter.
public record ManualActivityRequest(
    string? Sport, string? Title, DateTimeOffset? StartedAt, int? DurationS, double? DistanceM,
    float? ElevationGainM, short? AvgHr, string[]? Tags = null, Guid[]? EquipmentIds = null, string? Description = null);

// Det löparen själv fyller i, kontrollerat innan det sparas
public static class ActivityInput
{
    public static readonly string[] Sports = ["running", "cycling", "walking", "hiking", "other"];
    public static readonly string[] Tags = ["race", "workout", "long_run", "easy"];

    public static ApiError? ValidateTags(string[]? tags) =>
        tags is null || tags.All(tag => Tags.Contains(tag)) ? null
            : new ApiError("Välj giltiga taggar.", "invalidTags");

    public static string[] NormalizeTags(string[]? tags) => tags?.Distinct().ToArray() ?? [];

    public const int MaxDescriptionLength = 10_000;

    public static ApiError? ValidateDescription(string? description) =>
        description?.Trim().Length > MaxDescriptionLength
            ? new ApiError("Beskrivningen får vara högst 10 000 tecken.", "invalidDescription") : null;

    public static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    const int MaxTitleLength = 100;

    public static ApiError? ValidateTitle(string? title) =>
        title?.Trim().Length > MaxTitleLength ? new ApiError("Titeln får vara högst 100 tecken.", "invalidTitle") : null;

    // Tom titel = ingen egen titel (webben visar sport + tid på dygnet)
    public static string? NormalizeTitle(string? title) => string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    public static ApiError? ValidateSport(string? sport) =>
        sport is null || Sports.Contains(sport) ? null : InvalidActivity;

    public static ApiError? Validate(ManualActivityRequest r, DateTimeOffset now) =>
        ValidateTitle(r.Title)
        ?? ValidateDescription(r.Description)
        ?? ValidateTags(r.Tags)
        ?? (r.Sport is null ? InvalidActivity : ValidateSport(r.Sport))
        ?? (r.StartedAt is not { } startedAt || startedAt.Year < 1900 || startedAt > now.AddDays(1) ? InvalidActivity : null)
        ?? (r.DurationS is not (> 0 and <= 7 * 24 * 3600) ? InvalidActivity : null)
        ?? (r.DistanceM is not (>= 0 and <= 1_000_000) ? InvalidActivity : null)
        ?? (r.ElevationGainM is < 0 or > 20_000 ? InvalidActivity : null)
        ?? (r.AvgHr is < 30 or > 250 ? InvalidActivity : null);

    static readonly ApiError InvalidActivity = new("Kontrollera sport, datum, tid och distans.", "invalidActivity");
}
