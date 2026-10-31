alter table users add column default_activity_privacy text not null default 'private'
    check (default_activity_privacy in ('private', 'followers', 'public'));
