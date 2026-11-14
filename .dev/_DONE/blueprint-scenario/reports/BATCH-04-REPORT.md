# BATCH-04 Implementation Report — Runtime Mutation Events + Consuming System (BSA-301)

**Date:** 2026-06-09  
**Branch:** blueprint-integ-1  
**Author:** pjanec  
**Batch:** BATCH-04 — Runtime Mutation Events + Consuming System (BSA-301)

---

## Summary

Created 3 unmanaged event structs (`AttachInstanceBlueprintEvent`, `RemoveInstanceBlueprintEvent`, `ReplaceInstanceBlueprintEvent`) with unique `[EventId]` values, a `BlueprintConstants` class for the EventId constants, and `BlueprintEventIngressSystem` — an Input-phase ECS system that drains the events and applies them via the BSA-102 core attach/detach seam with remove-before-add ordering (Design §7). Registered the system in `CgfSubsystem` alongside `BlueprintMaterializationSystem`. All 18 test methods (covering 7 specified test scenarios) pass; 0 net-new failures in the touched test projects.

---

## Files Changed

| File | Action | Description |
|------|--------|-------------|
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintConstants.cs` | **NEW** | EventId constants (9100, 9101, 9102) |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/Events/BlueprintLifecycleEvents.cs` | **NEW** | 3 event structs with `[EventId]` attributes |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintEventIngressSystem.cs` | **NEW** | Input-phase consuming system (82 lines) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintEventIngressSystemTests.cs` | **NEW** | 18 test methods covering 7 specified scenarios |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | **EDIT** | Added using + system registration (line 364) |

---

## Q1: Is `ReadManaged<T>()` consuming or non-consuming? How did you handle the Replace event's two-phase processing?

### Finding: Used native (unmanaged) event path instead of managed

The `[EventId]` attribute is required by `EventType<T> where T : unmanaged`, which drives the **native** (unmanaged) event path (`Publish<T>()` / `Read<T>()` returning `ReadOnlySpan<T>`). The managed event path (`PublishManaged<T>()` / `ReadManaged<T>()`) uses `typeof(T).FullName.GetHashCode()` for type identification and ignores `[EventId]`.

Since our events are unmanaged structs (`Entity` is a readonly struct of `int Index` + `ushort Generation`, both unmanaged types), the native path is architecturally correct:

- **`Read<T>()` returns `ReadOnlySpan<T>` from the read buffer** — non-consuming within a frame. The double-buffered `NativeEventStream<T>` swaps buffers only at end-of-frame (`SwapBuffers()`). Multiple calls to `Read<T>()` within the same frame return the same data.
- **Two-phase processing:** `Read<ReplaceInstanceBlueprintEvent>()` is called **twice** — once in Phase 1 (detach old `BlueprintId`) and once in Phase 2 (attach new `BlueprintId`). Both calls return the same events because the read buffer is stable between `SwapBuffers()` calls.
- **No null checks needed** — struct values are never null (unlike `IReadOnlyList<T>` which can contain null entries).

### Code pattern in the system:

```csharp
// Phase 1: Removes first
foreach (var evt in repo.Bus.Read<RemoveInstanceBlueprintEvent>())
    BlueprintInstanceService.DetachFromEntity(repo, evt.BlueprintId, evt.Entity);

foreach (var evt in repo.Bus.Read<ReplaceInstanceBlueprintEvent>())
    BlueprintInstanceService.DetachFromEntity(repo, evt.OldBlueprintId, evt.Entity);

// Phase 2: Attaches after
foreach (var evt in repo.Bus.Read<AttachInstanceBlueprintEvent>())
    BlueprintInstanceService.AttachToEntity(repo, _registry, evt.BlueprintId, evt.Entity);

foreach (var evt in repo.Bus.Read<ReplaceInstanceBlueprintEvent>())
    BlueprintInstanceService.AttachToEntity(repo, _registry, evt.NewBlueprintId, evt.Entity);
```

---

## Q2: Where did you register the system in CGF? Which file/line?

**File:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`, **line 364**

Registered immediately after `BlueprintMaterializationSystem`, in the same Input-phase block:

```csharp
// Line 362-364:
_context.Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(_entityMap!));
_context.Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.BlueprintMaterializationSystem(_blueprintRegistry!));
_context.Kernel.RegisterGlobalSystem(new BlueprintEventIngressSystem(_blueprintRegistry!));
```

**Additional CgfSubsystem change:** Added `using Fdp.Toolkit.Blueprints.Systems;` at line 14.

Both systems share the same `_blueprintRegistry` instance and run in the `Input` phase (`[UpdateInPhase(SystemPhase.Input)]`). The registration order (MaterializationSystem before EventIngressSystem) is intentional: genesis materialization runs first, then runtime events are processed on subsequent frames.

---

## Q3: What EventId values did you use? Were there collisions?

| Constant | Value | Event Struct |
|----------|-------|-------------|
| `EventId_AttachInstanceBlueprint` | 9100 | `AttachInstanceBlueprintEvent` |
| `EventId_RemoveInstanceBlueprint` | 9101 | `RemoveInstanceBlueprintEvent` |
| `EventId_ReplaceInstanceBlueprint` | 9102 | `ReplaceInstanceBlueprintEvent` |

**No collisions found.** Searched the entire codebase for `9100`, `9101`, `9102` — the only matches were:
- The `BATCH-04-INSTRUCTIONS.md` itself (the constants)
- `HsmTickSystemTerminalTests.cs` — uses `9100`/`9101`/`9102` as **integer literals for behavior IDs** (not EventIds), which is a different namespace. No conflict.

Existing EventId blocks in the codebase:
- `8000–8299`: Animation events (`8201–8213` in code, `8001–8013` in docs)
- `9000–9099`: Various (LifecycleEvents 9001–9004, ClusterMaster 9011–9019, 9050–9057)
- `9100–9102`: **New — Blueprint lifecycle events**

---

## Q4: Suggested Commit Message

```
feat: BSA-301 BlueprintLifecycleEvents + BlueprintEventIngressSystem (remove-before-add ordering)

