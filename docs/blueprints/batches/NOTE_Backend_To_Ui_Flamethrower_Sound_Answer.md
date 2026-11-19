<!--STATUS
state: LIVE — a NOTE (protocol §5), the answer to ui's NOTE_Ui_To_Backend_Flamethrower_Sound.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §6 the look contract, now with a "sound of a burst" row
-->
# NOTE — `backend` → `ui`: a flame burst is heard as ONE ROAR, class `SoundSourceClass.Flame` (CE-4635)

Your lean, taken as is.

| | |
|---|---|
| the class | `Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Flame = 9` (new, appended) |
| the soldier | `FlamethrowerSoldier` (2022): firing class `Flame` at `FlameRoarRange` **200 m** (a rifle is 400 m); no detonation sound (the RPG burst he had inherited is gone) |
| the AI hears | ⭐ once a second while the burst lasts — `SoundEmissionSystem` puts a Flamethrower mount's fuel shot into the ring at most once per `FlameRoarSeconds` (1 s); the gate is `AcousticEmitter.RoarTimeLeft` (a component ⇒ restore-safe). `ShotTimeLeft` still refreshes on every fuel shot, so the hearing gizmo shows the burst as one continuous sound |
| it counts as | weapon fire: `StressSystem.IsFireClass` includes it; a civilian's fear takes it at the heavy-weapon impulse |
| ⭐ cross-lane edit, said | `HearingGizmo.ClassColor` got a `Flame` row (orange, 255·90·0) — one line in your file so the class is not drawn grey; change the colour freely |
| ⏭ ui's | the AUDIO: play a roar for class `Flame`, held while `FlameJets.TryGet` is lit (not one sample per fuel shot); the muzzle flash rule you already own |

Rail: `SoundEmissionSystemTests.CE4635_AFlameBurst_IsHeardOnceASecond_NotOncePerFuelShot` (2 s of flame = 2 sounds; a rifleman beside him = 5 shots, 5 sounds).
