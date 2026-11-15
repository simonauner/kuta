-- Groups are joined only through an invite link (/app/g/{code}); there is no directory or search. Membership gives no
-- access to activities: the group shows its members and a weekly leaderboard of the totals each member chose to share.
create table groups (
    id          uuid primary key default gen_random_uuid(),
    name        text not null check (length(btrim(name)) between 1 and 100),
    -- Same alphabet as users.share_code (migration 028). Replacing it stops the old link.
    invite_code text not null unique check (invite_code ~ '^[a-hjkmnp-z2-9]{8}$'),
    -- The leaderboard's weeks are counted in this zone, so they are the same for every member.
    time_zone   text not null,
    created_at  timestamptz not null default now()
);
alter table groups enable row level security;
revoke all on groups from anon, authenticated;

-- accepted_at null = a request waiting for an admin. share_totals is the member's own choice per group and off by default.
create table group_members (
    group_id     uuid not null references groups (id) on delete cascade,
    user_id      uuid not null references users (id) on delete cascade,
    role         text not null default 'member' check (role in ('admin', 'member')),
    accepted_at  timestamptz,
    share_totals boolean not null default false,
    created_at   timestamptz not null default now(),
    primary key (group_id, user_id),
    check (role = 'member' or accepted_at is not null)
);
create index group_members_user_idx on group_members (user_id);
alter table group_members enable row level security;
revoke all on group_members from anon, authenticated;

-- A group always has an admin: when the last one leaves (or deletes the account) the longest-standing member becomes
-- admin, and a group without members is deleted. Runs in the database so that account deletion by cascade is covered.
create function keep_group_admin() returns trigger language plpgsql as $$
begin
    -- Serialises concurrent leaves, so two admins leaving at once cannot both see the other one stay.
    perform 1 from groups where id = old.group_id for update;
    if not exists (select 1 from group_members where group_id = old.group_id and accepted_at is not null) then
        delete from groups where id = old.group_id;
    elsif not exists (select 1 from group_members where group_id = old.group_id and role = 'admin') then
        update group_members set role = 'admin'
        where (group_id, user_id) = (select group_id, user_id from group_members
            where group_id = old.group_id and accepted_at is not null order by accepted_at, user_id limit 1);
    end if;
    return null;
end $$;

create trigger group_members_keep_admin
    after delete or update of role on group_members
    for each row when (old.role = 'admin') execute function keep_group_admin();
