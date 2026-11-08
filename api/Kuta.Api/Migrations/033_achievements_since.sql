-- Achievements reached before this point count as seen, so the teaser only celebrates what happens after the feature
-- launched (existing users) or the profile was created (new users) – not old history or an imported archive.
alter table users add column achievements_since timestamptz not null default now();
