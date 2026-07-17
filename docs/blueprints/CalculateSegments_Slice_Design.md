# Slice: `Action_CalculateSegments` (design)

> Migration slice (P1b). Rebuild the oracle `HillAttackCommanderNodes.Action_CalculateSegments` as a
> blueprint. Uses only **shipped** capabilities (`GetParameter`, `SetVariable`, `Literal`, `Return`) + **one
> small curated helper** for the distance/clamp/spacing-default math. No new compiler node/IR. No architect
> gate (authoring slice of shipped nodes, mirrors slice 2's `VectorOps` precedent); this note is the
> in-repo design gate.

## Oracle (ground truth — `HillAttackCommanderNodes.cs:49-71`)

Unconditional setup action on `HillAttackMutableState` (4-param stateful form): computes the firing-line
slot count from the `Start`/`End` segment and `TankSpacing`, then zeroes/initializes eight more mutable
fields. Always returns `Success`.

## Oracle line → blueprint node mapping

| Oracle line | Blueprint node |
|---|---|
| `start=(p.StartX,p.StartY); end=(p.EndX,p.EndY)` | 4× `GetParameter` (StartX, StartY, EndX, EndY) → in to `FunctionCall` |
| `spacing = p.TankSpacing>0 ? p.TankSpacing : 30` | `GetParameter`(TankSpacing) → in to `FunctionCall` |
| `segLen=Distance(...); totalSlots=clamp(max(1,(int)(segLen/spacing)),1,16)` | curated `FunctionCall SegmentMath.TotalSlots(startX,startY,endX,endY,rawSpacing)` (pure) |
| `s.TotalSlots = totalSlots` | `SetVariable(TotalSlots)` ← `FunctionCall.Return` |
| `s.BurnedSlotsMask = 0` | `SetVariable(BurnedSlotsMask)` ← `Literal(System.UInt16, "(ushort)0")` |
| `s.WaveUsedSlotsMask = 0` | `SetVariable(WaveUsedSlotsMask)` ← `Literal(System.UInt16, "(ushort)0")` |
| `s.BaselineReservedMask = 0` | `SetVariable(BaselineReservedMask)` ← `Literal(System.UInt16, "(ushort)0")` |
| `s.ActiveAttackerCount = 0` | `SetVariable(ActiveAttackerCount)` ← `Literal(System.Int32, "0")` |
| `s.CurrentWave = 0` | `SetVariable(CurrentWave)` ← `Literal(System.Byte, "(byte)0")` |
| `s.CachedEqsRequestId = -1` | `SetVariable(CachedEqsRequestId)` ← `Literal(System.Int64, "-1L")` |
| `s.CachedTargetGroupHandle = -1` | `SetVariable(CachedTargetGroupHandle)` ← `Literal(System.Int32, "-1")` |
| `s.EqsRequestTime = 0f` | `SetVariable(EqsRequestTime)` ← `Literal(System.Single, "0f")` |
| `return NodeStatus.Success` | `Return(Success)` |

Exec chain: `EventEntry → SetVariable(TotalSlots) → ... → SetVariable(EqsRequestTime) → Return`.

## WorkingState fields (`HillAttackMutableState` subset touched)

| Field | C# type | Blueprint TypeId |
|---|---|---|
| TotalSlots | int | `System.Int32` |
| BurnedSlotsMask | ushort | `System.UInt16` |
| WaveUsedSlotsMask | ushort | `System.UInt16` |
| BaselineReservedMask | ushort | `System.UInt16` |
| ActiveAttackerCount | int | `System.Int32` |
| CurrentWave | byte | `System.Byte` |
| CachedEqsRequestId | long | `System.Int64` |
| CachedTargetGroupHandle | int | `System.Int32` |
| EqsRequestTime | float | `System.Single` |

## The one design point: curated helper `SegmentMath.TotalSlots`

`Vector2.Distance` + the `TankSpacing>0 ? TankSpacing : 30` conditional default + `Math.Max(1, (int)(...))`
+ clamp-to-16 has no visual-node expression (architect Q#6-A: non-trivial math stays in reviewable curated
helpers; only plain arithmetic is a visual `BinaryOp`). Added `Hrot.AI.Behaviors.Brains.SegmentMath`
(reflection-free, contextless, pure — mirrors the `VectorOps`/`UnitRosterOps` off-graph-helper shape):

```csharp
public static class SegmentMath
{
    public static int TotalSlots(float startX, float startY, float endX, float endY, float rawSpacing);
}
```

Called via `FunctionCall` (`IsPure=true`, `TrailingContext=None`), In pins `startX/startY/endX/endY/rawSpacing`
wired from the five `GetParameter` reads, `Return` (`System.Int32`) wired into `SetVariable(TotalSlots)`.

## Literal `ValueJson` gotcha (non-Int32/Single numeric types)

The compiler splices `Literal.ValueJson` verbatim into `var __tN = <ValueJson>;` with no type-aware
cast/suffix (only `Stage3_Normalize.FormatDefaultLiteral`, used for *synthesized* unconnected-pin defaults,
does that). A bare `"0"`/`"-1"` C#-infers to `int`, which fails to compile against a `ushort`/`byte`/`long`
target field. `UInt16`/`Byte`/`Int64` literals in this asset are therefore authored as full C# literal
expressions: `"(ushort)0"`, `"(byte)0"`, `"-1L"` — confirmed in the generated `TickCore` (`var __t6 =
(ushort)0; ws.BurnedSlotsMask = __t6;` etc.).

## Proof

`HillAssault2_CalculateSegments.bp.json` + `HillAssault2_CalculateSegments_ProofTests.cs` (real generator).
Source-inspection: generated `TickCore` contains `SegmentMath.TotalSlots(`, `p.StartX`, `p.TankSpacing`.
Behavioral: Start=(0,0), End=(100,0), TankSpacing=10 → distance 100 → `TotalSlots=10`; asset returns
`Success`; all nine `WorkingState` fields match the oracle's post-tick values (`TotalSlots=10`,
`BurnedSlotsMask/WaveUsedSlotsMask/BaselineReservedMask/ActiveAttackerCount/CurrentWave=0`,
`CachedEqsRequestId/CachedTargetGroupHandle=-1`, `EqsRequestTime=0f`). Not reproduced: the oracle's
`BehaviorLog.IsDebugEnabled` diagnostic log call (debug-only side channel, no behavioral effect). Oracle
untouched.
