public static class Supporters
{
    // Active supporter tier for the users row u, as seen by @userId with the same read rule as activities; otherwise null.
    public static readonly string VisibleTier = $"""
        case when u.supporter_until > now() and (u.id = @userId or {SocialEndpoints.NotBlocked("u.id", "@userId")} and
            (u.supporter_badge_privacy = 'public' or (u.supporter_badge_privacy = 'followers' and {SocialEndpoints.FollowedBy("@userId", "u.id")})))
        then u.supporter_tier end
        """;
}
