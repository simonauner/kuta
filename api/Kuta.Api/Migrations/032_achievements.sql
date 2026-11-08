-- Achievements are evaluated from the activities when read (Achievements.cs); only which ones the user has seen
-- is stored, so the app shows each new one once.
create table achievement_views (
    user_id uuid not null references users(id) on delete cascade,
    key     text not null check (length(key) <= 100),
    seen_at timestamptz not null default now(),
    primary key (user_id, key)
);
