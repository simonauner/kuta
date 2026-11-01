-- Hemligt slumpvärde per användare som flyttar de dolda zonernas mittpunkter i delade kartminiatyrer.
-- Utan förskjutning ligger varje klippunkt på en känd cirkel runt den hemliga punkten, som då kan räknas fram.
-- Skickas aldrig till webben eller i exporten.
alter table users add column privacy_seed uuid not null default gen_random_uuid();
