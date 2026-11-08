-- Aerobic efficiency of steady runs (AerobicEfficiency.cs): grade-adjusted average speed and time-weighted heart
-- rate after the warm-up, calculated with the records. Derived from heart rate, so cleared with the consent.
alter table activities add column aerobic_speed_m_s double precision
    check (aerobic_speed_m_s > 0 and aerobic_speed_m_s < 15);
alter table activities add column aerobic_hr double precision check (aerobic_hr > 0 and aerobic_hr < 300);
alter table activities add constraint activities_aerobic_pair
    check ((aerobic_speed_m_s is null) = (aerobic_hr is null));
