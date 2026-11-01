public static class Supporters
{
    // Aktiv supporternivå för users-raden u, sedd av @userId med samma läsregel som passen; annars null.
    public const string VisibleTier = """
        case when u.supporter_until > now() and (u.id = @userId or u.supporter_badge_privacy = 'public' or
            (u.supporter_badge_privacy = 'followers' and exists (select 1 from follows f
             where f.follower_id = @userId and f.following_id = u.id and f.accepted_at is not null)))
        then u.supporter_tier end
        """;
}
