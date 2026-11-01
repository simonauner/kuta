-- Pass som levereras via Garmin Connect Developer Program; källan styr Garmins krav på märkning.
alter table activities drop constraint activities_source_check;
alter table activities add constraint activities_source_check check (source in ('fit', 'manual', 'strava', 'garmin'));
