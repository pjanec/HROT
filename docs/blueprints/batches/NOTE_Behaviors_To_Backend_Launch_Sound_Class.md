<!--STATUS
state: LIVE — behaviors → backend: a question, not a request to build now
updated: 2026-10-19
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8d HQ-1 (a heard launch is a SPOT) reads the class
  - docs/blueprints/batches/NOTE_Backend_To_Behaviors_Urban_Battle_Data.md — BR-14, the reports this asks about
-->
# Note — behaviors → backend: one sound class for a machine gun and a mortar

Thanks for BR-14 (`CE-3273`) and R-3023 (`CE-3276`): a soldier now hears an 81 mm launch at 1.2 km, and behaviors built on it —
`CE-2309` (Command Chain §8d HQ-1): a `HeavyWeapon` **Shot** heard from **≥ 1000 m** becomes a SPOT flagged `IndirectFire`, which the
platoon relays and asks fire support on.

| what we measured | consequence |
|---|---|
| BR-14 gives the HMG (1 km), the ZU-23's 23 mm (1.5 km), the shoulder / tripod launchers and the tubes ONE class, `HeavyWeapon` | below 1 km a machine gun and a mortar sound the same to a listener — so the rule needs ≥ 1000 m |
| between 1 and 1.5 km only the ZU-23 and the tubes carry | ⚠ a ZU-23 firing 1–1.5 km off is reported as a mortar |

**The question:** would a distinct class for the tubes and guns — e.g. `SoundSourceClass.Artillery` on the 60 / 81 / 120 mm, the 105 mm and
the M109 — be acceptable (R-207: still a class, never an identity)? With it the rule would read "an Artillery launch, any distance", and
the ZU-23 would stop reading as a mortar. ⛔ No change is needed for the demos to run; the 1000 m rule is the stop-gap.

---
_behaviors lane, 2026-10-19_
