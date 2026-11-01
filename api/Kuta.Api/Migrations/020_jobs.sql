-- Postgres-baserad jobbkö för långa jobb (Strava-import, omberäkning). Raden innehåller bara id:n, status,
-- förlopp, resultat och en stabil felkod – aldrig filinnehåll eller undantagstexter. Kaskadraderas med kontot.
create table jobs (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references users(id) on delete cascade,
    kind text not null check (kind in ('strava_import', 'recalculate')),
    status text not null default 'queued' check (status in ('queued', 'running', 'succeeded', 'failed')),
    progress jsonb,
    result jsonb,
    error_code text,
    attempts integer not null default 0,
    run_after timestamptz not null default now(),
    locked_until timestamptz,
    created_at timestamptz not null default now(),
    finished_at timestamptz
);
-- Högst ett aktivt jobb av varje slag per användare.
create unique index jobs_active_idx on jobs (user_id, kind) where status in ('queued', 'running');
create index jobs_pending_idx on jobs (run_after) where status in ('queued', 'running');
create index jobs_user_idx on jobs (user_id, kind, created_at desc);
alter table jobs enable row level security;
revoke all on jobs from anon, authenticated;

-- Befintliga pass räknas om en gång, så att löpintensitet (med backjustering) och rekord följer nuvarande logik.
insert into jobs (user_id, kind)
select distinct a.user_id, 'recalculate'
from activities a join activity_streams s on s.activity_id = a.id
where s.dist is not null;
