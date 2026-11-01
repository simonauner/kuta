create extension if not exists pg_cron with schema pg_catalog;

-- En session slutar gälla vid den första av utloggning/återkallelse och absolut utgång.
create index user_sessions_retention_idx on public.user_sessions (least(ended_at, expires_at));

-- Kör i databasen även när API-maskinen sover. Kommandot innehåller inga användarvärden.
select cron.schedule('kuta-session-retention', '0 * * * *',
    'delete from public.user_sessions
     where least(ended_at, expires_at) <= now() - interval ''30 days'';');
