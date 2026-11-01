-- Vilken version av användarvillkoren och integritetspolicyn användaren godkände vid registreringen, och när.
-- Kopieras från Supabase-metadata när profilen skapas; null för konton som inte skapats via registreringen.
alter table users
    add column terms_version text,
    add column terms_accepted_at timestamptz;
