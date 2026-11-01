-- Garmin Connect connection per user. Tokens are encrypted with Data Protection before they are stored.
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

-- Activity files Garmin has announced that Kuta has not downloaded yet. The callback URL is valid for 24 hours and
-- the row is removed once the file is downloaded or can no longer be downloaded.
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

-- The privacy policy promises Garmin data is shown only to its owner for now (Garmin approves showing it to others).
update activities set privacy = 'private' where source = 'garmin';
alter table activities add constraint activities_garmin_private check (source <> 'garmin' or privacy = 'private');
