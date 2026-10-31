create table follows (
    follower_id uuid not null references users(id) on delete cascade,
    following_id uuid not null references users(id) on delete cascade,
    accepted_at timestamptz,
    created_at timestamptz not null default now(),
    primary key (follower_id, following_id),
    check (follower_id <> following_id)
);
create index follows_following_idx on follows (following_id, follower_id);
alter table follows enable row level security;
revoke all on follows from anon, authenticated;
