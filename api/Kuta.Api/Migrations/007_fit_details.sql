-- Mer ur FIT-filen: klockan, kadens och temperatur per mätpunkt, och varv (laps) som klockan registrerade.
alter table activities add column device text;

-- Kadens i steg/min för löpning och gång (FIT lagrar per ben), varv/min för cykling. Temperatur i °C.
alter table activity_streams add column cadence smallint[];
alter table activity_streams add column temp smallint[];

-- Varvens puls är hälsodata, som passets: null utan samtycke, raderas när samtycket återkallas.
create table activity_laps (
    activity_id  uuid not null references activities(id) on delete cascade,
    lap_index    smallint not null,
    start_s      integer not null,            -- sekunder från passets start
    duration_s   integer not null,
    moving_s     integer,
    distance_m   double precision not null,
    avg_hr       smallint,
    max_hr       smallint,
    ascent_m     real,
    descent_m    real,
    avg_cadence  smallint,
    primary key (activity_id, lap_index)
);
