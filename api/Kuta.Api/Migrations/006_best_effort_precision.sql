-- Interpolerade fönster kan börja mellan två sekunder; behåll precisionen även vid lagring.
alter table best_efforts alter column start_offset_s type double precision;
alter table best_efforts alter column elapsed_s type double precision;
