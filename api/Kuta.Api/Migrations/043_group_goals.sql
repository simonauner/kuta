-- Shared, recurring goals of a group (at most Groups.MaxGoals), set by its admins. Progress counts the same totals as
-- the weekly leaderboard: only members who chose to share them (group_members.share_totals) and never Garmin activities.
create table group_goals (
    id          uuid primary key default gen_random_uuid(),
    group_id    uuid not null references groups (id) on delete cascade,
    sport_group text, -- key from SportGroups.All; null = all sports
    metric      text not null check (metric in ('distance', 'duration', 'count')),
    period      text not null check (period in ('week', 'month', 'year')),
    target      double precision not null check (target > 0 and target < 'Infinity'::float8),
    -- Local date in the group's time zone when the goal was set; it counts from the start of that period.
    starts_on   date not null,
    created_at  timestamptz not null default now()
);
create index group_goals_group_idx on group_goals (group_id, created_at);
alter table group_goals enable row level security;
revoke all on group_goals from anon, authenticated;
