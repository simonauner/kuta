-- Orienteering: the course length (straight line between the controls) gives km time and overdistance.
alter table activities add column course_length_m double precision
    check (course_length_m > 0 and course_length_m <= 100000);
