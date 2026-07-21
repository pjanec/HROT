# EQS request/poll slice design (`RequestAreaQuery` / `IsAreaQueryResolved`)

Reference: [`Architect_Question_7_EQS_Slice.md`](Architect_Question_7_EQS_Slice.md) — all four leans
(A/B/C/D) APPROVED 2026-07-17. Oracle: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`
(`Action_RequestAreaQuery` 188-227, `Condition_IsAreaQueryResolved` 237-278) — **untouched**.

## Assets

| Blueprint | Oracle | Intent / Hostings |
|---|---|---|
| `Assets/Blueprints/HillAssault2_RequestAreaQuery.bp.json` | `Action_RequestAreaQuery` | `Action` / `[BTreeAction]` |
| `Assets/Blueprints/HillAssault2_IsAreaQueryResolved.bp.json` | `Condition_IsAreaQueryResolved` | `Action` / `[BTreeAction]` (deviation, see below) |

## Deviation 1 — both assets are `Intent=Action`, not `Condition`

Both oracle nodes return tri-state `NodeStatus` including `Running`. A `Condition`-intent blueprint emits
a `bool` wrapper (`TickCore(...) == NodeStatus.Success`), which collapses `Running` → `false` and destroys
the poll. `Stage2_Validate.cs`'s `V_AiPrimitiveIntent` hard-errors **BP1100** ("Return Running is
forbidden") for `Condition` intent — confirming this structurally, not just by convention. `IsAreaQueryResolved`
keeps its oracle name (`Condition_…`) despite being authored as an `Action` blueprint.

## Deviation 2 — `Return(Running)` stateless poll (Q7-A), validated

No `__phase`, no latent primitive. Poll state lives entirely in `WorkingState.CachedEqsRequestId`. The
BTree host re-ticks an `AiPrimitive` Action from the top every frame while it returns `Running`; each
tick re-reads WorkingState from scratch, exactly like the oracle's C#. **Confirmed working**: both proof
suites drive `TickCore` across 2-3 ticks with a fresh WorkingState instance passed back in each time
(mirroring the real host's re-tick), and the tri-state routes correctly purely from WorkingState content —
no additional signal is threaded between ticks.

## `RequestAreaQuery` — oracle line → node mapping

| Oracle (188-227) | Blueprint node(s) |
|---|---|
| `s.CachedEqsRequestId != -1` | `Compare(GetVariable(CachedEqsRequestId), Literal -1L, NotEqual)` → `Branch1` |
| `!existing.IsReady` → `Running` | Branch1.True → `FunctionCall AreaQueryBatchOps.IsReady` → `Branch2.False` → `Return(Running)` |
| result ready → `Success` (advance) | `Branch2.True` → `Return(Success)` |
| `p.TargetAreaEntity.IsNull \|\| !IsAlive` → `Failure` | Branch1.False → `FunctionCall WorldOps.IsAlive(GetParameter(TargetAreaEntity))` → `Branch3.False` → `Return(Failure)` |
| `AreaQueryBatchHelper.RequestAreaQuery(...)` | `Branch3.True` → `FunctionCall AreaQueryBatchOps.Request` (EXEC) → `id` |
| `id == -1` → `Running` (batch full) | `Compare(id, -1L, Equal)` → `Branch4.True` → `Return(Running)` |
| `s.CachedEqsRequestId = id; s.EqsRequestTime = SimulationTime` → `Success` | `Branch4.False` → `SetVariable(CachedEqsRequestId←id)` → `FunctionCall WorldOps.SimTime` → `SetVariable(EqsRequestTime←now)` → `Return(Success)` |

`Return(Success)` is a 2-in-degree merge (Branch2.True direct + the submitted-request tail);
`Return(Running)` is a 2-in-degree merge (Branch2.False + Branch4.True). Both are pure exec-In-only
terminal sinks — proven safe by `HillAssault2_AimAndFireSpecific`'s 3-/4-in-degree Return merges.

## `IsAreaQueryResolved` — oracle line → node mapping

| Oracle (237-278) | Blueprint node(s) |
|---|---|
| `s.CachedEqsRequestId == -1` → `Failure` (guard) | `Compare(GetVariable(CachedEqsRequestId), -1L, Equal)` → `BranchGuard.True` → `Return(Failure)` |
| `!result.IsReady` | `FunctionCall AreaQueryBatchOps.IsReady` → `BranchReady.False` |
| `SimulationTime - s.EqsRequestTime > 5.0` → free + clear + `Failure` | `FunctionCall WorldOps.SimTime` `BinaryOp(Subtract, GetVariable(EqsRequestTime))` → `Compare(GreaterThan, 5f)` → `BranchTimeout.True` → `Free` → `SetVariable(CachedEqsRequestId←-1L)` → `SetVariable(CachedTargetGroupHandle←-1)` → `Return(Failure)` |
| still waiting → `Running` | `BranchTimeout.False` → `Return(Running)` |
| `result.TargetCount == 0` → free + clear + `Failure` (area clear) | `FunctionCall AreaQueryBatchOps.TargetCount` → `Compare(Equal, 0)` → `BranchClear.True` → `Free` → `SetVariable(CachedEqsRequestId←-1L)` → `SetVariable(CachedTargetGroupHandle←-1)` → `SetVariable(EqsRequestTime←0f)` → `Return(Failure)` |
| targets found → cache handle, **leave id set** (SC-HA011-5) → `Success` | `BranchClear.False` → `SetVariable(CachedTargetGroupHandle←FunctionCall TargetGroupHandle)` → `SetVariable(EqsRequestTime←0f)` → `Return(Success)` |

`Return(Failure)` is a 3-in-degree merge (BranchGuard.True + timeout-tail + area-clear-tail). The timeout
and area-clear exec chains stay two separate sub-chains up to that shared terminal — an exec `Out` pin
must remain single-destination, so mid-chain merging (e.g. sharing the `Free` call across both paths) was
deliberately *not* attempted; only pure data nodes (`GetVariable`/`Literal`) fan out freely.

## Params / WorkingState

| Asset | Params | WorkingState |
|---|---|---|
| `RequestAreaQuery` | `TargetAreaEntity` (`Fdp.Core.Entity`) | `CachedEqsRequestId` (`Int64`, `-1`), `EqsRequestTime` (`Single`, `0`) |
| `IsAreaQueryResolved` | — | `CachedEqsRequestId` (`Int64`, `-1`), `CachedTargetGroupHandle` (`Int32`, `-1`), `EqsRequestTime` (`Single`, `0`) |

## Curated helpers used (`Hrot.AI.Behaviors.Brains.AreaQueryBatchOps` / `WorldOps` — already committed, per Q7-D)

| Helper | Pure? | Why curated |
|---|---|---|
| `AreaQueryBatchOps.Request` | No (EXEC) | Side-effecting submit; EXEC so never dead-code eliminated |
| `AreaQueryBatchOps.IsReady` | Yes | Scalar accessor over `AreaQueryBatchHelper.GetAreaQueryResult` |
| `AreaQueryBatchOps.TargetCount` | Yes | ″ |
| `AreaQueryBatchOps.TargetGroupHandle` | Yes | ″ |
| `AreaQueryBatchOps.Free` | No (EXEC) | Side-effecting slot release; EXEC so never dead-code eliminated |
| `WorldOps.SimTime` | Yes | `SimulationTime` read (Q7-C: curated accessor, no native `GetTime` node yet) |
| `WorldOps.IsAlive` | Yes | Reused from the `AimAndFireSpecific`/`Dispatch` slices |

Per Q7-B, everything *except* the batch-system touch (submit/poll/free) stays visual: the `Running`/
`Success`/`Failure` routing, the 5 s timeout arithmetic, and all `WorkingState` writes are native
`Branch`/`Compare`/`BinaryOp`/`SetVariable`/`Return` nodes.

## SC-HA011-5

On the `IsAreaQueryResolved` targets-found success path, `CachedEqsRequestId` is deliberately **not**
reset to `-1` — `Action_DispatchWaveWithTargets` consumes it afterward. Proof test
`GeneratedTickCore_Ready_TargetsFound_ReturnsSuccess_CachesHandle_AndLeavesRequestIdSet` asserts
`ws.CachedEqsRequestId == slot` (unchanged) after a `Success` tick.

## Compiler fix (not the oracle — `Hrot.Blueprints.Compiler`)

Authoring the first curated **impure (EXEC) `FunctionCall`** whose non-void `Return` pin is consumed from
a later scheduler block (`AreaQueryBatchOps.Request`'s `id`, read by `SetVariable` after the batch-full
`Branch`) exposed two real `Stage5_Schedule.cs` bugs, fixed alongside these assets:

1. An impure `FunctionCallNode` with an empty `TargetGraphId` (an ordinary curated CLR helper call, not a
   call into another Library-dispatch blueprint) was lowered via `IrOp_LibraryCall(0, ...)`, which
   resolves to a nonexistent `__LibBp_00000000_Bp` class (CS0103). `IrOp_LibraryCall`'s actual purpose is
   invoking another blueprint by a real `LibraryBlueprintId`; a plain CLR static-method call must lower
   like the pure-`FunctionCall` case (`IrOp_PureCall` → `global::{TargetTypeId}.{MethodName}(...)`),
   scheduled eagerly as a statement instead of resolved lazily. Also fixed the latent void-return case
   (`Free`): the original code always allocated a `ResultValue`, which would have emitted the
   uncompilable `var __tN = SomeVoidMethod();` (CS8716) once a void impure call was ever authored.
2. The per-block pin-value cache (`_pinValueCache`, cleared at the start of every scheduler block — correct
   for pure/recomputable reads whose upstream may have been mutated by an intervening write) was the only
   cache consulted when resolving a data pin. A value produced by an impure exec **statement** is
   materialized exactly once as a real C# local; the emitted `TickCore` body is flat (`goto`-based, no
   nested scopes), so that local stays in scope and definitely-assigned for any later block reachable only
   through the block that declared it — but the resolver had no way to know that and fell through to a
   bogus `default` literal. Fixed by adding a cross-block `_statementPinCache` (never cleared) populated at
   the impure-`FunctionCall`/`CallPeerBlueprint` statement sites, consulted before the per-block cache.

Both fixes are scoped to `Compiler/Stages/Stage5_Schedule.cs`; full `Hrot.AiEditor.Generators.Tests` (168
tests) and `Hrot.Blueprints.Compiler.Tests` suites are green after the change — no regressions.
