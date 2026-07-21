# Slice: `Action_DispatchAllToBaseline` (design)

> Migration slice (P1b). Rebuild the oracle `HillAttackCommanderNodes.Action_DispatchAllToBaseline`
> (lines 79-128) as a blueprint. Uses shipped nodes (`FlowForEach` with loop-introspection out-pins,
> in-body `Branch`, `PublishEvent` against a `Managed` catalog entry, `FunctionCall`) + four small
> curated helpers. No new compiler node/IR — all capabilities (FlowForEach `CurrentIndex`/`Count`,
> `AssignTacticalIntentEvent` catalog entry, `WorldOps.IsAlive`) were already shipped by prior slices.
> This note is the in-repo design gate (mirrors the CalculateSegments/AreAllAtBaseline precedent — a
> mirror-pattern slice combining already-proven capabilities, not a new one).

## Oracle (ground truth — `HillAttackCommanderNodes.cs:79-128`)

Reads the commander's `UnitRoster`, zeroes `BaselineReservedMask`, then for each subordinate: unpacks
the `Entity`, skips if `packed==0` or not alive, interpolates a baseline position, publishes
`AssignTacticalIntentEvent{IntentId="MoveToLocation"}` with the serialized `MoveToLocationParams`
JSON, and (for the first 16 slots) sets the corresponding bit in `BaselineReservedMask`. Always
returns `Success`.

## Oracle line → blueprint node mapping

| Oracle line | Blueprint node |
|---|---|
| `if (!HasComponent<UnitRoster>(Self)) return Success` | **omitted** — see Deviation 1 |
| `s.BaselineReservedMask = 0` | `EventEntry → SetVariable(BaselineReservedMask)` ← `Literal(System.UInt16, "(ushort)0")` |
| `int count = roster.Count` / `for (i=0; i<count; i++)` | `FlowForEach` (SourceComponentFqn=`UnitRoster`, CountAccessorFqn/ItemAccessorFqn=`UnitRosterOps.Count`/`.Subordinate`), explicit out-pins `CurrentItem`(Entity)/`CurrentIndex`(Int32)/`Count`(Int32) |
| `packed==0 continue` + `!IsAlive(sub) continue` | in-body `Branch(Condition ← FunctionCall WorldOps.IsAlive(e ← CurrentItem))` — **one** branch covers **both** oracle `continue`s (see below) |
| `t = count>1 ? i/(count-1) : 0.5f` | curated `FunctionCall SegmentMath.LerpParam(index ← CurrentIndex, count ← Count)` (pure) |
| `bx = BaselineStartX + (BaselineEndX-BaselineStartX)*t` | curated `FunctionCall SegmentMath.Lerp(a ← GetParameter(BaselineStartX), b ← GetParameter(BaselineEndX), t ← LerpParam.Return)` |
| `by = BaselineStartY + (BaselineEndY-BaselineStartY)*t` | curated `FunctionCall SegmentMath.Lerp(a ← GetParameter(BaselineStartY), b ← GetParameter(BaselineEndY), t ← the SAME LerpParam.Return)` |
| `dto = {X=bx,Y=by,Speed=15,ArrivalRadius=5}; json = JsonSerializer.Serialize(dto, ...)` | curated `FunctionCall MoveIntentJson.Build(x ← Lerp_bx.Return, y ← Lerp_by.Return, speed ← Literal(15f), arrivalRadius ← Literal(5f))` |
| `World.Bus.PublishManaged(new AssignTacticalIntentEvent{Entity=sub, IntentId="MoveToLocation", JsonParams=json})` | `PublishEvent(EventId="AssignTacticalIntentEvent"; Target ← CurrentItem; IntentId ← Literal(System.String, "\"MoveToLocation\""); JsonParams ← MoveIntentJson.Build.Return)` |
| `if (i<16) s.BaselineReservedMask |= (ushort)(1<<i)` | curated `FunctionCall MaskOps.WithBitSet(mask ← GetVariable(BaselineReservedMask), index ← CurrentIndex)` → `SetVariable(BaselineReservedMask)` |
| `BehaviorLog.Debug(...)` (×2) | **not reproduced** — see Deviation 2 |
| `return NodeStatus.Success` | `FlowForEach.Completed → Return(Success)` |

Exec chain: `EventEntry → SetVariable(BaselineReservedMask=0) → FlowForEach.In`. Inside the loop body:
`FlowForEach.Body → Branch.In`; `Branch.True → PublishEvent → SetVariable(BaselineReservedMask)`
(chain ends there — the arm has no further exec successor); `Branch.False` is **unwired**. After the
loop: `FlowForEach.Completed → Return(Success)`.

## Parameters

| Name | Blueprint TypeId |
|---|---|
| BaselineStartX | `System.Single` |
| BaselineStartY | `System.Single` |
| BaselineEndX | `System.Single` |
| BaselineEndY | `System.Single` |

## WorkingState

| Field | C# type | Blueprint TypeId | Default |
|---|---|---|---|
| BaselineReservedMask | ushort | `System.UInt16` | `"0"` |

## Curated helpers used

