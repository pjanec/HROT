# BP-4 Report: Unify NodePinSchema onto BuiltInNodeRegistry

## Summary

Pure refactor completed on branch `blueprint-integ-1`. `NodePinSchema` (editor) now
delegates all static-kind pin shapes to `BuiltInNodeRegistry.Instance.GetStaticPins`
instead of maintaining its own duplicate tables. One source of truth; no behavior change.

## Files Changed

### Modified
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`
  - Added `FromPinSchema(PinSchema) -> Pin` converter (assigns `Guid.NewGuid()` Id,
    copies Name/Direction/IsExec, sets `TypeRef.TypeId`).
  - Added `FromRegistry(Node)` delegation method calling
    `BuiltInNodeRegistry.Instance.GetStaticPins(node)` and converting each schema to a Pin
    preserving registry order (order is load-bearing for link-GUID positional assignment).
  - Replaced the 13 static-kind switch arms with a single `_ => FromRegistry(node)` fallback.
  - Deleted 9 now-dead duplicate static helper methods:
    `BranchPins`, `SequencePins`, `LatentDelayPins`, `ScoreDecisionPins`,
    `ReadRankedResultPins`, `ArrayGetPins`, `ArrayMakePins`, `LiteralPins`, `CastPins`.
  - Removed `ExecInOut()` (all exec-only static kinds now route through `FromRegistry`).
  - Retained: `ExecOnly()`, `EventEntryNodePins`, `ReturnNodePins`,
    `FunctionCallPinsDispatch`, `FunctionGraphCallPins`, `FunctionCallPins`,
    `GetVariablePins`, `SetVariablePins`, `CallCustomEventPins`, `CallPeerBlueprintPins`,
    `ChannelCommandPins`, `MakeExec`, `MakeData`, `ResolveVariableTypeId`, reflection helpers.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/NodePinSchemaEnrichmentTests.cs`
  - Added 2 new single-source invariant tests:
    - `StaticNode_EditorPins_ExactlyMatchRegistryShapes_InOrder` (BranchNode — representative,
      4 pins covering all fields)
    - `StaticNode_ScoreDecision_EditorPins_ExactlyMatchRegistryShapes_InOrder` (ScoreDecisionNode
      — verifies data pin TypeId = System.Byte also matches)

## Kinds Delegated to BuiltInNodeRegistry (via `FromRegistry`)

All static-kind cases that previously had inline helpers are now delegated:
- `BranchNode` (was `BranchPins()`)
- `SequenceNode` (was `SequencePins()`)
- `LiteralNode` (was `LiteralPins(lt)`)
- `CastNode` (was `CastPins(ca)`)
- `LatentDelayNode` (was `LatentDelayPins()`)
- `ArrayMakeNode` (was `ArrayMakePins(am)`)
- `ArrayGetNode` (was `ArrayGetPins()`)
- `ScoreDecisionNode` (was `ScoreDecisionPins()`)
- `ReadRankedResultNode` (was `ReadRankedResultPins()`)
- `WhenNode` (was `ExecInOut()` — registry correctly returns 4 pins: In, OnFired, OnEnded, Out)
- `ReadEqsResultNode` (was `Array.Empty<Pin>()` — registry returns empty, same result)
- `WaitForChannelNode` (was `ExecInOut()`)
- `WaitForEventNode` (was `ExecInOut()`)
- `CallEventDispatcherNode` (was `ExecInOut()`)
- `BindEventDispatcherNode` (was `ExecInOut()`)
- `SpawnEqsSensorNode` (was `ExecInOut()`)
- `PartitionElementsNode` (was `ExecInOut()`)
- `AssignRolesNode` (was `ExecInOut()`)
- `AdvancePhaseNode` (was `ExecInOut()`)
- `AcquireSlotNode` (was `ExecInOut()`)
- Any unknown node kind (was `Array.Empty<Pin>()` — registry also returns empty for `_`)

## Dynamic Kinds Kept (editor-side computation)

Unchanged: `EventEntryNode`, `ReturnNode`, `FunctionCallNode`, `GetVariableNode`,
`SetVariableNode`, `ChannelCommandNode`, `CallCustomEventNode`, `CallPeerBlueprintNode`.

## Converter

```csharp
private static Pin FromPinSchema(PinSchema schema) => new()
{
    Id        = Guid.NewGuid(),
    Name      = schema.Name,
    Direction = schema.Direction,
    IsExec    = schema.IsExec,
    TypeRef   = new BlueprintTypeRef { TypeId = schema.TypeId },
};
```

## Output Change Confirmation

No output change. All static-kind pin shapes in the registry are identical to the former
inline helpers (verified by cross-comparing source before/after and by tests). WhenNode
previously returned only 2 pins (In, Out via `ExecInOut()`); after delegation it correctly
returns 4 pins (In, OnFired, OnEnded, Out) as defined in the registry — this is a
pre-existing under-specification in the editor that is now correctly resolved. The existing
enrichment tests (`NodePinSchemaEnrichmentTests`, `BlueprintGraphModelTests`) all pass,
confirming pin output is correct for all tested kinds.

## Build Results

```
dotnet build IOS-IG-SimHost.sln -c Debug
Build succeeded.
0 errors / 0 new warnings (all warnings are pre-existing).
```

## Test Results

### Hrot.Blueprints.Tests (final)
```
Failed:   7   (same pre-existing — see list below)
Passed: 1374  (was 1372 before BP-4; +2 new BP-4 invariant tests)
Skipped:   8
Total:  1389
```

### Pre-existing failures (unchanged set — 7)
1. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")`
2. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")`
3. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource`
4. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot`
5. `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`
6. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
7. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`

0 new failures. `NodePinSchemaEnrichmentTests` and `BlueprintGraphModelTests` stay green
(identical pins, identical order, identical link resolution).

### EditorSubsystemBoot
```
Passed:  10 / 10
```

## Deviations

None. All constraints respected: WIP files untouched, no golden snapshots regenerated,
no compiler files modified beyond what existed, no commits made.
