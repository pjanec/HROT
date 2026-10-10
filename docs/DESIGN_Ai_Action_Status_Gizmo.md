<!--STATUS
state: LIVE
updated: 2026-10-09
build-state: BUILT (T1–T4) 2026-10-09 — T1–T4 APPROVED by the user 2026-10-09 (R-241); T4 built with P-6
current-answer: §6 as-built · §2 decisions (approved) · §3 classes · §4 sequence · §5 modules
stale-below: nothing
known-rot: none
known-conflict: R-226 / DESIGN_Terrain_Combat_Tuning.md §5a precedent — UtilityDecisionLog exists only while a unit is OBSERVED (trace armed); T2 here leans ALWAYS present, argued in §2
related-designs:
  - DESIGN_Terrain_Combat_Tuning.md — OWNS §5a (R-226: debug gizmos draw RECORDED state, NoScenario components) and §5b (R-227: gizmo families, scope, pins)
  - DESIGN_Uniform_Gizmo_Membership.md — OWNS §10 (R-228: gizmo membership is uniform across the five map hosts — "has data = can draw") and §9.7 (where a behaviour's gizmo goes: its data outside the hot-reload assembly, the gizmo in Hrot.Presentation)
  - DESIGN_Peek_And_Fire.md — OWNS the first consumer: the aim timer / sight gate (P-3) whose holds this makes visible, and PeekAndFire's phases (§8.2)
  - designs/behav-diag-1/DESIGN.md — OWNS DebugState, the patch command and the trace rings (the "observe" arming this does NOT depend on, T2)
-->

# AI action status — seeing why a unit is not shooting *(backend, `2026-10-09`)*

> 🔒 **User, `2026-10-09`:** *"how can we indicate the internal state of 'aiming, not shooting because target not seen and stuff
> like that for debugging'? any switchable gizmo for these internal 'thoughts' of the AI behaviors, behavior specific?"*

## 1. INVENTORY — measured `2026-10-09` (read-only sweep + `GizmoFamilies.cs`, `AiOverlayFlags.cs`, `UtilityDecisionGizmo.cs` read)

| exists | state |
|---|---|
| `UtilityDecisionLog` + `UtilityDecisionGizmo` — a recorded `NoScenario` component drawn as text at the unit, family `UtilityDecision` | ✅ used — **the template** |
| `AiOverlayFlags.Channels` (*"active locomotion/weapon/interaction action"*) | ⚠ declared, **no gizmo uses it**, not in `GizmoFamilies.All` (no scope setting, no pin) |
| `HillAttackGizmo` — draws only while the unit runs `PlatoonHillAttack`, reads that behaviour's own state | ✅ used — **the behaviour-specific template** |
| `WeaponChannel.State` holds the aim timer (`AimState`, P-3); it is `NoScenario` ⇒ recorded | ✅ but the HOLD REASONS (not seen · cooldown · friendly on the line · ROE) all return `Running` and are indistinguishable |
| `BehaviorLog.Trace` | text to the NLog file only — never the map |
| a per-unit status/reason component | ⛔ **searched `docs/`, `.dev/`, `Hrot/`, `FDP/`: none** |

## 2. Decisions — ✅ APPROVED `2026-10-09` (R-241; 🔒 user: *"AI's 'thoughts' approved."*)

| # | lean | why |
|---|---|---|
| **T1** | **one recorded component `ActionStatus` per unit**: a row per channel (locomotion · weapon · interaction) = action id, **reason** (`Moving`, `Aiming`, `Firing`, `Cooldown`, `HoldNotSeen`, `HoldFriendlyOnLine`, `HoldRoe`, `Reloading`, `OutOfAmmo`, `Done`, `Failed`…), progress / needed (e.g. 0.4 / 0.8 s), target, sim time. **Written by the EXECUTORS** | the executor is the one place every behaviour's action passes (as the aim gate) ⇒ every behaviour — BTree, HSM, blueprint, C# — gets it with no change |
| **T2** | **always present on brain units** (≈ 60 B), not only while "AI trace" is armed | the question is usually *"why is NOBODY firing"*; arming each unit first defeats it, and a replay should already hold the answer. ⚠ Departs from the `UtilityDecisionLog` precedent (armed-only, its log is bigger and per decision) |
| **T3** | **`ActionStatusGizmo`**, family **`Channels`** (added to `GizmoFamilies.All`, default **selected or pinned**, switchable to all in the layer panel; pinnable from the context menu): one short line per busy channel under the unit — `W aiming 0.4/0.8 s → 1234` · `W hold: not seen` (amber) · `L moving 12 m`. `GET /entities/{id}/weapons` reports the same reason (map and API agree) | reuses families, scope, pins and the five map hosts as built (R-227, R-228) — no new switch mechanism |
| **T4** | **behaviour-specific thoughts = a gizmo per behaviour**, as `HillAttackGizmo`: `PeekAndFireGizmo` draws only while the unit runs `PeekAndFire` — its phase and timer, the hide and peek points, and the remembered firing positions with their heat (burned = red) from `FiringPositionMemory` | the behaviour's state is already recorded (unit memory, node state); no new runtime text API |

| rejected | the one fact that killed it |
|---|---|
| a free-text "thought" string each behaviour writes every tick | strings per unit per tick in recorded state; a reason enum + numbers carries the same and stays fixed-size |
| logging holds to `BehaviorLog` | never reaches the map; you read files after the fact |
| arming per unit only (the `UtilityDecisionLog` way) | see T2 |

## 3. Classes

```mermaid
classDiagram
  class ActionStatus { <<NEW · recorded · NoScenario · per brain unit>> Rows[3] }
  class ActionStatusRow { ActionId; Reason; Progress; Needed; Target; At }
  class ActionReason { <<NEW enum>> Moving · Aiming · Firing · Cooldown · HoldNotSeen · HoldFriendlyOnLine · HoldRoe · Reloading · OutOfAmmo · Done · Failed }
  class AimAndFireExecutor { <<existing>> writes the weapon row }
  class FireAtPointExecutor { <<existing>> writes the weapon row }
  class MoveExecutors { <<existing>> write the locomotion row }
  class ActionStatusGizmo { <<NEW>> family Channels }
  class PeekAndFireGizmo { <<NEW, BUILT with P-6>> reads FiringPositionMemory }
  class UtilityDecisionGizmo { <<existing template>> }
  class GizmoFamilies { <<existing>> All += Channels }
  ActionStatus "1" *-- "3" ActionStatusRow
  ActionStatusRow --> ActionReason
  AimAndFireExecutor ..> ActionStatus
  FireAtPointExecutor ..> ActionStatus
  MoveExecutors ..> ActionStatus
  ActionStatusGizmo ..> ActionStatus
  ActionStatusGizmo ..> GizmoFamilies
```

*What the picture shows: the writers are the EXECUTORS, not the behaviours — that is why no behaviour needs editing to be seen.*

## 4. Sequence — "why is A not shooting?"

```mermaid
sequenceDiagram
  participant BT as any behaviour
  participant X as AimAndFireExecutor
  participant S as ActionStatus (recorded)
  participant G as ActionStatusGizmo
  BT->>X: AimAndFire(target)
  X->>X: SightNow.Sees? no
  X->>S: weapon row = HoldNotSeen, target, now
  G->>S: read (selected or pinned unit)
  G-->>G: "W hold: not seen" (amber)
  X->>X: seen 0.4 of 0.8 s
  X->>S: weapon row = Aiming 0.4 / 0.8
```

## 5. Modules — who writes, who draws

```mermaid
graph TD
  CGF["Brain node (CGF / Editor / SimHost-as-brain)"] -->|WeaponDispatcherSystem runs the executors| X[executors]
  X -->|write| S[ActionStatus]
  S -->|recorded| R[flight recorder → Replay Browser]
  S -->|same world| M[map hosts: gizmo draws]
```

*The status lives where the brain runs (the brain components are not replicated, R-226); the map hosts that share that world draw it,
and a replay draws it from the recording. ⚠ An IG that only receives replicated state does not see it — as for every AI gizmo today.*

## 6. As-built *(`CE-3140`, `2026-10-09`)*

| # | built | where |
|---|---|---|
| T1 | `ActionStatus` (component id **344**, `NoScenario`, recorded) = `Locomotion` · `Weapon` · `Interaction` rows (`ActionStatusRow`: action id, `ActionReason`, progress/needed, target, sim time); the one write path `ActionStatusOf.Weapon/Locomotion` (a no-op where the unit or the world has none). Writers: ⭐ `AimAndFireExecutor` at EVERY exit (done · out of ammo · hold: not seen · aiming x/0.8 s · cooldown · reloading x/3 s · hold: friendly on line · hold: ROE · firing n/N), `FireAtPointExecutor` the same minus the sight/aim rows, and — ⚠ deviation, argued — the **locomotion row is written generically by `LocomotionDispatcherSystem`** after any locomotion executor (moving / done / failed) instead of in each of the seven locomotion executors: one writer, no per-executor edits; it carries no distance yet. The interaction row is not written yet | `Behavior/Diagnostics/ActionStatus.cs`, both fire executors, `LocomotionDispatcherSystem.cs` |
| T2 | always present: `BehaviorTkbTranslator` adds it beside the action channels at spawn (brain units only); registered with the brain components (`CognitiveComponentRegistry`), brain-only in `HrotRoleComponentSets` | as named |
| T3 | `ActionStatusGizmo` (family `Channels`, label **"Actions"**, default *selected or pinned*): lines `W aiming 0.4/0.8s` · `W hold: not seen` (amber) above the unit; a row older than 1 s of sim time is not drawn. The family joined `GizmoFamilies.All` ⇒ the layer panel scope (`LayerControlGizmo.ActionsSelectedOnly`) and the **Pin gizmos → Actions** menu entry (`GlobalActionIds.PinGizmosActions` = 268) come from the existing machinery. `GET /entities/{id}/weapons` reports the same `status` (reason, progress, needed, target, at) plus the magazine | `Hrot.Presentation/ScenarioEditor/Gizmos/ActionStatusGizmo.cs`, `GizmoFamilies.cs`, `GizmoPins.cs`, `LayerControlGizmo.cs`, `DebugApiService.Weapons.cs` |
| T4 | ✅ built with P-6 (`CE-3136`): `PeekAndFireGizmo` (family `Channels`, "Actions") draws the unit's `FiringPositionMemory` — a ring per remembered position, yellow → red with heat, red when burned, `h1.7 x3` — and, while the node ran within 1 s, `PF hidden 2.1s` above the unit with the hide and peek points. ⚠ Deviation: it does not gate on a behaviour NAME (as `HillAttackGizmo` does) — `PeekAndFire` is a node inside any tree, so the node mirrors its phase and points into the unit memory and the gizmo reads that (`UnitMemory.GetInView`, new) | `Hrot.Presentation/ScenarioEditor/Gizmos/PeekAndFireGizmo.cs` (⭐ not beside the node: a behaviour gizmo reads the behaviour's DATA, so the data type lives outside the hot-reload assembly and the gizmo in the presentation assembly every host loads — `DESIGN_Uniform_Gizmo_Membership.md` §9.7); 📄 DESIGN_Peek_And_Fire.md §7 P-6 |

Rails: `AimAndFireExecutorTests.T1_TheExecutorSaysWhy_InTheWeaponRow` (not seen → aiming 0.1/0.8 → firing → reloading 2 s), `DebugTraceGizmoTests.CE3136_TheActionsGizmo_SaysWhyAUnitIsNotFiring` (one line, a stale row dropped, the wording), `…CE3136_TheActionsFamily_IsListed_PinnableAndSelectedByDefault`.
