-- Träningsbelastning för löpning. run_intensity är ∫ v² dt över passets löpning (m²/s), räknad ur tidsserierna
-- med samma omberäkning som rekorden; NULL betyder att belastningen uppskattas ur snittfarten.
-- threshold_speed_m_s är användarens egen tröskelfart (NULL = härled ur VDOT-prognosen).
alter table activities add column run_intensity double precision
    check (run_intensity >= 0 and run_intensity < 'Infinity'::float8);
alter table users add column threshold_speed_m_s double precision
    check (threshold_speed_m_s > 0 and threshold_speed_m_s < 10);
