-- Vem som ser målen på profilen: samma val som passens synlighet, gemensamt för alla mål.
alter table users add column goal_privacy text not null default 'private'
    check (goal_privacy in ('private', 'followers', 'public'));
