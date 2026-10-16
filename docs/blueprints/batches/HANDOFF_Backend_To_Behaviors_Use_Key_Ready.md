<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Control.md — §8h.7, the backend half's as-built
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Behaviors.md — your items (CE-1154, CE-1155, CE-1157)
  - docs/blueprints/batches/REPORT_Backend_Use_Key_And_Rerun_Asks.md — the backend report and gates
-->

# NOTE — backend → behaviors: the use key's engine half is on `backend` at `4439b8aba`

FYI, no new work. Your `CE-1154` (`OperatorControlBehavior.OperateUse`) can start from **`backend@4439b8aba`**.

| you will find | where |
|---|---|
| `OperatorUseActions.For(world, actor, target)` (primary first) + `WriteParams(action, span)` | `Fdp.Toolkits/Behavior/Possession/OperatorUse.cs` |
| `UseVerbKind`, `UseReason`, `UseOutcome`, `OperatorUseStatus` (component 396, Brain group, replicated as `dtOperatorUseStatus` 141) | same file |
| `OperatorInput.UseSeq / UseVerb / UseTarget` (a new press = a changed `UseSeq`) | `Possession.cs` |
| `PossessionMode.Riding` (a riding unit gets no frames) | `Possession.cs` |
| `Embarkation.CanEmbark` (the same checks as `TryEmbark`) | `Embarkation.cs` |

Two things that differ from §8h.2 (both in §8h.7): `Drive`/`Pilot` is offered only while the driver seat itself is free, and
the vehicle reach is the constant `OperatorUseActions.VehicleReach` (4.5 m). ⚠ The live IG check of `dtOperatorUseStatus` is
still owed by backend, and waits for you: `CE-1154` is its only writer. Tell backend the sha when it lands.
