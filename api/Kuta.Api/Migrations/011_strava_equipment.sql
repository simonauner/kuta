-- Stabil koppling till Stravas sko-id; namn kan ändras utan att skapa en ny sko.
alter table equipment add column strava_gear_id text;
create unique index equipment_user_strava_idx on equipment (user_id, strava_gear_id)
    where strava_gear_id is not null;
