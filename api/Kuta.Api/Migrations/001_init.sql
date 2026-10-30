create extension if not exists postgis;

create table users (
    id           uuid primary key default gen_random_uuid(),
    email        text not null unique,
    display_name text not null,
    created_at   timestamptz not null default now()
);

create table shoes (
    id                uuid primary key default gen_random_uuid(),
    user_id           uuid not null references users(id) on delete cascade,
    name              text not null,
    brand             text,
    initial_distance_m double precision not null default 0,
    retired           boolean not null default false,
    created_at        timestamptz not null default now()
);

create table activities (
    id               uuid primary key default gen_random_uuid(),
    user_id          uuid not null references users(id) on delete cascade,
    source           text not null check (source in ('fit', 'manual')),
    sport            text not null default 'running',
    title            text,
    started_at       timestamptz not null,
    duration_s       integer not null,
    moving_s         integer,
    distance_m       double precision not null,
    avg_hr           smallint,
    max_hr           smallint,
    avg_speed_mps    real,
    elevation_gain_m real,
    shoe_id          uuid references shoes(id) on delete set null,
    route            geography(LineString, 4326),
    raw_file_key     text,                       -- nyckel i objektlagring (t.ex. Cloudflare R2)
    privacy          text not null default 'private'
                     check (privacy in ('private', 'followers', 'public')),
    created_at       timestamptz not null default now()
);

create index activities_user_started_idx on activities (user_id, started_at desc);
create index activities_route_gix on activities using gist (route);

-- En rad per pass med tidsserier som arrayer (inte en rad per sekund).
create table activity_streams (
    activity_id uuid primary key references activities(id) on delete cascade,
    t           integer[]          not null,  -- sekunder från start
    dist        real[],                       -- meter
    hr          smallint[],
    speed       real[],                       -- m/s
    alt         real[],                       -- meter
    lat         double precision[],
    lon         double precision[]
);

-- Bästa insats per standarddistans och pass, förberäknat vid import.
create table best_efforts (
    id            uuid primary key default gen_random_uuid(),
    activity_id   uuid not null references activities(id) on delete cascade,
    user_id       uuid not null references users(id) on delete cascade,
    distance_m    integer not null,           -- 1000, 1609, 5000, 10000, 21097, 42195
    elapsed_s     real not null,
    start_offset_s integer not null,
    achieved_at   timestamptz not null
);

create index best_efforts_pr_idx on best_efforts (user_id, distance_m, elapsed_s);
