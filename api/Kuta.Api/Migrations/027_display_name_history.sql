-- The name before the latest change and when it changed. Others see the previous name for 30 days, so a follower cannot
-- quietly take a friend's name after being approved; the change time also limits how often the name can change.
alter table users add column previous_display_name text, add column display_name_changed_at timestamptz;
