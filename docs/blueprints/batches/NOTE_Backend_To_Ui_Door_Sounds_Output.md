<!--STATUS
state: LIVE
updated: 2026-10-25
current-answer: the whole note
related-designs:
  - ../../DESIGN_Node_Roles_And_Policies.md — §3.0a: sound CALCULATED on Perception, OUTPUT on every 3-D map host (the user, 2026-10-25)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.8 the door sounds and the voice the AI now hears
  - ../../designs/audio/DESIGN_FPS_Audio.md — OWNS AudioLayer (the output)
-->

# NOTE — backend → ui: door sounds are heard by the AI now; the operator hears none (`CE-3320`)

🔒 User, `2026-10-25`: *"the sound output anywhere where 3d map is, sound calculation on percpetion node"* ([Node Roles §3.0a](../../DESIGN_Node_Roles_And_Policies.md)).

| for `AudioLayer` (every map host, your file) | what backend built for the AI, to mirror |
|---|---|
| a door's sound | each node sees the replicated `DoorState` change; the carries are `Fdp.Toolkit.Perception.Signatures.DoorSounds.Carry(from, to)` (lock 5 m · open / shut 15 m · breach 40 m), at `TerrainWorld.Doors[i].Center` / `SillZ` |
| a shout | `Fdp.Toolkit.Civilians.Utterances.IsShouted(kind)` — every kind but `Talk`; your AQ105 F handoff plays the clip |
| walls | the AI's ear loses a halving per wall / slab (`AcousticPerception.WallFactor`); the operator's ear ignores walls today — your call whether it should match |
