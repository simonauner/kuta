-- FIT imports used to store Garmin's raw sport name (the Sport enum in lower case, e.g. "training"), which is not one
-- of our sport keys. Map stored names to the keys FitSports now produces. The sub sport was never stored, so gym
-- activities become "workout"; the owner can change them to strength training on the edit page.
create temporary table fit_sport_names (raw text primary key, sport text not null) on commit drop;
insert into fit_sport_names values
    ('generic', 'other'), ('training', 'workout'), ('fitnessequipment', 'workout'),
    ('crosscountryskiing', 'nordic ski'), ('alpineskiing', 'alpine ski'), ('snowboarding', 'snowboard'),
    ('mountaineering', 'hiking'), ('snowshoeing', 'snowshoe'), ('paddling', 'canoe'), ('canoeing', 'canoe'),
    ('standuppaddleboarding', 'stand up paddling'), ('ebiking', 'e-bike ride'), ('inlineskating', 'inline skate'),
    ('iceskating', 'ice skate'), ('rockclimbing', 'rock climb'), ('sailing', 'sail'), ('windsurfing', 'windsurf'),
    ('kitesurfing', 'kitesurf'), ('wheelchairpushwalk', 'wheelchair'), ('wheelchairpushrun', 'wheelchair');

-- Our sport keys at the time of this migration (ActivitySports.All and Multisport.Kinds).
create temporary table known_sports (sport text primary key) on commit drop;
insert into known_sports values
    ('running'), ('trail run'), ('cycling'), ('swimming'), ('walking'), ('hiking'), ('mountain bike ride'),
    ('gravel ride'), ('e-bike ride'), ('e-mountain bike ride'), ('alpine ski'), ('badminton'), ('backcountry ski'),
    ('basketball'), ('canoe'), ('crossfit'), ('cricket'), ('dance'), ('elliptical'), ('golf'), ('ice skate'),
    ('inline skate'), ('handcycle'), ('hiit'), ('kayaking'), ('kitesurf'), ('nordic ski'), ('padel'), ('pickleball'),
    ('pilates'), ('physical therapy'), ('racquetball'), ('rock climb'), ('roller ski'), ('rowing'), ('sail'),
    ('skateboard'), ('snowboard'), ('snowshoe'), ('soccer'), ('squash'), ('stand up paddling'), ('stair-stepper'),
    ('surfing'), ('table tennis'), ('tennis'), ('velomobile'), ('virtual ride'), ('virtual run'), ('virtual rowing'),
    ('volleyball'), ('weight training'), ('windsurf'), ('wheelchair'), ('workout'), ('yoga'), ('orienteering'),
    ('other'), ('triathlon'), ('swimrun'), ('multisport');

-- A changed sport can change the sport group, so the activity is matched to a route again (as on the edit page).
update activities a
set sport = coalesce((select m.sport from fit_sport_names m where m.raw = a.sport), 'other'), route_id = null
where a.sport not in (select sport from known_sports);

update activities a
set original_sport = coalesce((select m.sport from fit_sport_names m where m.raw = a.original_sport), 'other')
where a.original_sport not in (select sport from known_sports);

-- Parts only take the split editor's sports (Multisport.Sports).
update activity_parts
set sport = 'other'
where sport not in ('running', 'cycling', 'swimming', 'walking', 'hiking', 'transition', 'other');
