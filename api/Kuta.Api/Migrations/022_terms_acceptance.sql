-- När användaren godkände användarvillkoren och integritetspolicyn vid registreringen.
-- Kopieras från Supabase-metadata när profilen skapas; null för konton som inte skapats via registreringen.
alter table users add column terms_accepted_at timestamptz;
