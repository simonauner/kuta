alter table activities add column tags text[] not null default '{}';
create index activities_tags_idx on activities using gin (tags);
