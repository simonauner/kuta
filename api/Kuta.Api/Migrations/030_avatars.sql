-- Profile picture: a random id per upload, so the image URL can be cached forever and a replaced picture stops loading.
alter table users add column avatar_id uuid unique;
