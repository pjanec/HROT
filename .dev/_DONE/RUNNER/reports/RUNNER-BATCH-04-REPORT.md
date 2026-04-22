# RUNNER-BATCH-04 Report

**Batch:** RUNNER-BATCH-04  
**Developer:** GitHub Copilot  
**Date:** 2026-02-26  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| R0.1 | ✅ Complete | Auto-assignment removed; `[ComponentId]` enforced unconditionally |
| R0.2 | ✅ Complete | All 164 production + ~92 test components explicitly attributed |
| R0.3 | ✅ Complete | `UnsafeLayout<T>` 32-bit support added; `AutoCycloneTranslator<EntityMaster>` restored |
| R3.1 | ✅ Complete | `HeadlessTestExecutor` implemented with full `RunAsync()` loop |
| R3.2 | ✅ Complete | `TestScript`, `TestStep`, `AssertionRule` models and JSON parser implemented |

---

## 🧪 Testing Results

**Fdp.Kernel.Tests:** 691 passed / 693 total (2 intentionally skipped)  
**Hrot.IG.Tests:** 229 passed / 229 total  
**Hrot.SimHost.Tests:** 55 passed / 55 total  

**Key Test Scenarios Verified:**
- [x] All previously auto-assigned components now throw `InvalidOperationException` without `[ComponentId]`
- [x] `EntityMasterTranslatorTests` — `AutoCycloneTranslator<EntityMaster>` initialises without crash
- [x] `TransformSyncSystemRegistrationTests` — explicit IDs survive registry clear/re-register cycles
- [x] `EntityRepositorySyncTests.SyncFrom_IncludeTransient_IncludesTransient` — fixed sparse-ID mask bug
- [x] `DeltaTests.GetComponentRW_MarksChanged` — fixed sequential-ID assumption in `QueryDelta`
- [x] `TestScript` JSON parser xUnit tests — all passing
- [x] `HeadlessTestExecutor` unit tests — initialise, run, shutdown, exit code

---

## 📝 Developer Insights

### Q1: What issues did you encounter during implementation? How did you resolve them?

**Issue 1 — File corruption from PowerShell regex replacement.**  
During the R0.2 sweep of `FlightRecorderIntegrationTests.cs`, a PowerShell `$1` capture-group replacement incorrectly expanded `$1` as a PowerShell variable, injecting the script output into the file 14 times (9 427 lines instead of ~535). The file was reconstructed by extracting the last valid copy using `$lines[7745..$lines.Length-1]`, restoring two missing `using` directives and the closing namespace brace.

**Issue 2 — `byte` ID space exhaustion.**  
`ComponentIdAttribute(byte id)` has an absolute maximum of 255. With production IDs occupying 0–163, only IDs 164–255 (92 slots) remained for test components. Several test files required IDs beyond 255 if assigned naively. Resolution: a PowerShell gap analysis (`0..255 | Where-Object { $_ -notin $allUsed }`) identified large available blocks within the production reserved ranges (8–19, 34–41, 143–159). Test-only types that could not fit in 164–255 were placed in these gaps (e.g., `CommandBufferPerformanceTests.TestComponent` → 143).

**Issue 3 — Duplicate IDs between test files.**  
`FlightRecorderSchemaTests.cs` was independently assigned IDs 240–244 in a prior session, colliding with Benchmark and Concurrency test types added later (also 240–244). A dedup scan (`Group-Object ID | Where-Object Count -gt 1`) revealed five colliding sets. `FlightRecorderSchemaTests.cs` was reassigned to 8–12 (a production gap), and three hardcoded `244` dictionary-key literals within the same file were updated to `12`.

**Issue 4 — Sequential ID assumption in two production methods.**  
After all components were explicitly attributed with sparse IDs, two tests failed:
- `DeltaTests.GetComponentRW_MarksChanged` (expected count 1, got 0)
- `EntityRepositorySyncTests.SyncFrom_IncludeTransient_IncludesTransient` (transient component not found)

Root cause: both `QueryDelta` in `EntityRepository.cs` and `GetSnapshotableMask(includeTransient: true)` in `EntityRepository.Sync.cs` iterated `for (i = 0; i < RegisteredCount; i++)`, which was valid only when IDs were contiguous starting from 0. With a transient component at ID 144 and `RegisteredCount = 1`, the loop iterated `i = 0` only and never checked the actual ID.

Fix:
1. Added `GetAllIds()` to `ComponentTypeRegistry` (iterates `_idToType.Keys`).
2. `GetSnapshotableMask(includeTransient: true)` now uses `foreach (var id in ComponentTypeRegistry.GetAllIds())`.
3. `QueryDelta` now iterates `_componentTables` (`Dictionary<int, IComponentTable>`) directly, reading `kvp.Value.ComponentTypeId` from each table — no reliance on contiguous IDs.

