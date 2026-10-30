-- Inloggningssessioner: cookien pekar på en rad här, så utloggning slår igenom direkt
-- (även om Supabase inte svarar). Raden finns före profilen, därav FK mot auth.users.
create table user_sessions (
    id         uuid primary key default gen_random_uuid(),
    user_id    uuid not null references auth.users(id) on delete cascade,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    ended_at   timestamptz
);

create index user_sessions_user_idx on user_sessions (user_id) where ended_at is null;

-- Data Protection-nycklar (krypterar cookien och antiforgery-token) så att sessioner överlever omstarter.
create table data_protection_keys (
    id            serial primary key,
    friendly_name text not null,
    xml           text not null
);
