alter table activities
    add column original_sport text,
    add column parts_edited boolean not null default false;

-- Original FIT sessions and editable overlays share the same small summary format.
-- Recorded streams and the raw file are never changed by an edit.
create table activity_parts (
    activity_id uuid not null references activities(id) on delete cascade,
    original boolean not null,
    part_index integer not null check (part_index >= 0),
    sport text not null,
    start_s integer not null check (start_s >= 0),
    end_s integer not null check (end_s > start_s),
    distance_m double precision not null check (distance_m >= 0 and distance_m < 'Infinity'::float8),
    use_equipment boolean not null default false,
    primary key (activity_id, original, part_index)
);
alter table activity_parts enable row level security;
revoke all on activity_parts from anon, authenticated;
