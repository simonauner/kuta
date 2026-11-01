public static class Supporters
{
    // Active supporter tier for the users row u, as seen by @userId with the same read rule as activities; otherwise null.
    public const string VisibleTier = """
        case when u.supporter_until > now() and (u.id = @userId or u.supporter_badge_privacy = 'public' or
            (u.supporter_badge_privacy = 'followers' and exists (select 1 from follows f
             where f.follower_id = @userId and f.following_id = u.id and f.accepted_at is not null)))
        then u.supporter_tier end
        """;
}
