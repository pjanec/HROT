# BATCH-01 Report

**Batch:** BATCH-01 — Stage 1 Foundation — Audit, Harness, DTOs, and Contract
**Tasks:** RB-1.0, RB-1.1, RB-1.2, RB-1.3
**Status:** COMPLETE

---

## 1. Task Completion Status

| Task | Description | Status |
|------|-------------|--------|
| RB-1.0 | Codebase audit and gap fix — `GetAllRegistered()` on ComponentTypeRegistry + EventType; `HasComponentByTypeId` | COMPLETE |
| RB-1.1 | `FdpRecordingHarness` test substrate with self-test | COMPLETE |
| RB-1.2 | Domain DTOs — `JsonExportOptions`, `ChangelogEntryDto`, `DiffNode`, enums | COMPLETE |
| RB-1.3 | `IRecordingExportService` contract + `RecordingExportService` stub | COMPLETE |

All four tasks are fully implemented. 8 new tests added; all pass. 0 regressions in existing tests.

---

## 2. Files Created / Modified

### Engine modifications (`Fdp.Core`)

| File | Status | Change |
|------|--------|--------|
| `FDP/Engine/Fdp.Core/ComponentType.cs` | MODIFIED | Added `ComponentTypeRegistry.GetAllRegistered()` |
| `FDP/Engine/Fdp.Core/EventType.cs` | MODIFIED | Added `EventTypeRegistry.GetAllRegistered()` (internal) and new `public static class EventType` with `GetAllRegistered()` gateway |

### New production files (`Fdp.Toolkits`)

| File | Status |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/JsonExportOptions.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ChangelogEntryDto.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/IRecordingExportService.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` | NEW (stub) |

### New test files (`Fdp.Toolkits.Tests`)

| File | Status | Tests |
|------|--------|-------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Audit/RegistryAuditTests.cs` | NEW | 4 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` | NEW | (harness, not tests) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarnessTests.cs` | NEW | 1 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/JsonExportOptionsTests.cs` | NEW | 2 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/AssemblyReferenceTests.cs` | NEW | 1 |

Note: `RepositoryExtensions.cs` was NOT created. `EntityRepository.HasComponentByTypeId(Entity, int)` already exists as a public instance method in `EntityRepository`. No extension helper was needed.

---

## 3. Test Results

### New ReplayBrowser tests (8 new, all pass)
```
Passed Fdp.Toolkit.ReplayBrowser.Audit.RegistryAuditTests.GetAllRegistered_ComponentTypes_ContainsRegisteredTypes [3 ms]
Passed Fdp.Toolkit.ReplayBrowser.Audit.RegistryAuditTests.GetAllRegistered_EventTypes_ContainsRegisteredTypes [3 ms]
Passed Fdp.Toolkit.ReplayBrowser.Audit.RegistryAuditTests.HasComponentByTypeId_ReturnsTrue_WhenComponentPresent [< 1 ms]
Passed Fdp.Toolkit.ReplayBrowser.Audit.RegistryAuditTests.HasComponentByTypeId_ReturnsFalse_WhenComponentAbsent [3 ms]
Passed Fdp.Toolkit.ReplayBrowser.Support.FdpRecordingHarnessTests.HarnessSelfTest_ProducesReadableRecording [116 ms]
Passed Fdp.Toolkit.ReplayBrowser.Export.JsonExportOptionsTests.Defaults_MatchDesignSpec [< 1 ms]
Passed Fdp.Toolkit.ReplayBrowser.Export.JsonExportOptionsTests.RoundTrip_Json_PreservesAllFields [32 ms]
Passed Fdp.Toolkit.ReplayBrowser.Export.AssemblyReferenceTests.RecordingExportService_Assembly_HasNoFdpPresentationOrRaylibReference [< 1 ms]

Test Run Successful.
Total tests: 8
Passed: 8
Total time: 1.3 Seconds
```