| Helper | Signature | Why curated (not visual) |
|---|---|---|
| `SegmentMath.LerpParam` | `(int index, int count) → float` | The `count>1?i/(count-1):0.5` ternary guarding the `count-1` divisor has no visual-node form (architect Q#6-A). |
| `SegmentMath.Lerp` | `(float a, float b, float t) → float` | Plain `a+(b-a)*t` — bundled with its `LerpParam` sibling as one reviewable "baseline interpolation" helper rather than a `BinaryOp` chain, to keep the dispatch graph tractable. |
| `MoveIntentJson.Build` | `(float x, float y, float speed, float arrivalRadius) → string` | DTO construction + `JsonSerializer.Serialize` has no visual-node form (architect Q#6-C) — the graph only ever sees the resulting opaque `string`, published verbatim via `PublishEvent`. |
| `MaskOps.WithBitSet` | `(ushort mask, int index) → ushort` | Bitwise `mask | (1<<index)` plus the `index<16` guard — bitwise composition has no visual-node form (architect Q#6-A keeps boolean/bit composition off-graph). |
| `WorldOps.IsAlive` *(reused, not new)* | `(Entity e, ISimulationView view) → bool` | Already introduced by the AimAndFireSpecific slice; P7 context-aware `FunctionCall` (`TrailingContext=View`), one authored `e` pin, `view` auto-appended by Stage5. |

## The subtle part: one `Branch` covers both oracle `continue`s

The oracle has two separate skip conditions (`packed==0` and `!IsAlive(sub)`), but a `packed==0` slot
unpacks to `new Entity((ulong)0)` — `Entity.Null`, which `EntityRepository.IsAlive` always reports as
`false`. So a single `Branch(WorldOps.IsAlive(CurrentItem))` subsumes both cases: dead entities and
zero-packed slots both take the (unwired) False arm and are skipped identically to the oracle. No
second `Branch`/`IsNull` check is needed.

## Loop-introspection out-pins

`FlowForEach.CurrentIndex` (0-based, body-scoped) feeds both `SegmentMath.LerpParam.index` and
`MaskOps.WithBitSet.index`; `FlowForEach.Count` (loop-invariant, hoisted outer-scope) feeds
`SegmentMath.LerpParam.count`. Both are authored as explicit `Pins` entries on the `FlowForEach` node
(mirroring how `HillAssault2_AreAllAtBaseline.bp.json` already authors `CurrentItem`) — Stage5 only
binds a loop-introspection pin when the asset actually authors + wires it, so this costs nothing on
slices that don't need it.

## `PublishEvent` → managed catalog entry

`AssignTacticalIntentEvent` is the **only** `Managed:true` entry in `BuiltInEngineEventCatalog` (it
carries managed `string` fields `IntentId`/`JsonParams`, so it must go through
`IEventBus.PublishManaged<T>`, not the struct-event `Publish<T>` path). `PublishEvent`'s `Target` pin
maps to the catalog's `TargetFieldName="Entity"`; every other authored data-in pin (`IntentId`,
`JsonParams`) maps to the event field of the same name. Confirmed in the generated `TickCore`:
`world.Bus.PublishManaged(new global::...AssignTacticalIntentEvent{ Entity = __t3, IntentId = __t7,
JsonParams = __t17 });`.

## `Literal` string gotcha

`Literal.ValueJson` is spliced verbatim into `var __tN = <ValueJson>;` — a `System.String` literal
must therefore carry the JSON string `"\"MoveToLocation\""` (JSON-escaped double quotes) so the
generated C# reads `var __t7 = "MoveToLocation";`, not a bare (unparseable) `MoveToLocation`.

## Deviations (documented)

1. **`HasComponent<UnitRoster>(Self)` guard omitted.** The `FlowForEach` lowering reads
   `GetComponentRO<UnitRoster>` on self unconditionally — exactly as `HillAssault2_AreAllAtBaseline`
   already does. The commander always carries a `UnitRoster` in practice; the proof test adds it
   explicitly, so the guard's absence has no observable effect on the modeled scenarios.
2. **`BehaviorLog.IsDebugEnabled` diagnostic logs not reproduced.** Debug-only side channel with no
   bearing on `WorkingState`/return-status/published-event behavior.

Does not modify the C# oracle (`HillAttackCommanderNodes.Action_DispatchAllToBaseline`).

## Proof

`HillAssault2_DispatchAllToBaseline.bp.json` + `HillAssault2_DispatchAllToBaseline_ProofTests.cs`
(real Roslyn generator, part of `Hrot.AI.Behaviors`'s own build). Source-inspection: generated
`TickCore` contains `PublishManaged`, `SegmentMath.LerpParam(`, `SegmentMath.Lerp(`,
`MoveIntentJson.Build(`, `MaskOps.WithBitSet(`, `WorldOps.IsAlive(`, `"MoveToLocation"`, and the
in-body `if (` nested after the `for (` header (P1b). Behavioral: 3 alive subordinates, baseline
`(0,0)→(30,0)` → 3 `AssignTacticalIntentEvent`s read back via `world.Bus.ReadManaged<...>()` after
`SwapBuffers()`, each `IntentId=="MoveToLocation"`, `Entity` matching the corresponding subordinate,
`JsonParams` containing the interpolated `X` (`0`/`15`/`30`); `BaselineReservedMask==0b111` after the
tick. A second test covers the empty-roster vacuous-success path (no events, mask stays `0`).
