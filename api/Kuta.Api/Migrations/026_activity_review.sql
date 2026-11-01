-- Garmin imports arrive without the user's own title, description or equipment, so null marks them as not yet
-- reviewed and the app suggests editing them. Everything else counts as reviewed when it is saved.
alter table activities add column reviewed_at timestamptz default now();
update activities set reviewed_at = created_at;
create index activities_unreviewed_idx on activities (user_id, started_at desc) where reviewed_at is null;
