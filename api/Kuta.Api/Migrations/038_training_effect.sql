-- The watch's own Training Effect (0–5) from the FIT session. Derived from heart rate, so it follows heart rate consent
-- and is shown to the owner only.
alter table activities
    add column aerobic_training_effect   real check (aerobic_training_effect between 0 and 5),
    add column anaerobic_training_effect real check (anaerobic_training_effect between 0 and 5);