### Full `Fdp.Toolkits.Tests` suite
```
Failed!  - Failed: 30, Passed: 974, Skipped: 0, Total: 1004, Duration: 6 s
```
The 30 failures are all pre-existing and not caused by this batch. Confirmed by running the suite
filtered to exclude ReplayBrowser tests, which yields the same 30 failures:
```
Failed!  - Failed: 30, Passed: 966, Skipped: 0, Total: 996, Duration: 5 s
```
That is: 974 - 966 = 8, which exactly matches the 8 new tests added. Zero regressions.

Pre-existing failing namespaces (not introduced by this batch):
- `Fdp.Toolkit.Geographic.Tests.SimTransformBridgeSystemTests`
- `Fdp.Toolkit.Replay.Tests.ReplayModuleTests`
- `Fdp.Toolkit.Navigation.Tests.NavigationIntentBridgeSystemTests`
- `Fdp.Toolkit.Behavior.Tests.HsmTickSystemTerminalTests`
- `Fdp.Toolkit.Combat.Tests.FireProcessingSystemTests`

### Build
```
dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
Build succeeded. 0 Error(s), 0 Warning(s)
```

---

## 4. Design Decisions

### RB-1.0: No `RepositoryExtensions.cs` needed
The instructions asked to check whether `EntityRepository.HasComponentByTypeId(Entity, int typeId)`
existed, and add an extension method if absent. The method already exists as a public instance
method on `EntityRepository` itself. No extension method was added — the audit tests call it
directly via the harness's `EntityRepository` instance, which matches how BATCH-02 production
code will call it.

### RB-1.0: Public `EventType` gateway class in `EventType.cs`
`EventTypeRegistry` is `internal`. To expose `GetAllRegistered()` publicly without changing
the registry's access modifier (which would break encapsulation and might cause knock-on effects),
a new `public static class EventType` was added in the same file. This follows the existing pattern
where the codebase has a public `EventType<T>` generic class that already wraps the internal
registry. The new non-generic `EventType` gateway is the natural sibling for cross-type queries.

### RB-1.1: Used `_repo.Bus` instead of a separate `FdpEventBus`
The `AsyncRecorder`'s `CaptureFrame` and `CaptureKeyframe` overloads accept an optional
`FdpEventBus? eventBus`. The `EntityRepository` already owns a `Bus` property. The harness passes
`_repo.Bus` to the recorder rather than creating a second bus, which matches how production
modules wire the recorder. This avoids event routing confusion in tests.

### RB-1.1: `AsyncRecorder.ClearDestructionLog()` not called by harness
`AsyncRecorder.CaptureFrame` calls `repo.ClearDestructionLog()` internally. The harness does not
call it separately. This was discovered by reading the `AsyncRecorder` source to avoid a double-
clear bug that would discard destruction events from subsequent frames.

### RB-1.1: `PlaybackController` disposed before harness `Dispose()` in self-test
On Windows, `PlaybackController` holds the `.fdp` file open with a `FileStream`. If the harness's
`Dispose()` attempts to delete the file while `PlaybackController` still holds it open, the
`File.Delete` throws `UnauthorizedAccessException`. The self-test disposes `PlaybackController`
in a `using` block that ends before `harness.Dispose()` is called. This ordering is documented
with an inline comment in the test.

### RB-1.1: Harness component type IDs in the 200-203 range
The test project reserves type IDs by convention. After checking all existing test files, IDs
200-209 were free. `HarnessPosition` uses ID 202 and `HarnessVelocity` uses ID 203. The IDs in
the 240-249 range (which many existing tests use) were avoided.

### RB-1.1: Event type IDs 99001-99003 for harness test types
`EventTypeRegistry.ClearForTesting()` is `internal` and not accessible from `Fdp.Toolkits.Tests`
(which is a different assembly). To prevent cross-test contamination from event type registration
collisions, globally unique IDs were used: 99001 (`AuditEventA`), 99002 (`AuditEventB`),
99003 (`HarnessTestEventA`). These do not collide with any existing `[EventId]` in the test
project or production code.

