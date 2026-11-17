-- Web push subscriptions, one per browser. Each is tied to the sign-in session that created it, so signing out or an
-- ended session stops notifications to that device.
create table push_subscriptions (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references users(id) on delete cascade,
    session_id uuid not null references user_sessions(id) on delete cascade,
    endpoint text not null unique check (length(endpoint) <= 2048),
    p256dh text not null,
    auth text not null,
    locale text not null check (locale in ('sv', 'en')),
    created_at timestamptz not null default now()
);
create index push_subscriptions_user_idx on push_subscriptions (user_id);
alter table push_subscriptions enable row level security;
revoke all on push_subscriptions from anon, authenticated;

-- Whether the push job has handled a request (for the person asked) and an approval (for the requester). Existing
-- follows count as handled; new requests start unhandled, and approval clears accept_notified.
alter table follows
    add column request_notified boolean not null default true,
    add column accept_notified boolean not null default true;
alter table follows alter column request_notified set default false;

alter table jobs drop constraint jobs_kind_check;
alter table jobs add constraint jobs_kind_check
    check (kind in ('strava_import', 'recalculate', 'garmin_import', 'routes', 'push'));
