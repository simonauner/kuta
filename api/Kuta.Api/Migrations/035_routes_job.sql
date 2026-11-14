-- Routes are assigned by a queued job, one activity per transaction (Routes.RunJobAsync).
alter table jobs drop constraint jobs_kind_check;
alter table jobs add constraint jobs_kind_check check (kind in ('strava_import', 'recalculate', 'garmin_import', 'routes'));
