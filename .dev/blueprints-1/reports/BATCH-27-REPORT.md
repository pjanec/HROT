# BATCH-27 Report

## Status: COMPLETE

All six flaws fixed (Flaw 1-6). All tests T1-T5d pass.
Build: 0 C# errors, 2 pre-existing xUnit2029 warnings.
Test results: 490 passed, 0 failed, 7 skipped (total 497).

Note: `dotnet build` exits with code 1 due to CycloneDDS.NET post-build IDL file-copy
errors unrelated to this batch (the errors pre-date this batch and affect only
`.idl` artifact copying, not C# compilation). All C# compilation succeeds; all
tests pass when run with `--no-build` against the freshly compiled DLLs.

---

## Flaw 1 (P1) — Delete ghost stub `BlueprintCompiler.cs`

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintCompiler.cs` (deleted)

### Changes
Deleted the Phase-1 stub file at the Core root. The class was in namespace
`Hrot.Blueprints.Core`, threw `NotImplementedException`, and was entirely superseded
by the real compiler at `Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs`.
No callers referenced it.

---

## Flaw 2 (P2) — Populate the three static catalogs

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInEngineEventCatalog.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInWaitPrimitiveCatalog.cs`

### Changes

**BuiltInEngineEventCatalog**: Populated with 3 entries referencing real event types:
- `"HitEvent"` — `typeof(HitEvent)` from `Fdp.Toolkit.Combat.Contracts`
- `"BehaviorFinishedEvent"` — `typeof(BehaviorFinishedEvent)` from `Fdp.Toolkit.Behavior.Events`
- `"TargetVisibleEvent"` — `typeof(TargetVisibleEvent)` from `Fdp.Toolkit.Perception.Events`

**BuiltInChannelCommandCatalog**: Populated with 5 entries using simple ActionId strings
(see Deviation 1 below):
- `"MoveTo"` / `LocomotionChannel` / `NavigationConstants.ActionIdMoveTo`
- `"FollowRoute"` / `LocomotionChannel` / `NavigationConstants.ActionIdFollowRoute`
- `"AimAndFire"` / `WeaponChannel` / `CombatConstants.ActionIdAimAndFire`
- `"OpenDoor"` / `InteractionChannel` / `BehaviorConstants.ActionIdOpenDoor`
- `"EjectPassengers"` / `InteractionChannel` / `BehaviorConstants.ActionIdEjectPassengers`

**BuiltInWaitPrimitiveCatalog**: Populated with 5 entries:
- `"WaitForChannel:Locomotion"` — `typeof(WaitForChannelNode)` / `typeof(LocomotionChannel)`
- `"WaitForChannel:Weapon"` — `typeof(WaitForChannelNode)` / `typeof(WeaponChannel)`
- `"WaitForChannel:Interaction"` — `typeof(WaitForChannelNode)` / `typeof(InteractionChannel)`
- `"WaitForEvent:BehaviorFinishedEvent"` — `typeof(WaitForEventNode)` / `typeof(BehaviorFinishedEvent)`
- `"WaitForRingBufferResult:Pathfinding"` — `typeof(WaitForRingBufferResultNode)` / `typeof(PathfindingRingBuffer)`

---

## Flaw 3 (P1) — Move `BlueprintDebugSession` from Core to Editor

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs` (deleted)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (created)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/ExecutionHistory.cs` (accessibility change — see Deviation 3)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` (accessibility change — see Deviation 4)

### Changes
The concrete `BlueprintDebugSession` class was moved from Core to Editor. The namespace
`Hrot.Blueprints.Core.Debug` was preserved for a transparent move (Editor already uses
Core types in that namespace). The class implements `IBlueprintDebugSession` and
`IBlueprintProbeSink`. Full implementation of Flaw 4 and Flaw 5 was incorporated
during the move.

---

## Flaw 4 (P2) — Implement `Detach()`

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`

### Changes
`Detach()` was fully implemented (previously was a stub with `throw NotImplementedException`):
- Calls `Continue()` if currently paused (via `IsPaused` check)
- Replaces `_sink` with `NullProbeSink.Instance`
- Clears `_breakpoints`, `_watches`, and `_entityHistory`
- Sets `IsPaused = false`

---

## Flaw 5 (P2) — Fire `OnNodeExecuted` from `OnNodeEnter` and implement `GetRecentNodeHistory`

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`

### Changes
`IBlueprintProbeSink.OnNodeEnter` implementation now raises the `OnNodeExecuted` event
immediately after recording the node entry in `ExecutionHistory`. The `NodeExecuted`
constructor is invoked with all 6 required parameters:
`(self, Guid.Empty, Guid.Empty, nodeId, _view.Time, _view.Tick)`.

`GetRecentNodeHistory(int maxCount = 100)` was implemented by aggregating
`ExecutionHistory.GetRecent(maxCount)` across all tracked entities in `_entityHistory`
and returning a sorted-by-tick flat list capped at `maxCount`.

---

## Flaw 6 (P3) — Fix `Watch.WriteValue<T>` unsafe ref

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`

### Changes
`Watch.WriteValue<T>` changed from `ref _valueBuffer[0]` to
`ref MemoryMarshal.GetArrayDataReference(_valueBuffer)`, eliminating the bounds check
on an already-sized array.

---

## Tests added

### T1 — `Detach_ClearsAllStateAndNullsProbe`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugSessionInterfaceTests.cs`

Verifies that after `Detach()`:
- The probe sink is replaced with `NullProbeSink`
- Breakpoints list is empty
- Watches list is empty
- `IsPaused == false`

### T2 — `Detach_CallsContinue_WhenPaused`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugSessionInterfaceTests.cs`

Verifies that `Detach()` calls `Continue()` when the session is paused, and that
`IsPaused == false` after detach.

### T3 — `OnNodeExecuted_FiredOnNodeEnter`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/NodeHistoryTests.cs`

Verifies that the `IBlueprintDebugSession.OnNodeExecuted` event fires when
`IBlueprintProbeSink.OnNodeEnter` is called, with correct `NodeIdString` and `Self`.

### T4 — `GetRecentNodeHistory_ReturnsAggregatedHistory`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/NodeHistoryTests.cs`

Verifies that `GetRecentNodeHistory(100)` returns all 3 entries when `OnNodeEnter`
was called for two distinct entities (E1, E2, E1).

### T5a — `EngineEventCatalog_ContainsExpectedEntries`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/CatalogTests.cs`

Verifies `BuiltInEngineEventCatalog` has at least 2 entries including `"HitEvent"`
and `"BehaviorFinishedEvent"`.

### T5b — `ChannelCommandCatalog_ContainsExpectedEntries`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/CatalogTests.cs`

Verifies `BuiltInChannelCommandCatalog` has at least 2 entries including `"MoveTo"`
and `"AimAndFire"` (see Deviation 1 and Deviation 5 below).

### T5c — `WaitPrimitiveCatalog_ContainsExpectedEntries`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/CatalogTests.cs`

Verifies `BuiltInWaitPrimitiveCatalog` has at least 2 entries including
`"WaitForChannel:Locomotion"` and `"WaitForEvent:BehaviorFinishedEvent"`.

### T5d — `Stage2_ValidatesChannelCommand_WhenCatalogIsPopulated`
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/CatalogTests.cs`

Verifies that compiling an AiPrimitive with a `ChannelCommandNode` using
`ChannelType="NonExistent"` / `ActionId="UnknownAction"` against the populated catalog
yields `result.Succeeded == false` and diagnostic code `BP1401`.
The asset is built with `WithHostings(AiPrimitiveHosting.BTreeAction)` to pass
`V_DispatchKindCompatibility` (see Deviation 2 below) before `V_ChannelCommandReferences` runs.

---

## Deviations from instructions

### Deviation 1 — Catalog entry names use simple ActionId strings, not hierarchical paths

**Instructions specified:** `"Locomotion/MoveTo"`, `"Weapon/AimAndFire"`, etc.

**Actual implementation:** `"MoveTo"`, `"AimAndFire"`, etc.

**Reason:** Stage-2 validator `V_ChannelCommandReferences` checks `e.Name == node.ActionId`
(see `Stage2_Validate.cs` around line 469). The `ActionId` stored in JSON blueprint
assets for real scenarios (`MoveToAndFire`, etc.) is the plain action ID string
(`"MoveTo"`, not `"LocomotionChannel.MoveTo"` or `"Locomotion/MoveTo"`). Using
hierarchical names would break 18 existing passing tests by causing false-positive
BP1401 diagnostics on all real scenario blueprints. Simple names match the actual
validator contract.

### Deviation 2 — T5d uses `WithHostings(AiPrimitiveHosting.BTreeAction)`

**Instructions did not mention hostings.**

**Reason:** `V_DispatchKindCompatibility` (Stage-2 validator, runs before
`V_ChannelCommandReferences`) emits a fatal error BP1021 ("AiPrimitive must declare
at least one hosting") when `Hostings` is empty. Because Stage-2 stops on fatal errors,
`V_ChannelCommandReferences` would never run without a valid hosting. Adding
`.WithHostings(AiPrimitiveHosting.BTreeAction)` makes the asset structurally valid
so the catalog validator executes.

### Deviation 3 — `ExecutionHistory` changed from `internal` to `public`

**Instructions did not specify this change.**

**Reason:** Moving `BlueprintDebugSession` to the `Hrot.Blueprints.Editor` assembly
required `ExecutionHistory` (used inside `BlueprintDebugSession`) to be accessible
from outside Core. Making it `public` is the minimal change; it is still a sealed
implementation class with no public API surface risk.

### Deviation 4 — `Watch.WriteValue<T>` and `Watch.IsStale` setter changed to `public`

**Instructions did not specify these accessibility changes.**

**Reason:** Same as Deviation 3 — `BlueprintDebugSession` in Editor assembly calls
both members directly. `internal` is scoped to the assembly declaring the type
(`Hrot.Blueprints.Core`), so Editor code cannot access `internal` members.

### Deviation 5 — T5b asserts `"MoveTo"` and `"AimAndFire"`, not `"Locomotion/MoveTo"` and `"Weapon/AimAndFire"`

**Instructions specified:** assert that catalog contains `"Locomotion/MoveTo"` and `"Weapon/AimAndFire"`.

**Reason:** Follows from Deviation 1 — the catalog uses simple names, so the test
assertions must match.

### Deviation 6 — `HitEvent` namespace is `Fdp.Toolkit.Combat.Contracts`, not `Fdp.Core.Events`

**Instructions referenced** `Fdp.Core.Events.HitEvent`.

**Actual location:** `Fdp.Toolkit.Combat.Contracts.HitEvent` (moved in DEBT-031).

**Reason:** The type was relocated prior to this batch. Using the actual current
namespace is required for compilation.

---

## Final test results

```
Passed: 490   Failed: 0   Skipped: 7   Total: 497
```
