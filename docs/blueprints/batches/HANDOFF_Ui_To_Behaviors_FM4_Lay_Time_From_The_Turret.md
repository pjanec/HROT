<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — owns FM-4 (time on target) and §3.7 IF-2 as built; step 1 writes here
  - docs/DESIGN_Weapon_Layers.md — W-8 / W-9 own the turret, its rates and TurretSlew, which the lay time is computed from
  - docs/blueprints/batches/REPORT_Backend_Presets_Elevation_Attitude.md — backend's P2: guns are now LAID before they fire; its lean L6 raised this
-->

# HANDOFF — behaviors: FM-4's lay time from each gun's own turret, not a fixed 10 s

**For:** the behaviors lane (`behaviors`). **From:** the ui lane (`ui`). ⭐ Dispatched at the commit that adds this file; scope frozen there. A FRAME:
step 1 is yours — write the change into [Indirect Fire](../../designs/indirect-fire/DESIGN_Indirect_Fire.md) (FM-4 and §3.7, diagram first), then
build, then fold the as-built. Ids from your `CE-2xxx` block; the ui row tracking this is **CE-1109**.

## 0. Why

🔒 **User, `2026-10-15`:** *"is M109 slow turret a real number? it should, and if it makes our behaviors failing we would need to fix those."*

| the fact | measured |
|---|---|
| backend's P2 (`b6ad14c83`, on `backend` and `ui`, ⛔ **not yet on `behaviors`**): `FireAtPoint` now LAYS the gun's turret (Track, held Slewing until OnTarget) before it fires | `REPORT_Backend_Presets_Elevation_Attitude.md` P2; `FireAtPointExecutor.cs` |
| the M109 (107) turret: **10 °/s traverse, 5 °/s elevation** — real: the only published M109 figures are the M109A1's 11 °/s and 7 °/s (hydraulic); none found for the A6 / A7 | `BdcTkbCatalog.DefineSelfPropelledHowitzer` · afvdatabase.com M109 |
| ⇒ a worst-case lay (180°) takes **18 s**; mortars and the towed 105 (30 / 15 °/s) ≈ 6 s | backend's lean L6 |
| FM-4 sets `ImpactAt = now + max(maxTof + LaySeconds, ImpactInS)` with `LaySeconds = 10` for every gun | `IndirectFireBehaviors.cs:32`, `:290` |
| ⇒ after you merge backend: an M109 that must turn more than ~100° fires **after** `ImpactAt − tof` ⇒ its first round lands late ⇒ ⚠ `CE2176_ABattery_TimeOnTarget_EveryFirstRoundLandsTogether` (and the IF-4 demo ② "three first rounds in the same second") can go red | — |

## 1. The item

| | |
|---|---|
| **goal** | every gun of a time-on-target mission can be laid before its fire time, whatever its turret's rates and wherever it points now |
| **lean** | ⭐ `ImpactAt = now + max_i(lay_i + tof_i) + margin`, floor `ImpactInS`, where `lay_i` = the seconds gun i's turret needs from its CURRENT `TurretPose` to its solution (azimuth relative to its hull, elevation), both axes slewing together as `TurretSlew.Step` does ⇒ `max(|Δaz| / traverse rate, |Δel| / elevation rate)`. `margin` ≈ 2 s (the OnTarget settle + one think). ⭐ One pure helper **beside `TurretSlew.Step`** — e.g. `TurretSlew.SecondsTo(pose, azimuth, elevation, turret)` — the same limits and short-way / dead-zone rule as `Step` (a limited arc goes the long way inside it). A gun with no turret part lays in 0 |
| **fences** | ⛔ do not slow-proof by raising the catalogue rates (the user: the number should be real). ⛔ No second slew model: the helper is `Step`'s arithmetic in closed form. ⚠ `TurretSlew` is in `Fdp.Toolkits/Combat` (backend's file): adding a pure static beside `Step` is a cross-lane edit — say it in the commit |
| **what would change the lean** | if the brain cannot read a gun's current pose at plan time (a gun not yet replicated to the planner) ⇒ use the worst case `180° / traverse rate` for that gun instead |
| **acceptance** | ★ the TOT rail with a battery of three M109s, one facing AWAY from the target (180°): every first round lands within 1 s of the others (today's rail plus the far-facing gun). ★ a mortar battery's `ImpactAt` does not grow (its lay ≤ 6 s). ★ the helper's rail: full-circle short way, limited arc long way inside it, elevation-bound case, a zero-rate axis. ★ the IF-4 demo ② still lands its three first rounds in the same second |

## 2. Before anything

1. Merge `backend` (≥ `b6ad14c83`) — FM-4 only bites with P2's laying. Push a `chore: started …` marker (rule 1b).
2. Gate report per CLAUDE.md rule 8; fold the as-built into Indirect Fire FM-4 / §3.7.