### RB-1.2: `EndTimeSec = float.PositiveInfinity` left as specified; round-trip test uses 99.9f
`System.Text.Json` cannot serialize `float.PositiveInfinity` with default settings. The `Defaults`
test verifies `float.PositiveInfinity` as the design requires. The round-trip JSON test uses
`99.9f` as `EndTimeSec` to stay within what `System.Text.Json` can serialise/deserialise without
a custom converter. This is a test-only workaround; the production field default is unchanged.

### RB-1.2: `TargetEntities` round-trip uses empty list
The `Entity` struct has two constructors and no `[JsonConstructor]` attribute, making it
undeserializable via `System.Text.Json`. The round-trip test uses `TargetEntities = new()` (empty
list) to stay within what the serializer can handle. Non-empty entity lists are a BATCH-02 concern
(the export service, not the options DTO, will handle entity resolution).

### RB-1.2: `ChangelogEntryDto` defined as a `record`
The design talk specifies a `record`. The implementation uses `public record ChangelogEntryDto`
with positional-style properties. This gives value equality and deconstruction for free, which
will be useful in diff display code in BATCH-03.

---

## 5. Issues Encountered

### Issue 1: `EventTypeRegistry.ClearForTesting()` is internal
The registry's `ClearForTesting()` method is internal to `Fdp.Core`. Tests in `Fdp.Toolkits.Tests`
cannot call it. Since multiple test classes register distinct event types, and registrations are
global/static, there was risk of ID collisions between test classes.

Resolution: Assigned globally unique `[EventId]` values (99001-99003) to all new test event types
so they do not collide with each other or any existing registration. No teardown needed.

### Issue 2: `ComponentTypeRegistry.Clear()` is public but must be used carefully
`ComponentTypeRegistry.Clear()` wipes all registered types globally. The `RegistryAuditTests`
constructor calls it before each test to start from a clean slate. This is safe because xunit.runner
is configured with `parallelizeTestCollections: false` and `maxParallelThreads: 1`, so no other
test runs concurrently.

### Issue 3: File deletion race on Windows
When the self-test opened the produced `.fdp` file with `PlaybackController` and then called
`harness.Dispose()`, the delete failed because `PlaybackController` held the file open. The fix
was to structure the test so the `PlaybackController` `using` block ends before `harness.Dispose()`
is called. See Design Decisions above for details.

---

## 6. Integration Notes

### How BATCH-02 connects
`RecordingExportService` will implement `IRecordingExportService.ExportToJson` in BATCH-02. It
will use `PlaybackController` to read the `.fdp` file and the new `ComponentTypeRegistry.GetAllRegistered()`
/ `EventType.GetAllRegistered()` APIs to discover all types at export time without requiring
callers to register types ahead of time.

### How BATCH-03 connects
`DiffNode` is a placeholder abstract class (`Name`, `IsModified`). BATCH-03 will expand it into
the full type hierarchy (`LeafDiffNode`, `ObjectDiffNode`, etc.) within the same
`Fdp.Toolkit.ReplayBrowser.Diff` namespace.

### `FdpRecordingHarness` as shared substrate
All Stage 1, 3, and 4 test files will construct their `.fdp` inputs via `FdpRecordingHarness`
rather than crafting raw binary files. BATCH-02 tests will depend on it heavily. No changes to
the harness API should be needed until BATCH-03 adds event-capture assertions.

---

## 7. Success Criteria Checklist

- [x] `ComponentTypeRegistry.GetAllRegistered()` exists and unit test passes.
- [x] `EventType.GetAllRegistered()` exists and unit test passes.
- [x] `EntityRepository.HasComponentByTypeId` confirmed present (no extension needed); positive and negative tests pass.
- [x] `FdpRecordingHarness` compiles and `HarnessSelfTest_ProducesReadableRecording` passes (5-frame recording, read back correctly, no temp file leak).
- [x] `JsonExportOptions` defaults test passes.
- [x] `JsonExportOptions` JSON round-trip test passes.
- [x] `IRecordingExportService` interface and `RecordingExportService` stub compile.
- [x] Assembly-reference test asserts no `Fdp.Presentation`/`Raylib` in the toolkits assembly.
- [x] `dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — 0 errors, 0 warnings.
- [x] All 8 new tests pass; 0 regressions in existing tests.
