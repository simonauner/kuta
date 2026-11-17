-- "I ran the same course": the owner links their own activity to another activity they may read. Activities linked
-- to each other, directly or through others, form one course. Deleting the other activity removes the link.
alter table activities add column same_course_as uuid references activities(id) on delete set null
    check (same_course_as <> id);
create index activities_same_course_idx on activities (same_course_as) where same_course_as is not null;
