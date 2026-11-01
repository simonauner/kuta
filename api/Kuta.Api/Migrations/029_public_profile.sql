-- Opt-in: visitors without an account may see the runner's name and public activities via a shared link.
alter table users add column public_profile boolean not null default false;
