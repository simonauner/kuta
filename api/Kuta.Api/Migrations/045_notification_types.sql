-- One cursor per user replaces the per-row flags on follows: the push job sends events newer than it and moves it
-- forward, which also covers events with several recipients (every admin of a group). push_disabled lists the
-- notification types the user has turned off, for all devices.
alter table users
    add column push_notified_until timestamptz not null default now(),
    add column push_disabled text[] not null default '{}'
        check (push_disabled <@ array['followRequest', 'followAccepted', 'groupJoinRequest', 'groupJoinAccepted']);
alter table follows drop column request_notified, drop column accept_notified;
