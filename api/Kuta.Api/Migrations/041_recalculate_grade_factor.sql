-- The grade adjustment of the training load and aerobic efficiency now follows the GAP curve, so recalculate stored
-- values once for every user with streams, skipping users who already have a recalculation queued or running.
insert into jobs (user_id, kind)
select distinct a.user_id, 'recalculate'
from activities a join activity_streams s on s.activity_id = a.id
where s.dist is not null
on conflict (user_id, kind) where status in ('queued', 'running') do nothing;
