-- Supporterskap: sätts av betalningsintegrationen (inte byggd än). Nivån avslöjar beloppet, så badgen är privat tills ägaren väljer annat.
alter table users
    add column supporter_tier text check (supporter_tier in ('bronze', 'silver', 'gold')),
    add column supporter_until timestamptz,
    add column supporter_badge_privacy text not null default 'private'
        check (supporter_badge_privacy in ('private', 'followers', 'public'));
