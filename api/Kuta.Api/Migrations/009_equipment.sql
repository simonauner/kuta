alter table shoes rename to equipment;
alter table equipment
    add column kind text not null default 'shoe',
    add column initial_duration_s bigint not null default 0 check (initial_duration_s >= 0),
    add constraint equipment_initial_distance_check check (initial_distance_m >= 0 and initial_distance_m < 'Infinity'::float8),
    add constraint equipment_id_user_key unique (id, user_id);
create index equipment_user_idx on equipment (user_id);

alter table activities add constraint activities_id_user_key unique (id, user_id);
create table activity_equipment (
    activity_id uuid not null,
    equipment_id uuid not null,
    user_id uuid not null,
    primary key (activity_id, equipment_id),
    foreign key (activity_id, user_id) references activities (id, user_id) on delete cascade,
    foreign key (equipment_id, user_id) references equipment (id, user_id) on delete cascade
);
create index activity_equipment_equipment_idx on activity_equipment (equipment_id, user_id);
alter table activity_equipment enable row level security;

insert into activity_equipment (activity_id, equipment_id, user_id)
select id, shoe_id, user_id from activities where shoe_id is not null;
alter table activities drop column shoe_id;
