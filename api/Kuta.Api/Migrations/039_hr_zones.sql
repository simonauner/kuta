-- Heart rate zone boundaries from the watch (FIT time_in_zone for the session): the upper limit of the range below
-- zone 1 followed by the upper limits of zones 1..n-1; the last zone is open-ended. Health data like the heart rate
-- stream: stored only with consent and cleared when consent is withdrawn.
alter table activities add column hr_zones smallint[]
    check (cardinality(hr_zones) between 2 and 10 and 0 < all(hr_zones) and 300 > all(hr_zones));
