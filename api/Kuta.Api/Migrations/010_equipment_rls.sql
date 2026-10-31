-- Data API grants remain revoked; RLS also protects the renamed equipment table.
alter table equipment enable row level security;
