-- A short, random code for the profile link. The owner can replace it to stop a link that spread too far; the code is
-- created on first use, so existing rows have none until then. The alphabet leaves out look-alikes (0/o, 1/l/i).
alter table users add column share_code text unique check (share_code ~ '^[a-hjkmnp-z2-9]{8}$');
