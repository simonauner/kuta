-- Användarens egen arkivexport, utan anslutning till Stravas API.
alter table activities drop constraint activities_source_check;
alter table activities add constraint activities_source_check check (source in ('fit', 'manual', 'strava'));
alter table activities add column strava_activity_id text;
create unique index activities_user_strava_idx on activities (user_id, strava_activity_id)
    where strava_activity_id is not null;
