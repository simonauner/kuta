-- Garmin Connect-anslutning per användare. Tokens krypteras med Data Protection innan de sparas.
create table garmin_connections (
    user_id            uuid primary key references users(id) on delete cascade,
    garmin_user_id     text not null unique,
    access_token       text not null,
    refresh_token      text not null,
    access_expires_at  timestamptz not null,
    refresh_expires_at timestamptz not null,
    permissions        text[] not null default '{}',
    created_at         timestamptz not null default now()
);

-- Aktivitetsfiler som Garmin aviserat men Kuta inte hämtat än. Callback-URL:en gäller 24 timmar och raden tas
-- bort när filen hämtats eller inte längre går att hämta.
create table garmin_files (
    summary_id           text primary key,
    user_id              uuid not null references users(id) on delete cascade,
    callback_url         text not null,
    file_type            text not null,
    device_name          text,
    activity_name        text,
    activity_description text,
    received_at          timestamptz not null default now()
);
create index garmin_files_user_idx on garmin_files (user_id, received_at);

alter table garmin_connections enable row level security;
alter table garmin_files enable row level security;
revoke all on garmin_connections, garmin_files from anon, authenticated;

alter table jobs drop constraint jobs_kind_check;
alter table jobs add constraint jobs_kind_check check (kind in ('strava_import', 'recalculate', 'garmin_import'));

-- Integritetspolicyn lovar att Garmin-data tills vidare bara visas för ägaren (Garmin godkänner visning för andra).
update activities set privacy = 'private' where source = 'garmin';
alter table activities add constraint activities_garmin_private check (source <> 'garmin' or privacy = 'private');