---

### Q2: Did you spot any weak points in the existing codebase? What would you improve?

**Sequential ID assumption risk.** The `QueryDelta` / `GetSnapshotableMask` bugs were silent until the explicit-ID mandate exposed them. Any future method that iterates `for (i = 0; i < RegisteredCount; i++)` will have the same bug. Recommend a code audit pattern: search for `RegisteredCount` and `_nextId` in any loop bounds throughout the kernel.

**`ComponentIdAttribute` byte constraint is tight.** With 164 production IDs consumed and only 92 test slots available in 164–255, the ID space is already near capacity. A short-term workaround is to carve into the reserved gaps (8–19, 34–41, etc.); a long-term fix would be to widen `ComponentIdAttribute` to `ushort` (allowing 0–65535) or split test-only components into a separate non-overlapping registry namespace.

**`BitMask256` is the root capacity constraint.** The 256-bit limit flows from the mask implementation. Any future expansion would require widening `BitMask256` to `BitMask512` or similar. This is a significant architecture change but the ID pressure from tests alone is already revealing the ceiling.

---

### Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?

**`GetAllIds()` vs. exposing `_idToType` directly.** The production fix needed a way to enumerate actual registered IDs without exposing the internal dictionary. `GetAllIds()` returns a plain `int[]` snapshot under the registry lock — simple and thread-safe. An alternative was to expose `IReadOnlyDictionary<int, Type>` directly, but that creates unnecessary coupling and leaks the type map.

**`QueryDelta` iteration via `_componentTables`.** The fixed implementation iterates the private `_componentTables` field (a `Dictionary<int, IComponentTable>`) using `kvp.Value.ComponentTypeId`. The alternative was to use `GetAllIds()` and then look up each table by ID, but iterating `_componentTables` directly is already O(n registered) and avoids a second dictionary lookup per component.

**Test ID gap allocation strategy.** Rather than requesting a specification update to widen the ID type, test-component IDs were allocated in existing production reserved gaps (documented in `GlobalComponentIds.cs` as "reserved for future use"). These ranges (8–19, 34–41, 143–159) are within the production spec but currently unpopulated, making them safe to occupy with test-only types. The IDs are explicit and permanent (not auto-assigned), so they will not shift when the production reserved ranges are eventually populated.

---

### Q4: What edge cases did you discover that weren't mentioned in the spec?

**Managed records and classes require `[ComponentId]` too.** The spec mentions structs but the enforcement applies equally to `record`, `class`, and `interface` types registered as components. `ManagedComponentTableSyncTests.TestRecord` (a `public record`) and `FlightRecorderIntegrationTests.TestManagedComponent` (a `[MessagePackObject]` class) both needed explicit IDs.

**`[ComponentId]` on interface types.** `ITkbDatabase` and `INetworkTopology` (ID 60, 61) are registered as component interfaces. The original `[AttributeUsage]` on `ComponentIdAttribute` only allowed `AttributeTargets.Struct`. An R0.1 sub-task updated this to include `AttributeTargets.Class | AttributeTargets.Interface`.

**`repeat`/`interval` expansion in `TestScript`.** The spec mentioned `interval` logic but did not specify whether expanded steps preserve the original step's `Args` and `Assert` by reference or by value. The implementation clones `Args` and `Assert` per expanded step so that a handler mutating `Args` at runtime does not corrupt subsequent repetitions.

**`HeadlessTestExecutor` exit code semantics.** The spec required "0 = pass, 1 = fail" but did not define "fail". The implementation treats any uncaught exception in the update loop *or* any assertion failure as fail. Assert failures are accumulated in `_assertionFailures` and reported even if the loop completes normally.

---

### Q5: Are there any performance concerns or optimization opportunities you noticed?

**`GetAllIds()` allocates on every call.** The method returns a new `int[]` each time under lock. In systems that call `GetSnapshotableMask` frequently (e.g., per-frame snapshot), this is an allocation on the hot path. A simple optimisation: cache the array in a `volatile int[]` field and invalidate only when a new type is registered. Component registration is rare (startup only in production), so the cached array is almost always valid.

**`TestScript.Steps` is deserialized into a flat `List<TestStep>`.** With `repeat`/`interval` expansion applied at parse time, large scripts produce a proportionally large list. For scripts with high-frequency repeated steps (e.g., 1000 `tick` steps at 60 Hz for a 16-second test), the expanded list is 1000 items. This is fine for test use but would benefit from lazy enumeration in a real-time streaming scenario.

---

## 📋 Component Attribution Summary (R0.2)

### Previously auto-assigned components (no `[ComponentId]`)

Before R0.2, **approximately 92 component types** had no explicit ID and relied on the auto-increment fallback in `ComponentTypeRegistry`. These fell into three groups:

