<!--STATUS
state: LIVE — for behaviors (the "pop smoke" decision can be built now)
updated: 2026-11-19
current-answer: the whole file (a short note)
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6e: VA-S5 smoke dischargers as built
  - HANDOFF_Coordinator_To_Behaviors_Pop_Smoke.md — your half, which waited for this mount
-->
# NOTE backend → behaviors: SMOKE DISCHARGERS work — "pop smoke" can be built (`2026-11-19`, CE-4670 VA-S5)

The mount your `HANDOFF_Coordinator_To_Behaviors_Pop_Smoke.md` waits for is built (design §6e):

| | |
|---|---|
| which vehicles | M1 Abrams (12 tubes × 2 salvos), M2 Bradley (8 × 2), T-72 (8 × 1) — each its LAST mount |
| find it | `Countermeasures.MountOfKind(world, unit, WeaponCategory.SmokeDischarger, out _, out int mount, out var dto)` |
| fire it | the existing `FireAtPointNodes.FireAtPoint` with `Mount = mount` and `Point = the threat` (any distance): ONE fire = the whole salvo, a 110° fan of screening grenades centred on the bearing to the point, bursting 30 m out; each a cloud that blocks sight AND the thermal sight |
| holds | hold-fire and the force level do NOT hold it (self-protection, `WeaponCategories.IsSelfProtection`); the ammunition is salvos (`WeaponState` of that mount) |
| never a weapon | every "armed" / weapon-choice reader skips it — your fire logic will not pick it by itself |

⭐ Cross-lane, said: `AirCombatNodes` (your file) now asks `IsSelfProtection()` instead of `!= Countermeasure` — the same meaning for every
aircraft today. Test bed: any scenario with one of the three types; a rail fires it (`FireProcessingSystemTests.CE4670_SmokeDischargers_*`).
