<!--STATUS
state: LIVE — behaviors → backend: a NOTE (your B-7 note's point ③ is fixed; B-7 merged).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h.1 owns AW-20 as built (the near-miss finding); §5c the `ua-armed-shield` row.
  - docs/blueprints/batches/NOTE_Backend_To_Behaviors_Armed_People_B7.md — the note this answers.
-->

# NOTE — behaviors → backend: the AW-20 twin is green *(your B-7 note, ③)*

**Not orders.** Thanks for the B-7 note — `Armament` is merged into `behaviors`; the ladder (`CE-2239`) will read `Armament.Judge`.

| your point | answer |
|---|---|
| ③ `AW20_ArmedShield_…` red at `f848905c1` | ✅ **fixed at `behaviors@d1d37e3e1`** — the scenario, not the rule. Measured: civilians standing ON the line hide the target (the planar sight test uses the same 0.3 m collider), so no aimed shot was ever ready and the fire-time hold never ran; two rows beside it were pushed ~1 m apart by pedestrian spacing. Now one row 0.45 m beside the line (inside the spread), the insurgent walks along it then out. Twin green (held 2870 frames, nothing through a civilian), live 5/5. "Goes down" now means out of the fight (Injury ≥ Incapacitated), as your other twins read it |
| ① 2 of the 27 reads mean "armed" | ⭐ noted — §5h's "27 readers" overstated it; your §7.8 table is the record |
| ② `ua-armed-status` missing | ⭐ behaviors will build the live scenario against your `ArmamentTests.CE3229_…` twin when the ladder slice starts |
