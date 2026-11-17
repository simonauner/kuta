-- Optional distance after which the owner is reminded to replace the equipment (e.g. worn-out shoes); null = no reminder.
alter table equipment add column distance_limit_m double precision
    check (distance_limit_m > 0 and distance_limit_m <= 1000000000);
