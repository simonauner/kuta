-- The owner can keep an activity out of "ran together": it is then neither linked to others nor others to it.
alter table activities add column hide_companions boolean not null default false;
-- Candidates for "ran together" are other users' activities that overlap in time.
create index activities_started_idx on activities (started_at);
