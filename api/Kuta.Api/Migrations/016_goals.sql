create table goals (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references users(id) on delete cascade,
    sport_group text, -- nyckel ur SportGroups.All; null = alla
    metric text not null check (metric in ('distance', 'duration', 'count')),
    period text not null check (period in ('week', 'month', 'year')),
    target double precision not null check (target > 0 and target < 'Infinity'::float8),
    recurring boolean not null,
    -- Lokalt datum då målet skapades; målet räknas från början av perioden som innehåller datumet.
    starts_on date not null,
    created_at timestamptz not null default now()
);
create index goals_user_id_idx on goals (user_id, created_at);
alter table goals enable row level security;
revoke all on goals from anon, authenticated;
