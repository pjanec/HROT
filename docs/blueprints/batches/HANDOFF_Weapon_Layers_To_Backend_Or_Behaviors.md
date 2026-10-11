<!--STATUS
state: LIVE
updated: 2026-10-11
current-answer: the whole file (a frame — the designs it points at carry the substance)
stale-below: nothing
related-designs:
  - docs/DESIGN_Weapon_Layers.md — the owning design (W-1..W-12, §5a–§5e as built, §6 ambient fire)
  - docs/DESIGN_Visual_Effects.md — §6b E4 Muzzle.Of (built)
  - docs/DESIGN_Building_Interiors.md — §3k warheads; "WHEN the AI throws" settled by W-12e
-->

# Handoff — weapon layers: what is built, what is approved and waiting, for the backend or behaviors lane

**Dispatched at `ee08b16f1` (branch `ui`).** 🔒 User, `2026-10-11`: *"The weapon stuff and grenade scenario needs to go to backend or
behaviors lane. Telling them to merge our branch and decide. So we can focus on the ui things again."* ⇒ the ui lane builds no more of
this; the receiving lane (decided between backend and behaviors) owns it from here, including the tracker row
[`CE-1049`](../Blueprint_Issues_Tracker.md).

**First step:** merge `ui` (at or after `ee08b16f1`). ⚠ The ui lane already merged `backend@62d42f5ff` (commit `6c2274a53`).

## Built on `ui` (merge it, nothing to redo)

| slice | what | design | key rails |
|---|---|---|---|
| WL-1a/1b | turret part child per TKB turret on every node, `TurretPose`, `dtTurretPose` (MuscleGround) on the wire | Weapon_Layers §5a, §5b | `WeaponMountTests.WL1_*`, `EntityTurretPoseTranslatorTests` |
| WL-2a/2b | `WeaponTask` / `WeaponTaskStatus` on the turret part (W-11, R-267); the Muscle's `WeaponTaskDispatcherSystem` (Track, `BallisticAim`, `TurretSlew`); `dtWeaponTask` (Brain) / `dtWeaponTaskStatus` (Muscle) | §5c, §5d | `WeaponTaskDispatcherTests`, `EntityTurretPoseTranslatorTests.WL2b_*` |
| WL-3 | Engage: `AimAndFireExecutor` grants rounds through the turret task, the muscle fires; `TrackTargetExecutor` (action 3) on CGF | §5e | `AimAndFireExecutorTests.WL3_*`, cluster `WeaponChoiceScenarioTests` (asserts the turret path) |
| B5 / E4 | `Muzzle.TryOf` — the round, flash, tracer and drawn barrel tip from one point | Visual_Effects §6b | `FireProcessingSystemTests.E4_*`, `Map3DTests.E4b_*` |

## Approved by the user, NOT built — the receiving lane decides when and how

| item | design | ⚠ note |
|---|---|---|
| **WL-4** category profiles — W-12a (mount chosen once per engagement), W-12b (which categories use the turret task), W-12c (ATGM one round at a time), W-12d (ammo choice deferred) | Weapon_Layers W-12 (U-W8) | W-12a removes WL-3's known limit (a per-frame mount flip loses an unaccounted round, §5e) |
| **Rifle or grenade at a hidden target** — W-12e (the weapon brain throws at the last-known position within grenade range), W-12f (`CloseToThrow` in the Attack approach decision), demo **`ua-grenade-or-rifle`** | W-12e/f + the U-W7 note | ⚠ unmeasured: does a low throw clear a 1 m wall at 20–25 m? Measure before fixing the layout |
| **TrackTarget behaviour node** (BTree / Blueprint) | §5e as-built | the executor exists; the vocabulary does not |
| **W-10 turret dead reckoning** | W-10 | the user said "no implementation yet" (U-W5); the wire already carries rates + sim stamp (written 0 / now) |
| **Ambient fire** (A-1..A-6) | §6 | ⚠ at high counts it needs W-10 — a scanning turret sends ~60 pose samples/s without it (0.2° threshold) |

## Fences

- ⛔ Do not change the turret part ids (`2000 + index`, M23 as built) or descriptor numbers 124–126 without updating both directions.
- ⛔ `WeaponTaskFailure`'s zero member is `NoFailure`, not `None` (DDS codegen puts one file's enums in one IDL scope).
- ⭐ Fold every deviation back into `DESIGN_Weapon_Layers.md` (obligation ⑤), as the ui lane did for §5a–§5e.
