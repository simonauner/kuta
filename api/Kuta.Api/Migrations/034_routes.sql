-- A route is a group of the owner's activities on the same course. The first activity's line is copied as the route's
-- line and never changes, so later activities are matched against a stable reference. Assignment happens in the API
-- (Routes.AssignPendingAsync); an activity with a GPS route and no route_id is pending.
create table routes (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid not null references users (id) on delete cascade,
    sport_group text not null,
    route       geography(LineString, 4326) not null,
    length_m    double precision not null,
    unique (id, user_id)
);
create index routes_route_gix on routes using gist (route);
create index routes_user_idx on routes (user_id, sport_group);

alter table activities add column route_id uuid,
    add foreign key (route_id, user_id) references routes (id, user_id) on delete set null (route_id);
create index activities_route_id_idx on activities (route_id);
create index activities_route_pending_idx on activities (user_id) where route is not null and route_id is null;

-- A route without activities is deleted, so deleting activities leaves no copy of their line behind.
create function delete_unused_route() returns trigger language plpgsql as $$
begin
    delete from routes r where r.id = old.route_id
        and not exists (select 1 from activities a where a.route_id = old.route_id);
    return null;
end $$;

create trigger activities_delete_unused_route
    after delete or update of route_id on activities
    for each row when (old.route_id is not null) execute function delete_unused_route();
