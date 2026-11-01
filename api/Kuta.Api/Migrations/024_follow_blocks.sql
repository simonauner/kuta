-- Blockerade kan inte be om att följa igen. Blockeringen syns inte för den blockerade.
create table follow_blocks (
    blocker_id uuid not null references users(id) on delete cascade,
    blocked_id uuid not null references users(id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (blocker_id, blocked_id),
    check (blocker_id <> blocked_id)
);
alter table follow_blocks enable row level security;
revoke all on follow_blocks from anon, authenticated;
