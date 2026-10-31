create table privacy_places (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references users(id) on delete cascade,
    name text not null check (length(btrim(name)) between 1 and 100),
    latitude double precision not null check (latitude between -90 and 90),
    longitude double precision not null check (longitude between -180 and 180),
    radius_m integer not null default 200 check (radius_m between 50 and 5000)
);
create index privacy_places_user_id_idx on privacy_places(user_id);
alter table privacy_places enable row level security;
revoke all on privacy_places from anon, authenticated;
