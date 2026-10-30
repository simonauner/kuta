-- Inloggning sköts av Supabase Auth: users blir en profiltabell med samma id som auth.users.
alter table users drop column password_hash;
alter table users drop column email;  -- finns i auth.users och JWT:n; en kopia här blir inaktuell
alter table users alter column id drop default;
alter table users add foreign key (id) references auth.users(id) on delete cascade;

-- Webben pratar bara med vårt API. Stäng ute Supabases Data API (rollerna anon/authenticated)
-- från public, även för tabeller som skapas i senare migreringar.
revoke all on all tables in schema public from anon, authenticated;
revoke all on all sequences in schema public from anon, authenticated;
alter default privileges in schema public revoke all on tables from anon, authenticated;
alter default privileges in schema public revoke all on sequences from anon, authenticated;
