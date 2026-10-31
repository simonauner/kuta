-- Synlighet per mål med samma val och läsregel som passen; nya mål är privata.
alter table goals add column privacy text not null default 'private'
    check (privacy in ('private', 'followers', 'public'));
