-- Energy expenditure in kcal as the watch or Strava reports it. Derived from heart rate, so it follows the heart rate
-- consent and is shown only to the owner.
alter table activities add column calories integer check (calories between 0 and 50000);
