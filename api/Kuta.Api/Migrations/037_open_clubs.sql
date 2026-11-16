-- An admin can make a group an open club: its name, place, sport and description become searchable for everyone signed
-- in. Joining still needs an admin's approval, and members and the leaderboard stay visible to members only, so a club
-- never becomes a directory of people. name_skeleton is MeEndpoints.NameSkeleton(name), set by the API with the name.
alter table groups
    add column visibility    text not null default 'private' check (visibility in ('private', 'open')),
    add column description   text check (length(description) between 1 and 500),
    add column place         text check (length(btrim(place)) between 1 and 100),
    add column sport_group   text,
    add column name_skeleton text,
    add check (visibility = 'private' or name_skeleton is not null);

-- shortcut: search is a substring match over the open clubs, a scan of this partial index; add pg_trgm if they grow to many thousands.
create index groups_open_name_idx on groups (name_skeleton) where visibility = 'open';