- Add BlueprintConstants with EventId values 9100–9102 (no collisions)
- Add 3 unmanaged event structs: Attach/Remove/ReplaceInstanceBlueprintEvent
- Add BlueprintEventIngressSystem (Input phase) that drains events via
  the BSA-102 core seam, applying ALL removes before ANY attaches (Design §7)
- Register system in CgfSubsystem alongside BlueprintMaterializationSystem
- 18 test methods covering: struct layout, publish/read round-trip, attach,
  remove, replace, idempotent/no-op, and drain ordering (no spurious upgrade)
```

---

## Test Results

### New tests (all pass)

```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
    --filter "FullyQualifiedName~BlueprintEventIngressSystemTests"
```

| # | Test Method | Status |
|---|------------|--------|
| 1a | `AttachInstanceBlueprintEvent_IsValueType` | ✅ Pass |
| 1b | `RemoveInstanceBlueprintEvent_IsValueType` | ✅ Pass |
| 1c | `ReplaceInstanceBlueprintEvent_IsValueType` | ✅ Pass |
| 1d | `AttachInstanceBlueprintEvent_HasCorrectEventId` | ✅ Pass |
| 1e | `RemoveInstanceBlueprintEvent_HasCorrectEventId` | ✅ Pass |
| 1f | `ReplaceInstanceBlueprintEvent_HasCorrectEventId` | ✅ Pass |
| 1g | `AttachInstanceBlueprintEvent_HasCorrectFields` | ✅ Pass |
| 1h | `ReplaceInstanceBlueprintEvent_HasCorrectFields` | ✅ Pass |
| 2a | `AttachEvent_PublishReadRoundTrip_FieldsMatch` | ✅ Pass |
| 2b | `RemoveEvent_PublishReadRoundTrip_FieldsMatch` | ✅ Pass |
| 2c | `ReplaceEvent_PublishReadRoundTrip_FieldsMatch` | ✅ Pass |
| 2d | `EmptyBus_Read_ReturnsEmptySpan` | ✅ Pass |
| 3 | `System_PublishAttachEvent_BlueprintAttachedToEntity` | ✅ Pass |
| 4 | `System_PublishRemoveEvent_BlueprintDetachedFromEntity` | ✅ Pass |
| 5 | `System_PublishReplaceEvent_OldDetachedNewAttached` | ✅ Pass |
| 6a | `System_RemoveAbsentBlueprint_DoesNotThrow` | ✅ Pass |
| 6b | `System_ReplaceWithAbsentOld_AttachStillProceeds` | ✅ Pass |
| 7 | `System_DrainOrdering_RemoveBeforeAdd_NoSpuriousTierUpgrade` | ✅ Pass |

**Total: 18 passed, 0 failed, 0 skipped**

### Pre-existing test baseline (0 net-new failures)

**Hrot.Blueprints.Tests:** 1717 total, 7 pre-existing failures (same 7 fail before and after — verified via `git stash` comparison): `AiPrimitiveEmitGoldenTests` (2), `Stage8Tests` (2), `AllocationFreeTests` (1), `MoveToAndFireDemoTests` (1), `WhenNodePerfTests` (1).

**Fdp.Toolkits.Tests:** 1872 total, 49 pre-existing failures — zero blueprint-related.

---

## Design Decisions & Edge Cases

### 1. Native vs. managed event path

The BATCH-04 instructions referenced `ReadManaged<T>()` but also `[EventId]` — these are mutually exclusive. The `[EventId]` attribute is only consumed by `EventType<T> where T : unmanaged`, which drives the native/unmanaged path. Since `Entity` is an unmanaged struct, the native path (`Read<T>()` / `Publish<T>()`) is architecturally correct and consistent with the existing `LifecycleEvents.cs` pattern.

### 2. Entity validity check

`BlueprintEventIngressSystem` checks `repo.IsAlive(evt.Entity)` before every mutation. Events are published in frame N and consumed in frame N+1 (after `SwapBuffers()`). An entity could be destroyed between publish and consume, and `DetachFromEntity` would be a safe no-op, but `AttachToEntity` would throw. The `IsAlive` guard prevents this.

### 3. BlueprintRegistry.RegisterInstance for test helpers

`BlueprintRegistry.CommitStaging()` **replaces** the entire registry snapshot (not merges). When test helpers registered blueprints one-by-one via separate staging commits, only the last blueprint survived. **Fix:** Used `_registry.RegisterInstance(id, def)` — a direct-registration method that appends to the current snapshot without replacement. This avoids the staging/commit cycle for tests.

### 4. Tier upgrade prevention (Test 7)

Test 7 verifies the remove-before-add ordering by filling a B1024 tier to capacity (4 slots), then publishing `Remove(A)` + `Attach(E)` in the same frame. After the system executes:
- A is detached (frees a slot)
- E is attached (reuses the freed slot)
- Tier remains B1024 (no upgrade to B4096)

This works because the system drains all Remove events and Replace detachments before any Attach or Replace attachment — the freed slot is immediately available for reuse.