| Group | Count | Examples |
|-------|-------|---------|
| Production components (structs, classes, interfaces) | ~55 | `SimTransform`, `NetworkIdentity`, `ContextMenuState`, `ITkbDatabase` |
| Test-only per-class component types (in `Fdp.Kernel.Tests`) | ~30 | `Position` in `ComponentDirtyTrackingTests`, `PersistentPos` in `FlightRecorderTests`, `SerialObj1/2` |
| Test-only shared types (benchmarks, concurrency) | ~7 | `CommandBufferPerformanceTests.TestComponent`, `SyncConcurrencyTests.Pos`, etc. |

After R0.2, every one of these types has an explicit `[ComponentId(byte)]` attribute. The auto-assignment code path (`_nextId`, `RelocateAutoAssigned()`) was deleted entirely in R0.1.

---

## 🔧 UnsafeLayout 32-bit Support (R0.3)

### Problem

`UnsafeLayout<T>` and `MultiInstanceLayout<T>` in `FDP.Toolkit.Replication` contained a startup-time static constructor that validated the `EntityId` field layout using:

```csharp
if (field.FieldType != typeof(long))
    throw new InvalidOperationException("EntityId must be long (8 bytes)");
```

The DDS network layer uses `int` (4 bytes) for `EntityId`. `AutoCycloneTranslator<EntityMaster>` constructed `UnsafeLayout<EntityMaster>` at startup, causing an immediate `InvalidOperationException` and forcing the prior developer to delete the translator entirely.

### Fix

Both `UnsafeLayout<T>` and `MultiInstanceLayout<T>` received:

```csharp
public static readonly bool IsEntityId32Bit;

static UnsafeLayout()
{
    var field = typeof(T).GetField("EntityId", BindingFlags.Public | BindingFlags.Instance);
    IsEntityId32Bit = field?.FieldType == typeof(int) || field?.FieldType == typeof(uint);
    // validation now accepts both int/uint and long/ulong
}
```

`ReadEntityId` / `WriteEntityId` were updated to branch on `IsEntityId32Bit`:

```csharp
public long ReadEntityId(ref T instance)
    => IsEntityId32Bit
        ? Unsafe.As<T, int>(ref Unsafe.AddByteOffset(ref instance, _entityIdOffset))
        : Unsafe.As<T, long>(ref Unsafe.AddByteOffset(ref instance, _entityIdOffset));
```

### Translator Restoration

`AutoCycloneTranslator<EntityMaster>` was restored in both:
- `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`
- `Hrot.SimHost/Program.cs`

`SimHost` can now observe all networked entities via DDS again.

---

## 🧪 TestScript JSON Example (R3.2)

The following JSON is a complete validated `TestScript` exercising all supported features:

```json
{
  "testName": "BasicSpawnAndMove",
  "duration": 10.0,
  "steps": [
    {
      "time": 0.0,
      "action": "spawn",
      "args": {
        "entityType": "infantry",
        "count": 5,
        "position": { "x": 100.0, "y": 0.0, "z": 200.0 }
      }
    },
    {
      "time": 1.0,
      "action": "tick",
      "args": {},
      "repeat": 10,
      "interval": 0.5,
      "assert": {
        "entityCount": { "min": 5, "max": 5 }
      }
    },
    {
      "time": 6.0,
      "action": "move",
      "args": {
        "entityType": "infantry",
        "target": { "x": 150.0, "y": 0.0, "z": 250.0 }
      }
    },
    {
      "time": 9.5,
      "action": "assert_position",
      "args": {
        "entityType": "infantry"
      },
      "assert": {
        "distanceToTarget": { "max": 5.0 }
      }
    }
  ]
}
```

The `repeat: 10, interval: 0.5` on the `tick` step expands to 10 individual steps at simulation times 1.0, 1.5, 2.0 … 5.5. Each expanded step carries an independent copy of `args` and `assert`.

Assertion rules support `min`, `max`, and `equals` (tolerance ±0.001). Only non-null fields are evaluated, so `{ "max": 5.0 }` checks only the upper bound.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] `byte` ID space is nearly full. With ~164 production + ~92 test IDs the range 0–255 is exhausted except for reserved gaps. Before adding new production components (IDs 164–199 range per the original plan), an audit of test-ID placement in reserved gaps (143–159 particularly) is required to avoid future collisions.
- [ ] `HeadlessTestExecutor` uses `SubsystemOrchestrator`, which is currently a stub collaborator. Full integration is gated on the Phase R3 orchestrator implementation.
- [ ] `GetAllIds()` in `ComponentTypeRegistry` allocates an `int[]` on every call. A caching optimisation is deferred to a future batch.
- [ ] Two tests in `Fdp.Kernel.Tests` are permanently skipped (`[Skip]` attributed) — these were pre-existing skips unrelated to this batch.
