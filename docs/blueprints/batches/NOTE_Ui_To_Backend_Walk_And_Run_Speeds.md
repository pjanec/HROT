<!--STATUS
state: LIVE — ui → backend: (1) first-person walk and run are too close, the speeds belong in the TKB (CE-1208); (2) the operator's weapon choice never reaches ActiveMount (CE-1222).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Control.md — §2.2 / §8: the possessed walker and its keys (Shift = run); no walk / run values designed (searched)
  - docs/blueprints/PLAN_Ui_FPS_And_Quick_Wins.md — #9: this note is that step
-->

# NOTE — ui → backend: walk and run speeds per person type (`CE-1208`); the operator's active mount (`CE-1222`)

## 1. Walk and run

🔒 User, live, `2026-10-19`: *"I think the speed difference between running and walking in FPS mode is quite small."*
Tracker row: [`CE-1208`](../Blueprint_Issues_Tracker.md) (backend — the TKB speeds and the walker).

| measured | where |
|---|---|
| walking is a hard-coded HALF of the type's top speed: `top = MaxSpeedFwd * (drive.Gait == 1 ? 0.5f : 1f)` | `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/CarKinematicsSystem.cs:185` |
| the BDC rifleman's top speed is `p.MaxSpeed = 2.5f; // Walking` ⇒ run 2.5 m/s, walk 1.25 m/s, both slow | `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BdcTkbCatalog.cs:723` (and 2.5 at `:66`, `:229`, `:260`) |
| the urban soldier's 6.0 gives run 6 / walk 3 — closer to right | same catalogue |
| no walk / run values in any design | searched Entity Control §2.2 / §8 and Navigation v2 §11 |

**Lean:** the TKB carries a walk and a run speed per person type (≈ 1.5 and ≈ 5 m/s for a soldier), and the walker reads them in place of
`top × 0.5`. ⚠ The AI's movement uses the same top speed, so raising the BDC numbers moves the AI too — your call whether the AI keeps
walking at the walk speed and only the run uses the new top.

Nothing in ui depends on the shape: the possessed walker already sends `Gait` (0 run, 1 walk) through `drive`, so ui needs no change.

## 2. The operator's weapon choice never reaches `ActiveMount` (`CE-1222`)

Found building the first-person zoom ([`CE-1211`](../Blueprint_Issues_Tracker.md), [Entity Control §8j.5](../../DESIGN_Entity_Control.md)):

| measured | where |
|---|---|
| `Execute` hands an operator action to `OperatorFire` and returns | `FDP/Toolkits/Fdp.Toolkits/Combat/Executors/AimAndFireExecutor.cs:73` |
| the only `WeaponPostureOf.SetActiveMount` is on the AI path, below that return | same file, `:102` |
| `OperatorFire` reports the posture (`SayAt` ⇒ `WeaponPostureOf.Report`) but never the mount | `:343-400` |

⇒ while possessed, `WeaponPosture.ActiveMount` keeps the AI's last choice, not the weapon the operator picked with 1–4. It is read by the
sim's scope rule (`HeldItems.ScopeUp`) and by every map's drawn weapon (P4, W-13e). **Lean:** `OperatorFire` calls
`WeaponPostureOf.SetActiveMount(world, entity, mountIndex)` once it has resolved the mount (it already writes only on a change).

---
_ui lane, 2026-10-19_
