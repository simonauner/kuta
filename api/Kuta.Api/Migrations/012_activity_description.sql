alter table activities add column description text
    constraint activity_description_length check (char_length(description) <= 10000);
