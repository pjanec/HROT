# Wave-core slice design — `IsWaveCompleted` + `DispatchWaveWithTargets`

P1b (Hill-attack → Blueprints migration), architect [`Architect_Question_8_Wave_Core.md`](Architect_Question_8_Wave_Core.md)
(all five leans A–E approved 2026-07-17). Mirrors the shipped-slice conventions in
[`HANDOFF_Blueprint_Migration_State.md`](HANDOFF_Blueprint_Migration_State.md).

## Status

| Graph | Asset | Build | Proof |
|---|---|---|---|
| `HillAssault2_IsWaveCompleted` | `Assets/Blueprints/HillAssault2_IsWaveCompleted.bp.json` | ✅ 0 errors | ✅ 4/4 pass |
| `HillAssault2_DispatchWaveWithTargets` | `Assets/Blueprints/HillAssault2_DispatchWaveWithTargets.bp.json.pending-CS0400-cast-bug` | ❌ blocked — see [Compiler limitation](#compiler-limitation-blocks-dispatchwavewithtargets) | 3 tests authored, `[Fact(Skip=...)]` |

`DispatchWaveWithTargets` is fully authored and structurally verified (P1b nested-branch scheduling
confirmed working — see below) but is parked outside the `Assets\Blueprints\**\*.bp.json` build glob
(renamed with a `.pending-CS0400-cast-bug` suffix) so it does not block `IsWaveCompleted` or any other
blueprint's build. **Do not touch the compiler** to unblock it (out of scope for this slice) — reactivate
by renaming the file back and un-skipping the proof tests once the compiler-side fix below lands.

## `IsWaveCompleted` — oracle line → node mapping

Oracle: `HillAttackCommanderNodes.Condition_IsWaveCompleted`, line 447–503.

| Oracle behavior | Graph node(s) |
|---|---|
| `s.ActiveAttackerCount == 0 → Success` fast path + the reverse swap-remove walk | `WaveMonitorOps.Update(s)` (curated kernel — no visual-node form) |
| `s.ActiveAttackerCount` re-check | `WaveMonitorOps.ActiveCount(s)` |
| `count == 0 ? Success : Running` | `Compare(count, 0, Equal) → Branch → {True: Return(Success); False: Return(Running)}` |

Graph: `EventEntry → SetVariable(Wave) [Value ← FunctionCall WaveMonitorOps.Update(s ← GetVariable(Wave))
[IsPure, TrailingContext=View]] → Branch(Condition ← Compare(WaveMonitorOps.ActiveCount(s ← the SAME
Update.Return), Literal Int32 0, Equal)) → True: Return(Success); False: Return(Running)`. The single
`Update.Return` pin fans out to both the `SetVariable` writeback and `ActiveCount` (pure data nodes fan out
freely, proven by `HillAssault2_RequestAreaQuery`), so there is no re-read-before-write hazard.

## `DispatchWaveWithTargets` — oracle line → node mapping

Oracle: `HillAttackCommanderNodes.Action_DispatchWaveWithTargets`, line 287–437.

| Oracle behavior (line) | Graph node(s) |
|---|---|
| `s.WaveUsedSlotsMask = 0; s.ActiveAttackerCount = 0` (290-291) | `SetVariable(Runners ← MemberSlotListOps.Empty())`, `SetVariable(WaveUsedSlotsMask ← 0)` |
| roster loop (332) | `FlowForEach` over `UnitRoster` |
| `packed==0 \|\| !IsAlive \|\| parity \|\| tracker full` skips (333-340) | `WaveDispatchOps.ShouldConsider(sub, trackerCount, rosterCount, currentWave)` → `Branch1` (bundles all 3 skip conditions into one visual condition, Q#8-B/E) |
| inner `avail[]` scan + `Random.Shared.Next` pick (342-354) | `SlotOps.PickRandomFreeSlot(burned, waveUsed, totalSlots, currentWave)` — **deterministic seed**, not `Random.Shared` (Q#8-C) |
| `availCount == 0 → continue` (349-353) | `Compare(firingSlot, -1, NotEqual) → Branch2` |
| firing-slot interpolation (357-359) | `SegmentMath.LerpParam` + `SegmentMath.Lerp` ×2 |
| `PickClosestBaselineSlot` (362) | `SlotOps.PickClosestBaselineSlot(...)` |
| baseline-slot interpolation (379-381) | `SegmentMath.LerpParam` + `SegmentMath.Lerp` ×2 |
| round-robin target resolve (364-376) | `TargetPoolOps.ResolveNetId(cachedEqsRequestId, targetGroupHandle, roundRobinIndex)` |
| SoA tracker write + masks (384-391) | `MemberSlotListOps.AddRunner(...)`, `MaskOps.WithBitSet(...)` ×2 |
| `HullDownAttackParams` JSON + publish (401-426) | `HullDownIntentJson.Build(...)` → `PublishEvent(AssignTacticalIntentEvent, IntentId="HullDownAttack")` |
| EQS cache reset + free + wave flip (429-433) | `SetVariable ×3` + `AreaQueryBatchOps.Free(...)` [EXEC] + `WaveParityOps.NextWave(...)` |
| `return Success` (436) | `Return(Success)` |

Graph shape: `EventEntry → SetVariable(Runners) → SetVariable(WaveUsedSlotsMask) → FlowForEach.In`. Body →
`Branch1(ShouldConsider)` → True → `Branch2(PickRandomFreeSlot != -1)` (**P1b depth 2** — an in-body Branch
nested inside another in-body Branch, both inside the `FlowForEach` body) → True: the dispatch chain above,
ending `PublishEvent → SetVariable(Runners) → SetVariable(WaveUsedSlotsMask) → SetVariable
(BaselineReservedMask)`. Both `Branch1.False` and `Branch2.False` are unwired (tank skipped this wave).
`FlowForEach.Completed → SetVariable(CachedTargetGroupHandle←-1) → AreaQueryBatchOps.Free(CachedEqsRequestId)
[EXEC] → SetVariable(CachedEqsRequestId←-1L) → SetVariable(EqsRequestTime←0f) → SetVariable(CurrentWave←
WaveParityOps.NextWave(CurrentWave)) → Return(Success)`.

A single `GetVariable` per WorkingState field read in the body (`Runners`, `CurrentWave`, `BurnedSlotsMask`,
`WaveUsedSlotsMask`, `TotalSlots`, `BaselineReservedMask`, `CachedEqsRequestId`, `CachedTargetGroupHandle`)
feeds every pre-mutation consumer via pure fan-out — including across both nesting levels — exactly the
pattern `HillAssault2_RequestAreaQuery` already proved safe.

### WorkingState

| Field | Type |
|---|---|
| `Runners` | `Hrot.AI.Behaviors.Brains.MemberSlotList` |
| `WaveUsedSlotsMask`, `BaselineReservedMask`, `BurnedSlotsMask` | `System.UInt16` |
| `CurrentWave` | `System.Byte` |
| `TotalSlots`, `CachedTargetGroupHandle` | `System.Int32` |
| `CachedEqsRequestId` | `System.Int64` |
| `EqsRequestTime` | `System.Single` |

### Parameters (all `System.Single`)

`StartX, StartY, EndX, EndY, BaselineStartX, BaselineStartY, BaselineEndX, BaselineEndY, AttackDirX, AttackDirY`

## Curated kernels (why curated, not visual — architect Q#8-A/B/C/E)

| Helper | Reason curated |
|---|---|
| `WaveMonitorOps.Update/ActiveCount` | reverse swap-remove walk mutating while iterating — no visual-node form (Q#8-A) |
| `MemberSlotListOps.*` | fixed-capacity SoA struct accessors — no raw-array/struct-field node vocabulary; deferred until a 2nd `MemberSlotList` consumer exists (Q#8-A, revises Q#3) |
| `WaveDispatchOps.ShouldConsider` | bundles 3 oracle skip-conditions into 1 visual `Branch`, keeping FlowForEach body nesting at 2 instead of 4 (Q#8-B/E) |
| `SlotOps.PickRandomFreeSlot` | inner scan+pick kernel, **deterministic seed** replacing `Random.Shared` for replay/rollback/headless-proof determinism (Q#8-B/C, mandated) |
| `SlotOps.PickClosestBaselineSlot` | distance² search kernel, no visual-node form (Q#8-E) |
| `TargetPoolOps.ResolveNetId` | round-robin pool probe + alive + `NetworkIdentity` read bundle (Q#8-E) |
| `HullDownIntentJson.Build` | JSON serialization stays in reviewable C#, mirrors `MoveIntentJson` (Q#6-C/Q#8-E) |
| `MaskOps.WithBitSet` | bitwise mask ops have no visual-node form (reused from `DispatchAllToBaseline`) |
| `WaveParityOps.NextWave` | trivial byte flip, reused alongside `ShouldParticipate` (baked into `WaveDispatchOps.ShouldConsider`) |
| `SegmentMath.LerpParam/Lerp` | reused from `DispatchAllToBaseline` |

Both graphs are `Dispatch=AiPrimitive`, `Intent=Action` (not `Condition`) because both must be able to
return the full `Running`/`Success`/`Failure` tri-state — a `Condition`'s bool wrapper
(`TickCore(...)==NodeStatus.Success`) would collapse `Running` to `false`.

## Documented deviations from the oracle

1. **`HasComponent<UnitRoster>(Self)` guard omitted** (`DispatchWaveWithTargets`, oracle line 315-323). The
   `FlowForEach` lowering reads the roster on `self` unconditionally, exactly as
   `HillAssault2_DispatchAllToBaseline` and `HillAssault2_AreAllAtBaseline` already do — the commander
   always has a `UnitRoster` in practice.
2. **`BehaviorLog` diagnostic calls not reproduced** (both graphs) — debug/trace-only side channel, no
   bearing on WorkingState, return status, or published events.
3. **`Random.Shared` replaced by a deterministic seed** in `SlotOps.PickRandomFreeSlot` — architect
   Q#8-C, mandated for replay/rollback/headless-proof determinism. Seed = xorshift of
   `self.Index ^ currentWave ^ (int)SimulationTime`; same inputs → same slot, so proofs assert an exact
   slot rather than set-membership.

## Compiler limitation blocks `DispatchWaveWithTargets`

**Not an asset-authoring issue** — verified the P1b depth-2 nested-branch scheduling itself is correct
(see below); the blocker is orthogonal: an implicit numeric-widening cast the compiler cannot currently
emit.

**Root cause.** WorkingState `CurrentWave` is `System.Byte` (matching the oracle's `byte CurrentWave`).
The already-committed curated helpers `WaveDispatchOps.ShouldConsider`, `SlotOps.PickRandomFreeSlot`, and
`WaveParityOps.NextWave` all declare their `currentWave` parameter as `System.Int32` (per the curated-helper
API given for this task). This mismatch is baked into the given spec on **both** sides — not an authoring
choice, and not fixable by re-typing either side (the WorkingState type must match the oracle; the curated
helper signatures are already committed and out of scope to change).

Reading `GetVariable(CurrentWave):Byte` into an `Int32`-typed argument triggers
`Hrot.Blueprints.Core.Compiler.Stages.Stage3_Normalize.InsertImplicitCasts` (`Stage3_Normalize.cs:249-336`),
which auto-inserts a `CastNode` for the coercion. `Stage5_Schedule.cs`'s `CastNode` case
(`Stage5_Schedule.cs:1959-1977`) then emits:

```csharp
Operation = new IrOp_PureCall($"Cast.{cn.TargetTypeId}", new[] { castInput }, pinType),
```

i.e. a call to `global::Cast.System.Int32(...)` — a static method on a `Cast` class that **does not exist**
anywhere in the codebase (confirmed by search: no `class Cast` / `namespace Cast`). Separately,
`Stage3_Normalize.InsertImplicitCasts` (`Stage3_Normalize.cs:275`) computes a real coercion expression via
`ITypeRegistry.TryGetCoercion(fromIr, toIr, out var coercionExpr)`, but **`coercionExpr` is never used** —
only `toIr.FullName` is threaded into `CastNode.TargetTypeId`. The computed, presumably-correct coercion
expression is silently discarded; Stage5's generic `Cast.{TargetTypeId}` fallback is what actually ships,
and it is unresolvable for any coerced type.

**Exact diagnostic** (`dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj -c Debug`,
reproduced with a full clean rebuild, `--no-incremental`):

```
.../HillAssault2DispatchWaveWithTargets_8CA4BFD9_Bp.g.cs(78,33): error CS0400: The type or namespace name 'Cast' could not be found in the global namespace (are you missing an assembly reference?)
.../HillAssault2DispatchWaveWithTargets_8CA4BFD9_Bp.g.cs(85,37): error CS0400: The type or namespace name 'Cast' could not be found in the global namespace (are you missing an assembly reference?)
.../HillAssault2DispatchWaveWithTargets_8CA4BFD9_Bp.g.cs(139,29): error CS0400: The type or namespace name 'Cast' could not be found in the global namespace (are you missing an assembly reference?)
```

corresponding to the 3 `CurrentWave` reads: `ShouldConsider.currentWave`, `PickRandomFreeSlot.currentWave`,
`NextWave.currentWave`.

**P1b depth-2 nested branches: confirmed working.** Before the 3 `Cast.*` lines were hit, the rest of the
generated `TickCore` was inspected and is structurally and semantically correct — the emitted code is a
`for` loop containing `if (ShouldConsider) { ...; if (firingSlot != -1) { ...dispatch chain... } }`, i.e.
Branch2 correctly nests inside Branch1's `True` arm, both inside the `FlowForEach`'s `for`. Every curated
helper call, `PublishManaged`, and `WithBitSet`/`AddRunner` writeback appears with the correct arguments and
in the correct order matching the authored graph. **The scheduler capability this slice needed (in-body
Branch nested inside another in-body Branch, inside a FlowForEach body) works.** The only defect is the
unrelated `byte→int` `CastNode` emission.

**Likely fix** (compiler team, out of scope here): thread `coercionExpression` from
`Stage3_Normalize.InsertImplicitCasts` through `CastNode` (e.g. a new `CoercionExpression` field) and have
`Stage5_Schedule`'s `CastNode` case emit it directly (e.g. `(int)(...)` / `Convert.ToInt32(...)`) instead of
synthesizing `Cast.{TargetTypeId}`.

**Current repo state:** `HillAssault2_DispatchWaveWithTargets.bp.json` is fully authored and correct per
this design, parked at
`Hrot/Subsystems/Hrot.AI.Behaviors/Assets/Blueprints/HillAssault2_DispatchWaveWithTargets.bp.json.pending-CS0400-cast-bug`
(filename does not match the `Assets\Blueprints\**\*.bp.json` build glob, so it does not block any other
blueprint). `HillAssault2_DispatchWaveWithTargets_ProofTests.cs` is fully authored with `[Fact(Skip=...)]`
on all three facts, citing this document. **To reactivate:** rename the asset back to
`HillAssault2_DispatchWaveWithTargets.bp.json` and remove the `Skip` arguments, once the compiler-side fix
above lands.
