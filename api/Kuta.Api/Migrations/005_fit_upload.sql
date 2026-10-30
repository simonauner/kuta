-- FIT-uppladdning. Samma fil två gånger blir samma pass (per användare); råfilen ligger i objektlagring under
-- raw_file_key och kan importeras om när logiken ändras.
alter table activities add column raw_file_sha256 bytea;
create unique index activities_user_file_idx on activities (user_id, raw_file_sha256) where raw_file_sha256 is not null;

-- Puls är hälsodata: sparas bara när användaren uttryckligen har samtyckt (null = inget samtycke).
alter table users add column hr_consent_at timestamptz;
