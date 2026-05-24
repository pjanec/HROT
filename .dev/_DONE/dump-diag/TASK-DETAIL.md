# Cluster-Wide Diagnostic Dump — Task Detail

**Reference:** See [DESIGN.md](./DESIGN.md) for architectural context and rationale.

---

## Phase 1: JSON Serialisation Foundation

---

### DD-P1-T01 — Move FixedString Converters to Fdp.Core

**Design Reference:** [Phase 1.1](./DESIGN.md#11-move-custom-converters-to-fdpcore)

**Scope:**
- Move `FixedString32Converter` and `FixedString64Converter` from
  `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioJsonConverters.cs`
  to new files in `FDP/Engine/Fdp.Core/Serialization/Converters/`.
- Move `Vector2ArrayConverter`, `Vector3ArrayConverter`, `Vector4ArrayConverter`,
  `QuaternionArrayConverter` to the same new location.
- Move `StrictStringEnumConverter` from
  `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationJsonOptions.cs`
  to `FDP/Engine/Fdp.Core/Serialization/Converters/StrictStringEnumConverter.cs`.
  `OrchestrationJsonOptions` retains a thin forwarding wrapper or `[Obsolete]` type alias.
- Keep the FixedString and Vector types in `ScenarioJsonConverters.cs` as `[Obsolete]` forwarders
  (type aliases via `using` or explicit subclasses) until all callers are migrated in DD-P1-T04.
- Add `System.Text.Json` using in the new files; no new NuGet references needed
  (`System.Text.Json` is part of the .NET runtime used by `Fdp.Core`).

NOT included: Changing any call sites (done in DD-P1-T04).

**Constraints:**
- `Fdp.Core` must NOT take a `Newtonsoft.Json` dependency.
- New converter classes must reside in namespace `Fdp.Core.Serialization.Converters`.
- Public API surface must match the existing converters in `ScenarioJsonConverters.cs` exactly.

**Success Conditions:**

1. _New file test:_ `Fdp.Core.Tests` project: `new FixedString64Converter()` compiles and
   `JsonSerializer.Serialize(new FixedString64("hello"), opts)` where `opts` includes
   `FixedString64Converter` returns `"\"hello\""`.

2. _StrictStringEnumConverter test:_ `Fdp.Core.Tests`: `new StrictStringEnumConverter()` compiles
   and serialising an enum value returns its string name, not its integer value.

3. _Regression test:_ `Fdp.Toolkits.Tests/Scenario/ScenarioJsonConvertersTests.cs` still compiles
   and all existing tests pass (the forwarders preserve existing behaviour).

4. _No new package references_ are added to `Fdp.Core.csproj`.
  `AllowTrailingCommas = true`, `ReadCommentHandling = JsonCommentHandling.Skip`,
  `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`, all converters from DD-P1-T01
  (including `StrictStringEnumConverter` now in `Fdp.Core.Serialization.Converters`).
- Define `FdpJsonOptionsRegistry.Indented` as `new JsonSerializerOptions(DefaultRelaxed)`
  with `WriteIndented = true`.
- Both instances must be frozen (`.MakeReadOnly()`) after construction.

NOT included: Migrating existing call sites (done in DD-P1-T04).

**Constraints:**
- The registry must be in `Fdp.Core.Serialization` namespace.
- Both singletons are immutable: calling code must never mutate them.
- `StrictStringEnumConverter` (moved to `Fdp.Core` in DD-P1-T01) is registered in
  `DefaultRelaxed`. `JsonStringEnumConverter` must NOT be used; using the weaker standard
  converter would allow silent integer-as-enum parsing in diagnostic dump handlers.

**Success Conditions:**

1. _Compile-only test:_ `Fdp.Core.Tests` references `FdpJsonOptionsRegistry.DefaultRelaxed`
   and `FdpJsonOptionsRegistry.Indented` and both are non-null.

2. _Immutability test:_ Attempting `FdpJsonOptionsRegistry.DefaultRelaxed.WriteIndented = true`
   throws `InvalidOperationException`.

3. _FixedString round-trip:_ `JsonSerializer.Deserialize<FixedString64>("\\"hello\\"",
   FdpJsonOptionsRegistry.DefaultRelaxed)` returns a `FixedString64` whose `.ToString()` equals
   `"hello"`.

4. _Field inclusion:_ Serialising a struct with a public field (not property) via
   `DefaultRelaxed` produces non-empty JSON with the field name present.

---

### DD-P1-T03 — JsonAestheticFormatter

**Design Reference:** [Phase 1.3](./DESIGN.md#13-jsonaestheticformatter-fdptoolkits)

**Scope:**
- Create `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs`.
- Implement `public static string FlattenNumericArrays(string rawJson)`.
- Logic extracted from `ScenarioFileService.WriteFormattedToken` / `IsPureNumericArray`
  without semantic change.
- Update `ScenarioFileService.SaveScenario` to call `JsonAestheticFormatter.FlattenNumericArrays`
  instead of its private methods (the private methods may be removed or left private with the
  one call site changed to the new public method).

NOT included: Migrating other call sites.

**Constraints:**
- `JsonAestheticFormatter` must be in namespace `Fdp.Toolkits.Serialization`.
- The method is a pure function: same input always produces same output, no state.
- The behaviour of `ScenarioFileService.SaveScenario` must be identical before and after
  this change (no scenario format regression).

**Success Conditions:**

1. _Unit test:_ `JsonAestheticFormatter.FlattenNumericArrays("[1.0, 2.0, 3.0]")` returns
   `"[1.0, 2.0, 3.0]"` (already flat, unchanged).

2. _Unit test:_ Input with indented numeric array:
   ```json
   "Position": [\n  1.0,\n  2.0,\n  3.0\n]
   ```
   produces `"Position": [1.0, 2.0, 3.0]` inline.

3. _Unit test:_ A mixed array (strings + numbers) is NOT collapsed to a single line.

4. _Regression test:_ Existing `ScenarioFileService` save/load round-trip tests still pass.

---

### DD-P1-T04 — Refactor Existing JSON Callers

**Design Reference:** [Phase 1.4](./DESIGN.md#14-refactor-existing-callers)

**Scope:**
- Replace `FdpAutoSerializer._fieldAwareOptions` with `FdpJsonOptionsRegistry.DefaultRelaxed`.
- Replace `OrchestrationJsonOptions.Default` with `FdpJsonOptionsRegistry.DefaultRelaxed`
  (or retain as a thin wrapper delegating to the registry).
- Replace `MetadataSerializer._options` with `FdpJsonOptionsRegistry.DefaultRelaxed`.
- Replace `HrotSerializerOptions.HrotJsonOptions` with `FdpJsonOptionsRegistry.Indented`.
- Update `EventBrowserPanel` "Copy to JSON" single-item path to use
  `FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter.FlattenNumericArrays`.
- Update `EntityJsonDumper.Dump` to use `FdpJsonOptionsRegistry.Indented` +
  `JsonAestheticFormatter.FlattenNumericArrays`.

**Constraints:**
- Serialised payloads over DDS (`OrchestrationJsonOptions`) must remain backward compatible:
  field names, casing, and enum values must not change.
- `FdpAutoSerializer` lives in two files; both must be updated:
  `FDP/Engine/Fdp.Core/FlightRecorder/FdpAutoSerializer.cs` and
  `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs`.

**Success Conditions:**

1. _Bug fix test (FixedString64):_ Serialising a `DestructionOrder` event whose `Reason` field
   is a `FixedString64("HealthDepleted")` via the updated `EventBrowserPanel` path produces
   `"Reason": "HealthDepleted"`, NOT `{ "Length": 11, "IsEmpty": false }`.

2. _Regression:_ All existing `FdpAutoSerializer` round-trip tests pass.

3. _Regression:_ `OrchestrationPayloadDtos` deserialization tests pass (field names unchanged).

4. _Regression:_ `ScenarioJsonConvertersTests` all pass.

---

## Phase 2: Diagnostic Data Service Interfaces and Implementations

---

### DD-P2-T01 — IDiagnosticEventHistoryService and CapturedEventDto

**Design Reference:** [Phase 2.1](./DESIGN.md#21-idiagnosticeventhistoryservice-fdpcore)

**Scope:**
- Create `FDP/Engine/Fdp.Core/Diagnostics/IDiagnosticEventHistoryService.cs`.
- Define `CapturedEventDto` record (same file or adjacent file).
- Create `FDP/Engine/Fdp.Core/Diagnostics/DiagnosticEventHistoryService.cs` implementing the
  interface with a thread-safe circular buffer capped at 500 events.
- The service updates its buffer by iterating `FdpEventBus.GetDebugInspectors()` and calling
  `inspector.InspectReadBuffer()`. The update method is called from a dedicated
  `IEcsModuleSystem` registered in the `PostSimulation` or `Export` phase (wired in DD-P2-T02).
  This phase guarantee ensures all domain systems have published their intents before the buffer
  is captured, preventing torn reads.

NOT included: Wiring into the EventBrowserPanel (DD-P2-T02).

**Constraints:**
- The buffer is thread-safe. `GetHistory()` must perform a **copy-under-lock**: acquire the
  internal lock, copy all current references to a new `CapturedEventDto[]`, release the lock
  immediately, and return the array. The caller (dump handler or UI panel) serialises the
  returned array without holding any service lock. Holding the lock during serialisation would
  stall the 60 Hz PostSimulation writer, violating the non-blocking engine mandate.
- The service lives in `Fdp.Core.Diagnostics` namespace.
- `GetHistory(providerFilter)`: if `providerFilter` is null or empty, return all events; otherwise
  filter by `CapturedEventDto.TypeName` prefix match against the provider names.
- The update must NOT be wired to a generic kernel tick hook; it must be an explicit
  `IEcsModuleSystem` with its phase set to `PostSimulation` or `Export`.

**Success Conditions:**

1. _Unit test:_ Construct `DiagnosticEventHistoryService`, push 600 events, call `GetHistory()`,
   assert count == 500 (oldest are dropped).

2. _Unit test:_ `GetHistory(new[] { "World" })` returns only events whose `TypeName` matches the
   "World" provider.

3. _Thread safety test:_ Concurrent read and write from two threads does not throw.

4. _Copy-under-lock test:_ Call `GetHistory()` from a background thread while a writer thread
   continuously appends events. The returned array is a stable snapshot: writes that occur after
   `GetHistory()` returns do not modify the contents of the returned array.

---

### DD-P2-T02 — Refactor EventBrowserPanel to Use IDiagnosticEventHistoryService

**Design Reference:** [Phase 2.1](./DESIGN.md#21-idiagnosticeventhistoryservice-fdpcore)

**Scope:**
- Inject `IDiagnosticEventHistoryService` into `EventBrowserPanel`.
- Remove the panel's private event capture loop (the code that calls
  `FdpEventBus.GetDebugInspectors()` and maintains `_capacity = 500`).
- The panel reads the buffer from the service instead.
- Register `DiagnosticEventHistoryService` as a singleton in the subsystem bootstrappers (SimHost,
  CGF, IG, ExCon) and register a companion `EventHistoryCaptureSystem` (implementing
  `IEcsModuleSystem`) in the `PostSimulation` or `Export` phase of each kernel. The system’s
  `Update()` method calls `_historyService.Capture(_eventBus)` (or equivalent); it must NOT be
  wired to a kernel tick hook or a pre-simulation phase.

**Constraints:**
- The panel's existing rendering (frame + short type name, colour-coding, single-item copy-to-JSON)
  must be fully preserved.
- `CapturedEvent` private class in `EventBrowserPanel` is replaced by `CapturedEventDto`.

**Success Conditions:**

1. _Integration test:_ `EventBrowserPanel` constructed with a mock `IDiagnosticEventHistoryService`
   returning 5 events renders exactly 5 rows without exceptions.

2. _Regression:_ No compile errors in any subsystem that uses `EventBrowserPanel`.

---

### DD-P2-T03 — IArchitectureDiagnosticsService

**Design Reference:** [Phase 2.2](./DESIGN.md#22-iarchitecturediagnosticsservice-fdpmodulehost)

**Scope:**
- Create `FDP/Engine/Fdp.ModuleHost/Diagnostics/IArchitectureDiagnosticsService.cs` with
  `ArchitectureSnapshotDto`, `TranslatorDiagnosticsDto`, and the interface.
- Create `ArchitectureDiagnosticsService` implementing the interface, moving the reflection-based
  data-gathering logic from `ArchitectureDiagnosticsPanel` into `GetSnapshot()`.
- Inject `IArchitectureDiagnosticsService` into `ArchitectureDiagnosticsPanel`, removing direct
  `ModuleHostKernel` calls from the panel.

**Constraints:**
- `IArchitectureDiagnosticsService` and DTOs must be in namespace
  `Fdp.ModuleHost.Diagnostics`.
- `ArchitectureSnapshotDto` is a data-only record with no render logic.
- The panel's rendering must be visually unchanged.

**Success Conditions:**

1. _Unit test:_ `ArchitectureDiagnosticsService.GetSnapshot()` with a kernel hosting two modules
   returns `Modules.Count == 2`.

2. _Unit test:_ `Snapshot.Translators` list is non-empty when the kernel has registered
   translator-bearing systems.

3. _Regression:_ `ArchitectureDiagnosticsPanel` compiles and renders correctly in existing
   test fixtures.

---

### DD-P2-T04 — IEntityStateExtractionService

**Design Reference:** [Phase 2.3](./DESIGN.md#23-ientitystateextractionservice-fdptoolkits)

**Scope:**
- Create `FDP/Toolkits/Fdp.Toolkits/Diagnostics/IEntityStateExtractionService.cs` with
  `EntityStateDumpDto` record and the interface.
- Create `EntityStateExtractionService` wrapping `EntityJsonDumper.Dump` and
  `NetworkEntityMap` resolution.
- `ExtractEntities(null)` dumps all entities that have a `NetworkIdentity` component.
- `ExtractEntities(ids)` dumps only entities whose `NetworkIdentity.Value` is in `ids`.

**Constraints:**
- Lives in `Fdp.Toolkits.Diagnostics` namespace.
- Must not load entity data into memory more than once per call.
- The `NetworkIdentity` component value is the long-typed network ID.

**Success Conditions:**

1. _Unit test:_ Service with a mock `EntityRepository` containing 3 entities (all with
   `NetworkIdentity`) returns list of 3 `EntityStateDumpDto` from `ExtractEntities(null)`.

2. _Unit test:_ `ExtractEntities(new[] { 4001L })` returns only the entity whose
   `NetworkIdentity.Value == 4001`.

3. _Unit test:_ An entity without a `NetworkIdentity` component is excluded from
   `ExtractEntities(null)`.

---

### DD-P2-T05 — ILogArchiveExtractionService

**Design Reference:** [Phase 2.4](./DESIGN.md#24-ilogarchiveextractionservice-hrotcore)

**Scope:**
- Create `Hrot/Engine/Hrot.Core/Diagnostics/ILogArchiveExtractionService.cs`.
- Create `LogArchiveExtractionService` implementing streaming read/filter/write.
- Locates log files via the pattern `{SubsystemName}_{NodeId}*.log` in
  `HrotNodeConfig.LogDirectory`. This pattern matches both the active log and all rolling
  archives without ambiguity, and avoids matching log files belonging to other subsystem
  processes that share the same directory.
- Filters lines by: (a) parsed timestamp against `cutoffTime`, (b) parsed severity against
  `severityThreshold`.
- Uses `FileShare.ReadWrite` when opening log files so the active file is not locked.
- Does NOT use `string.Split`; uses `ReadOnlySpan<char>` for bracket token parsing.

**Constraints:**
- Lives in `Hrot.Core.Diagnostics` namespace.
- Entire method is `async` / `await`-based; I/O is via `StreamReader` and `StreamWriter`.
- Maximum memory usage per call is O(1) (one line buffer per input file).

**Success Conditions:**

1. _Unit test:_ Write a temp log file with 10 lines; 5 above threshold and 5 below. Call
   `ExtractLogsAsync` with that threshold. Output file contains exactly 5 lines.

2. _Unit test:_ Lines older than `maxAgeHours` are excluded.

3. _Unit test:_ A file that NLog is actively writing to (opened `FileShare.ReadWrite`) does not
   cause an `IOException`.

4. _Unit test:_ Cancellation via `CancellationToken` stops processing mid-stream without
   leaving a partial output file in a corrupt state (partial file acceptable but no exception
   propagation beyond `OperationCanceledException`).

---

## Phase 3: Multi-Select Copy-to-JSON in UI Panels

---

### DD-P3-T01 — EventBrowserPanel Multi-Select

**Design Reference:** [Phase 3.1](./DESIGN.md#31-eventbrowserpanel-multi-select)

**Scope:**
- Replace `_selectedEvent` (type `CapturedEventDto?`) with `_selectedEvents`
  (`HashSet<CapturedEventDto>`) and `_lastClickedIndex` (`int`, default `-1`).
- Implement multi-select in the ImGui Selectable loop:
  - Plain Click: clear `_selectedEvents`, add clicked item, update `_lastClickedIndex`.
  - Ctrl+Click: toggle clicked item in `_selectedEvents`, update `_lastClickedIndex`.
  - Shift+Click: compute inclusive index range
    `[min(_lastClickedIndex, currentIndex) .. max(_lastClickedIndex, currentIndex)]` in the
    current filtered+sorted view list (the `List<CapturedEventDto>` or array built before the
    Selectable loop each frame), add all items in that range to `_selectedEvents`. Do NOT
    update `_lastClickedIndex`.
- Update "Copy to JSON" context menu:
  - When 1 event is selected: existing behaviour (single object JSON).
  - When N > 1 events are selected: JSON array sorted by `Frame` ascending.
  - Both paths run through `FdpJsonOptionsRegistry.Indented` +
    `JsonAestheticFormatter.FlattenNumericArrays` + `ImGui.SetClipboardText`.

**Constraints:**
- The `Frame/Type` column rendering is NOT changed.
- The existing single-event detail pane is shown only when exactly 1 event is selected.
- When multiple events are selected, the detail pane shows "Multiple events selected".
- The Shift+Click range computation must use the filtered+sorted view list for the current
  frame, not the backing unfiltered circular buffer. Using the backing buffer would give
  incorrect indices when a provider or text filter is active.

**Success Conditions:**

1. _UI test (headless ImGui):_ Simulate Ctrl+Click on rows 1 and 3. `_selectedEvents.Count == 2`.

2. _Shift+Click range test:_ After plain-clicking row index 2, simulate Shift+Click on row
   index 5 in a view list of 8 items. `_selectedEvents` contains exactly the 4 items at
   indices 2, 3, 4, 5 of the view list; `_lastClickedIndex` remains 2.

3. _Unit test:_ The clipboard string produced for 2 selected events is a valid JSON array
   with 2 elements in ascending frame order.

4. _Unit test:_ `FixedString64` fields in the copied JSON are string values, not struct JSON.

5. _Regression:_ Single-event "Copy to JSON" (existing path) still works.

---

### DD-P3-T02 — EntityInspectorPanel Multi-Select

**Design Reference:** [Phase 3.2](./DESIGN.md#32-entityinspectorpanel-multi-select)

**Scope:**
- Extend `EntityInspectorPanel` to maintain `HashSet<Entity> _selectedEntities` and
  `int _lastClickedIndex` (default `-1`).
- Implement multi-select in the entity list:
  - Plain Click: clear `_selectedEntities`, add clicked entity, update `_lastClickedIndex`.
  - Ctrl+Click: toggle clicked entity, update `_lastClickedIndex`.
  - Shift+Click: compute inclusive index range in the current filtered+sorted entity view list,
    add all entities in that range to `_selectedEntities`. Do NOT update `_lastClickedIndex`.
- Add `IEntityContextMenuHandler.PopulateMenu(IReadOnlyCollection<Entity>, IContextMenuBuilder)`
  overload (the existing single-entity overload remains).
- "Copy to JSON (N items)" context menu item calls `IEntityStateExtractionService.ExtractEntities`
  with the selected entities' network IDs, then runs the two-stage JSON pipeline and copies
  to clipboard.

**Constraints:**
- The entity detail pane is shown only for exactly 1 selected entity; for multi-select it
  displays "Multiple entities selected — details not available".
- Existing single-entity context menu items (Center on Entity, Rename, Edit Shape, Delete…) are
  shown only when exactly 1 entity is selected.
- The Shift+Click range computation must use the filtered+sorted entity view list for the
  current frame, not the underlying entity repository. Using the repository directly would give
  incorrect indices when a name filter is active.

**Success Conditions:**

1. _Unit test:_ Select 3 entities, invoke "Copy to JSON (3 items)". Output is a valid JSON array
   with 3 elements each containing a `Components` dictionary with a `NetworkIdentity` entry.

2. _Shift+Click range test:_ After plain-clicking the entity at index 1, simulate Shift+Click
   on the entity at index 4 in a filtered view of 7 entities. `_selectedEntities` contains
   exactly the 4 entities at indices 1, 2, 3, 4; `_lastClickedIndex` remains 1.

3. _Unit test:_ Multi-select with entities lacking `NetworkIdentity` component falls back
   gracefully (entity is omitted or marked with null NetworkId).

4. _Regression:_ Existing single-select context menu operations are unaffected.

---

## Phase 4: Cluster-Wide Dump Orchestration Protocol

---

### DD-P4-T01 — Enum Extensions and DiagnosticDumpPayloadDto

**Design Reference:** [Phase 4.1–4.2](./DESIGN.md#41-enum-extensions)

**Scope:**
- Add `DumpDiagnostics = 16` to `ClusterOpType` enum.
- Add `DumpDiagnostics = 28` to `NodeOpType` enum.
- Add `DiagnosticDumpPayloadDto` record to
  `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs`.
- All DTO fields use `[JsonPropertyName(...)]` attributes and are serialised via
  `FdpJsonOptionsRegistry.DefaultRelaxed`.

**Constraints:**
- Enum integer values must not conflict with existing values (verified: ClusterOpType max = 15,
  NodeOpType max = 27).
- `DiagnosticDumpPayloadDto` is a `record` (immutable value type).
- `RequestedAt` is set by the Orchestrator/ExCon to local time at trigger time; nodes must
  not override it.
- `EventProviders = null/empty` means all providers; this contract is documented in XML doc.

**Success Conditions:**

1. _Compile test:_ `ClusterOpType.DumpDiagnostics == 16` and `NodeOpType.DumpDiagnostics == 28`.

2. _Round-trip test:_ `JsonSerializer.Serialize(dto, FdpJsonOptionsRegistry.DefaultRelaxed)`
   followed by `Deserialize` produces an equal `DiagnosticDumpPayloadDto`.

3. _Null provider semantics:_ Deserialising a DTO with `"EventProviders": null` produces a DTO
   where `EventProviders == null`.

4. _Timestamp test:_ Two DTOs created 1 second apart have different `RequestedAt` values;
   a DTO round-tripped via JSON preserves the `RequestedAt` value to second precision.

---

### DD-P4-T02 — ExecuteDiagnosticDumpIntent

**Design Reference:** [Phase 4.3](./DESIGN.md#43-executediagnosticdumpintent)

**Scope:**
- Add `ExecuteDiagnosticDumpIntent` struct to
  `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterOpIntents.cs`.
- `[EventId(9058)]` and `[DataPolicy(DataPolicy.NoRecord)]` attributes.
- Fields: `public Guid RequestId` and `public DiagnosticDumpPayloadDto Configuration`.

**Constraints:**
- EventId 9058 is the next available after existing 9057 (`LoadZoneIntent`).
- `DiagnosticDumpPayloadDto` is accessible from this file (it is in
  `Hrot.Network.Orchestration`; verify the project reference chain allows it or move the DTO
  to `Fdp.Toolkits` if needed to avoid a dependency issue).

**Dependency note:** `ClusterOpIntents.cs` is in `Fdp.Toolkits`, which does NOT directly
reference `Hrot.Network.Orchestration`. Therefore `DiagnosticDumpPayloadDto` must either:
(a) move to `Fdp.Toolkits.Orchestration`, or
(b) `ExecuteDiagnosticDumpIntent` carries a raw `string PayloadJson` and the DTO is decoded
downstream by the master translator.

Preferred: option (b) — `ExecuteDiagnosticDumpIntent` carries `string PayloadJson` matching the
pattern of `ExecuteStorageOpIntent` (inspect the actual field names used there for consistency).

**Success Conditions:**

1. _Compile test:_ `ExecuteDiagnosticDumpIntent` is accessible from `Fdp.Toolkits` and
   `Hrot.Network.Orchestration`.

2. _EventId uniqueness:_ No other struct in `ClusterOpIntents.cs` uses EventId 9058.

---

### DD-P4-T03 — ClusterOpEgressTranslator and ClusterOpMasterTranslator

**Design Reference:** [Phase 4.6](./DESIGN.md#46-clusteropegress-translator--clusteropmastertranslator)

**Scope:**
- In `ClusterOpEgressTranslator`: handle `ExecuteDiagnosticDumpIntent` by serialising the
  `DiagnosticDumpPayloadDto` into the `ClusterOpRequest.DomainPayload` and writing it to DDS
  with `OperationType = ClusterOpType.DumpDiagnostics`.
- In `ClusterOpMasterTranslator`: handle incoming `ClusterOpRequest` with
  `OperationType == DumpDiagnostics` by publishing `ExecuteDiagnosticDumpIntent` to the
  Orchestrator bus.

**Constraints:**
- Follow the exact same pattern as existing `SaveScenario` / `ExecuteStorageOpIntent` handling.
- No new DDS topics are introduced.

**Success Conditions:**

1. _Integration test:_ Publish `ExecuteDiagnosticDumpIntent` on ExCon bus; verify
   `ClusterOpEgressTranslator` writes a `ClusterOpRequest` with
   `OperationType == DumpDiagnostics` to the DDS mock.

2. _Integration test:_ Write a `ClusterOpRequest` with `OperationType == DumpDiagnostics` to
   the DDS mock; verify `ClusterOpMasterTranslator` publishes
   `ExecuteDiagnosticDumpIntent` on the master bus.

---

### DD-P4-T04 — DiagnosticsConsensusAggregator

**Design Reference:** [Phase 4.4](./DESIGN.md#44-diagnosticsconsensusaggregator)

**Scope:**
- Create `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsConsensusAggregator.cs`.
- Implement `INodeResponseAggregator` for `NodeOpType.DumpDiagnostics`.
- Flattens `List<FileManifestEntry>` payloads from all participating nodes into a single
  aggregated `List<FileManifestEntry>`.
- The full manifest (with `SourceUnc`) is retained internally for use by
  `DiagnosticsDumpProcessManager.PullToNasAsync`. When building the `ClusterOpStatus` payload
  to transmit back to ExCon over DDS, only `RelativeDest` paths are included (set `SourceUnc`
  to empty string or null in the serialised result); node-local absolute paths are inaccessible
  from ExCon and waste DDS payload budget.

**Constraints:**
- Mirrors `StorageConsensusAggregator` — same `INodeResponseAggregator` contract.
- Only handles `NodeOpType.DumpDiagnostics`; `CanHandle` returns false for all others.
- The stripping of `SourceUnc` before DDS transmission is enforced at this layer, not in the
  panel or cache.

**Success Conditions:**

1. _Unit test:_ Aggregate 3 `NodeOpStatus` responses each carrying 2 `FileManifestEntry` items.
   Result contains 6 entries.

2. _Unit test:_ A `NodeOpStatus` with empty manifest array is handled without exception
   (contributes 0 entries).

3. _Unit test:_ The aggregated result passed to `DiagnosticsDumpProcessManager` contains
   non-empty `SourceUnc` values; the manifest embedded in the `ClusterOpStatus` payload
   transmitted to ExCon has `SourceUnc` absent or empty for all entries.

---

### DD-P4-T05 — DiagnosticsDumpProcessManager

**Design Reference:** [Phase 4.5](./DESIGN.md#45-diagnosticsdumpprocessmanager)

**Scope:**
- Create `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsDumpProcessManager.cs`.
- Observes `ClusterOpCompletedEvent` for `ClusterOpType.DumpDiagnostics`.
- **Also observes transaction abort events** for `ClusterOpType.DumpDiagnostics`. On abort,
  immediately publishes `ClusterOpStatus(Failure)` without calling `PullToNasAsync`. This
  prevents a spurious SMB pull against a partial or empty manifest.
- Calls `StorageGatewayModule.PullToNasAsync` with the full aggregated manifest (SourceUnc
  + RelativeDest) only on a successful `ClusterOpCompletedEvent`.
- NAS destination base path comes from `_config.NasBasePath` (not
  `OrchestrationConstants.DefaultStagingDirectory`).
- After pull completion, publishes the final `ClusterOpStatus` (success or failure) back
  to the bus with the stripped manifest (RelativeDest only).

**Constraints:**
- Mirrors `StorageProcessManager` structure.
- NAS base path comes from `_config.NasBasePath` injected via constructor (same mechanism as
  `StorageProcessManager`); must NOT fall back to `OrchestrationConstants.DefaultStagingDirectory`.
- On pull failure, the error is published as a terminal `ClusterOpStatus` with a human-readable
  error message.
- Abort path must be handled deterministically; `PullToNasAsync` is never called on abort.

**Success Conditions:**

1. _Unit test:_ When `StorageGatewayModule.PullToNasAsync` returns `GatewayResult.Success`,
   `DiagnosticsDumpProcessManager` publishes `ClusterOpStatus(Success)`.

2. _Unit test:_ When pull returns failure, status is `ClusterOpStatus(Failure)` with error text.

3. _Unit test:_ When a transaction abort event is received, `PullToNasAsync` is NOT called and
   `ClusterOpStatus(Failure)` is published immediately.

---

## Phase 5: Node-Side Handler and NLog Configuration

---

### DD-P5-T01 — NLog File Target, Layout, and Auto-Rotation

**Design Reference:** [Phase 5.1](./DESIGN.md#51-nlog-programmatic-filetarget-and-layout)

**Scope:**
- In `Hrot/Runner/Hrot.ClusterRunner/Program.cs`, after CLI parsing, add a `FileTarget`
  to the existing `LoggingConfiguration`.
- Layout: `[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] [Node-${event-properties:nodeId}] ${message} ${exception:format=tostring}`
- File: `{LogDirectory}/{SubsystemName}_{NodeId}.log`
- Archive: `{LogDirectory}/{SubsystemName}_{NodeId}.{#}.log`, `ArchiveNumbering = Rolling`,
  `MaxArchiveFiles = 10`, `ArchiveAboveSize = 50 * 1024 * 1024`.
- `KeepFileOpen = true`, `ConcurrentWrites = false`.
- `Directory.CreateDirectory(LogDirectory)` must be called before setting up the target.

Including `SubsystemName` in the filename prevents file-lock collisions when multiple subsystem
processes (e.g., `SimHost` and `IG`) run on the same machine and happen to share a node ID value.

**Constraints:**
- Console target layout is NOT changed.
- `NLogMessageLogTarget` (in-process UI) continues to be registered.
- The `nodeId` structured property must be set on every log event (via `MappedDiagnosticsContext`
  or `LogEventInfo.Properties`). Prefer setting `NLog.MappedDiagnosticsLogicalContext` once at
  startup with the node ID.

**Success Conditions:**

1. _Integration test:_ After `Program.SetupNLog(config)`, `LogManager.Configuration` contains
   exactly one `FileTarget` with the specified layout.

2. _Integration test:_ Writing 5 log messages at `INFO` level produces a file in
   `config.LogDirectory` named `{SubsystemName}_{NodeId}.log` with exactly 5 lines matching the
   `[timestamp] [INFO] ...` format.

3. _Regression:_ Console output format is unchanged.

---

### DD-P5-T02 — HrotRunnerConfiguration `--log-dir` Option

**Design Reference:** [Phase 5.2](./DESIGN.md#52-hrotrunnerconfiguration----log-dir-option)

**Scope:**
- Add `[Option("log-dir", Required = false, ...)] public string LogDirectory { get; set; }` to
  `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`.
- Default: `Path.Combine(AppContext.BaseDirectory, "logs")`.
- After parsing, `resolvedLogDir = Path.GetFullPath(config.LogDirectory)`.
- Pass `resolvedLogDir` into `HrotNodeConfig.LogDirectory` during subsystem bootstrapping.

**Constraints:**
- Backward compatible: running without `--log-dir` uses the default.
- `Path.GetFullPath` ensures relative paths are expanded.

**Success Conditions:**

1. _Unit test:_ Parsing `--log-dir C:\MyLogs` sets `LogDirectory == "C:\\MyLogs"`.

2. _Unit test:_ Parsing without `--log-dir` sets `LogDirectory == default path`.

---

### DD-P5-T03 — HrotNodeConfig.LogDirectory

**Design Reference:** [Phase 5.3](./DESIGN.md#53-hrotnodeconfiglogdirectory)

**Scope:**
- Add `public string LogDirectory { get; set; } = string.Empty;` to
  `Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeConfig.cs`.

**Constraints:**
- `string.Empty` default means "not configured" — `LogArchiveExtractionService` must skip
  log extraction gracefully when `LogDirectory` is empty.

**Success Conditions:**

1. _Compile test:_ `HrotNodeConfig` has a `LogDirectory` property of type `string`.

2. _Behavioural test in `LogArchiveExtractionService`:_ When `LogDirectory` is empty, calling
   `ExtractLogsAsync` returns immediately without creating an output file or throwing.

---

### DD-P5-T04 — DiagnosticsDumpClusterOpHandler

**Design Reference:** [Phase 5.4](./DESIGN.md#54-diagnosticsdumpclusterophandler-hrotcommon)

**Scope:**
- Create `Hrot/Engine/Hrot.Common/Diagnostics/DiagnosticsDumpClusterOpHandler.cs`.
- Implement `IClusterStateHandler`.
- Constructor: `IDiagnosticEventHistoryService`, `IArchitectureDiagnosticsService`,
  `IEntityStateExtractionService`, `ILogArchiveExtractionService`, `HrotNodeConfig`.
- `CanHandle`: returns `true` for `NodeOpType.DumpDiagnostics`.
- `PrepareAsync`: spawns background `Task.Run(LongRunning)`, returns immediately.
- Background task:
  1. Deserialise `DiagnosticDumpPayloadDto` from `intent.PayloadJson` using
     `FdpJsonOptionsRegistry.DefaultRelaxed`.
  2. Skip if `TargetNodeIds != null && !TargetNodeIds.Contains(nodeId)`.
  3. `timestamp = dto.RequestedAt.ToString("yyyyMMdd_HHmmss")`.
  4. `outputDir = Path.Combine(LocalTempRoot, "dumps", dto.TransactionId.ToString("N"))`.
  5. For each requested dump kind, serialise through the two-stage JSON pipeline:
     - Entities / Architecture: call the corresponding service, serialise as a flat JSON
       array or object via `FdpJsonOptionsRegistry.Indented`, post-process via
       `JsonAestheticFormatter.FlattenNumericArrays`, optionally wrap in markdown.
     - Events: call `_eventService.GetHistory(provider)` for each entry in `dto.EventProviders`
       (or `GetHistory(null)` if empty/null), group the results into a
       `Dictionary<string, List<CapturedEventDto>>` keyed by provider name, then serialise
       the dictionary (not a flat array) via `FdpJsonOptionsRegistry.Indented` +
       `JsonAestheticFormatter.FlattenNumericArrays`. This provider-keyed output format is
       mandatory for the `ClusterDiagnosticsPanel` aggregation step to correctly reconstruct
       the `{ "Subsystem": { "Provider": [...] } }` composite schema.
  6. Return `List<FileManifestEntry>` as `object?`.
- `Commit`: no-op.
- `Abort`: `Directory.Delete(outputDir, recursive: true)` if it exists.
- File naming: `dump_{YYYYMMDD_HHmmss}_{kind}_{SubsystemName}_{NodeId}.{ext}`
  where DATETIME = `dto.RequestedAt.ToString("yyyyMMdd_HHmmss")`.
- Register handler in each node's bootstrapper (SimHost, CGF, IG, ExCon).

**Constraints:**
- `Hrot.Common.csproj` must directly or transitively reference `Fdp.Toolkits` for
  `IClusterStateHandler`, `JsonAestheticFormatter`, and `FdpJsonOptionsRegistry`. Verify the
  existing transitive chain (`Hrot.Common` -> `Hrot.Network.Orchestration` -> `Fdp.Toolkits`)
  is sufficient; if not, add a direct `Fdp.Toolkits` project reference.
- The handler must NOT access any ImGui or Raylib types.
- Log files use `.log` extension regardless of `UseMarkdownWrapper`.

**Success Conditions:**

1. _Unit test:_ Mock all four services. Call `PrepareAsync` and `await` the returned task.
   Assert `FileManifestEntry` list is returned with one entry per enabled dump kind.

2. _Unit test:_ When `TargetNodeIds` does not include this node's ID, `PrepareAsync` returns a
   completed task with a null or empty manifest.

3. _Unit test:_ `Abort` deletes the staging directory created during a prior `PrepareAsync`.

4. _File naming test:_ File name matches pattern
   `dump_{YYYYMMDD_HHmmss}_entities_{SubsystemName}_{NodeId}.json`.

5. _Markdown wrapper test:_ When `UseMarkdownWrapper = true`, the file content starts with
   ```` ```json ```` and ends with ```` ``` ````.

---

### DD-P5-T05 — Node LocalTempRoot Isolation and ClusterConfiguration NasBasePath

**Design Reference:** [Phase 5.5](./DESIGN.md#55-node-localtemproot-isolation-and-nas-path-separation)

**Scope:**
- Find `ClusterConfiguration` (or equivalent orchestrator configuration class) and add:
  `public string NasBasePath { get; init; } = @"C:\FDP_Temp\shared";`
- In `OrchestratorSubsystem.Initialize()` (or equivalent bootstrapper), replace every use of
  `OrchestrationConstants.DefaultStagingDirectory` as a NAS base path with `_config.NasBasePath`.
  Affected process managers: `StorageProcessManager`, `AssetInventoryProcessManager`,
  `AssetPrefetchProcessManager`, and the new `DiagnosticsDumpProcessManager`.
- In each subsystem bootstrapper that creates `HrotNodeConfig` (SimHost, CGF, IG, ExCon,
  and any others), namespace `LocalTempRoot` by node ID:
  ```
  var baseTempRoot = rawConfig.LocalTempRoot ?? OrchestrationConstants.DefaultStagingDirectory;
  hrotConfig.LocalTempRoot = Path.Combine(baseTempRoot, "nodes", $"node-{localNodeId}");
  ```
  This makes node 400's staging root `C:\FDP_Temp\nodes\node-400`, isolating it from every
  other node and from the NAS shared root.

**Constraints:**
- The default `NasBasePath` (`C:\FDP_Temp\shared`) must be different from the default
  node `LocalTempRoot` base (`C:\FDP_Temp\nodes\node-{id}`) so that `File.Copy` source and
  destination are never identical in a single-machine deployment.
- Existing scenario/checkpoint operations that already use `StorageProcessManager` must
  continue to work correctly after the `NasBasePath` wiring change; their relative destination
  paths already assume a NAS base and are not affected.
- The `LocalTempRoot` namespacing must be applied at bootstrap time, before any module is
  initialised. Do not modify `OrchestrationConstants.DefaultStagingDirectory` itself.

**Success Conditions:**

1. _Unit test:_ After bootstrapping with `NodeId = 400`, `HrotNodeConfig.LocalTempRoot` ends
   with `nodes\node-400`.

2. _Unit test:_ Two nodes bootstrapped on the same machine with different IDs produce different
   `LocalTempRoot` values.

3. _Unit test:_ `DiagnosticsDumpProcessManager` receives `NasBasePath = "C:\\FDP_Temp\\shared"`
   and combines it with `RelativeDest = "dumps\\foo.json"` to produce destination path
   `"C:\\FDP_Temp\\shared\\dumps\\foo.json"`.

4. _Integration test:_ In a single-machine run, `StorageGatewayModule.PullToNasAsync` with a
   `FileManifestEntry` whose `SourceUnc` is under `nodes\node-{id}` and `RelativeDest` targets
   `shared\...` does not throw `IOException` (source != destination).

---

## Phase 6: Cluster Diagnostics UI Panel

---

### DD-P6-T01 — ClusterDiagnosticsPanel (Configuration + Execution)

**Design Reference:** [Phase 6.1](./DESIGN.md#61-clusterdiagnosticspanel)

**Scope:**
- Create `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterDiagnosticsPanel.cs`.
- Configuration section: Markdown checkbox, dump-kind matrix (rows) x subsystem-type columns
  (from `ClusterUiCache.ReachableTargets`), log filter controls, EXECUTE button.
- On EXECUTE:
  - Sanitise the "Entities (Selected)" network ID input: split the raw `ImGui` string buffer
    by `','`, trim whitespace from each token, include only tokens where `long.TryParse`
    succeeds. Malformed tokens are silently discarded; the panel may display a brief inline
    warning when at least one token is discarded.
  - Resolve subsystem column selections to concrete node IDs from `ClusterUiCache`.
  - Build `DiagnosticDumpPayloadDto` with the sanitised `SpecificNetworkIds` list and
    generate `TransactionId` (new `Guid`).
  - Publish `ExecuteDiagnosticDumpIntent` to the local bus.
- Subsystem-type column cells that do not support a dump kind (e.g. entities for ExCon if it
  has no simulation) show as disabled (`[-]`).

**Constraints:**
- Uses only `ClusterUiCache` for reading cluster state (CQRS read-side).
- Must compile without direct DDS dependencies.
- Network ID sanitisation must occur entirely in the panel before DTO construction;
  the cluster master must never receive non-`long` entries in `SpecificNetworkIds`.

**Success Conditions:**

1. _Headless ImGui test:_ Render panel with a mock `ClusterUiCache` containing 3 subsystem
   types. Matrix has 3 data columns.

2. _Integration test:_ Click EXECUTE button; verify `ExecuteDiagnosticDumpIntent` is published
   to the bus with `TargetNodeIds` matching the selected columns.

3. _Sanitisation test:_ Input string `"4001, abc, , 4002"` in the network ID field; verify
   the published DTO's `SpecificNetworkIds` equals `[4001L, 4002L]` (malformed and empty
   tokens discarded, whitespace trimmed).

---

### DD-P6-T02 — ClusterDiagnosticsPanel (Results Tree + Context Menus)

**Design Reference:** [Phase 6.1](./DESIGN.md#61-clusterdiagnosticspanel)

**Scope:**
- Results section: tree grouped by subsystem type -> node ID -> file entries.
- File entries are `ImGui.Selectable` with `BeginPopupContextItem`:
  - Copy Content: read NAS file, `ImGui.SetClipboardText`.
  - Copy NAS Path: `ImGui.SetClipboardText(entry.RelativeDest)`.
  - Open from NAS: `Process.Start(new ProcessStartInfo { FileName = uncPath, UseShellExecute = true })`.
  - Save Local Copy As: `await _fileDialogService.ShowSaveAsDialogAsync(...)` + `File.Copy`.
- `ClusterUiCache` update triggers tree rebuild.
- Progress status shown above tree.
- **Subsystem group context menus**: each subsystem group node exposes a
  `BeginPopupContextItem` with "Copy Aggregated JSON". Invocation immediately renders a
  "Copying..." label and spawns a background `Task` (non-blocking render loop compliance):
  1. Collect all dump file entries (entity and event files) for that group's nodes from
     the manifest.
  2. Read each NAS file (bounded to 10 MB; skip oversized files and record inline warnings).
  3. For **entity** files: parse each flat JSON array via `FdpJsonOptionsRegistry.DefaultRelaxed`,
     accumulate into one merged `List<object>`, wrap as `{ "SubsystemName": [...] }`.
  4. For **event** files: parse each provider-keyed JSON object
     (`{ "ProviderName": [...] }`) via `FdpJsonOptionsRegistry.DefaultRelaxed`, merge event
     arrays per provider across nodes, wrap as
     `{ "SubsystemName": { "ProviderName": [ ...merged... ], ... } }`.
  5. Serialise via `FdpJsonOptionsRegistry.Indented` +
     `JsonAestheticFormatter.FlattenNumericArrays`.
  6. Set `_pendingClipboardText` (a `volatile string?` field on the panel). The per-frame
     render method polls this field and calls `ImGui.SetClipboardText(_pendingClipboardText)`
     then clears it, ensuring clipboard access happens on the render thread.
- **Root node context menu**: same async "Copy Aggregated JSON" action combining all
  subsystem groups into the full cross-cluster composite:
  - Entities: `{ "CGF": [...], "SimHost": [...] }`
  - Events: `{ "CGF": { "World": [...], "Perception": [...] }, "SimHost": { ... } }`

**Constraints:**
- `Save Local Copy As` must not block the render loop; use `async void` with
  `CancellationToken` from the panel's lifetime.
- File read for "Copy Content" must be bounded (reject files > 10 MB, show error).
- "Copy Aggregated JSON" must NOT perform any file I/O or JSON parsing on the render thread;
  all work runs in a background `Task`; `ImGui.SetClipboardText` is called only from the
  per-frame render method via the `_pendingClipboardText` marshal field.
- Only one "Copy Aggregated JSON" operation may be in progress at a time; a second invocation
  while one is already running is a no-op (menu item shown as disabled).

**Success Conditions:**

1. _Unit test:_ Context menu "Copy NAS Path" writes the expected path string to the clipboard
   mock.

2. _Unit test:_ "Save Local Copy As" invokes `IFileDialogService.ShowSaveAsDialogAsync`
   and calls `File.Copy` to the returned path.

3. _Edge case:_ "Copy Content" for a missing file shows an inline error message in the tree
   instead of throwing.

4. _Unit test:_ "Copy Aggregated JSON" on a subsystem group with 2 nodes' entity dump files
   produces a JSON object with the subsystem name as key and a merged entity array containing
   all entities from both files as value.

5. _Unit test:_ "Copy Aggregated JSON" on a subsystem group with 2 nodes' event dump files
   (both provider-keyed) produces `{ "SubsystemName": { "World": [...merged...], ... } }`
   with events from both nodes merged per provider.

6. _Unit test:_ "Copy Aggregated JSON" on the root node with 2 subsystem groups produces
   `{ "CGF": [...entities...], "SimHost": [...entities...] }` for entity aggregation and
   `{ "CGF": { "World": [...] }, "SimHost": { ... } }` for event aggregation.

7. _Async / thread-safety test:_ "Copy Aggregated JSON" is triggered from the render thread.
   Verify that `ImGui.SetClipboardText` is not called synchronously inside the menu callback;
   it is called in a subsequent frame when the render method polls `_pendingClipboardText`.

8. _Edge case:_ "Copy Aggregated JSON" with one NAS file exceeding 10 MB skips that file;
   the clipboard JSON is assembled from the remaining files and the panel displays an inline
   warning identifying the skipped file.

---

### DD-P6-T03 — Register ClusterDiagnosticsPanel in OrchestratorSubsystem and ExConSubsystem

**Design Reference:** [Phase 6.2](./DESIGN.md#62-registration)

**Scope:**
- Register `ClusterDiagnosticsPanel` as a "Diagnostics" docked tab in
  `OrchestratorSubsystem` and `ExConSubsystem`.
- The panel is constructed with injected `ClusterUiCache`, `FdpEventBus`, and
  `IFileDialogService`.

**Constraints:**
- `ExConSubsystem` already references `Hrot.Orchestrator`, so `ClusterDiagnosticsPanel`
  is accessible without new project references.
- The tab label must be "Diagnostics".

**Success Conditions:**

1. _Compile test:_ Both subsystems compile with the new panel registration.

2. _Integration test:_ Starting the subsystem in headless mode registers the panel without
   exceptions.

---

## Phase 7: IFileDialogService — Reusable Save As Dialog

---

### DD-P7-T01 — IFileDialogService Interface

**Design Reference:** [Phase 7.1](./DESIGN.md#71-ifiledialogservice-interface-fdppresentation)

**Scope:**
- Create `FDP/Engine/Fdp.Presentation/Abstractions/IFileDialogService.cs`.
- Define `Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter)`.

**Constraints:**
- Interface is in `Fdp.Presentation.Abstractions` namespace.
- The method is non-blocking to the caller: the task resolves only when the user completes
  or cancels the dialog.

**Success Conditions:**

1. _Compile test:_ Interface is accessible from `Hrot.Orchestrator`.

---

### DD-P7-T02 — ImGuiFileDialogService Implementation

**Design Reference:** [Phase 7.2](./DESIGN.md#72-imguifiledialogservice-fdppresentation)

**Scope:**
- Create `FDP/Engine/Fdp.Presentation/ImGui/Services/ImGuiFileDialogService.cs`.
- Implements `IFileDialogService`.
- State: `_isOpen`, `_currentDirectory`, `_fileNameBuffer`, `_extensionFilter`,
  `_tcs: TaskCompletionSource<string?>`.
- `ShowSaveAsDialogAsync`: sets state, creates new `TaskCompletionSource`, sets `_isOpen = true`,
  returns `_tcs.Task`.
- `Draw()`: calls `ImGui.OpenPopup` if `_isOpen`, renders the modal with:
  - Current directory display + "Up" button
  - Scrollable directory listing (subdirs with `[DIR]` prefix, files matching filter)
  - Double-click on dir: navigate into
  - Click on file: populate filename input
  - Filename `InputText`
  - Save / Cancel buttons
  - ImGui `x` close resolves the TCS with `null`.

**Constraints:**
- `Draw()` must be callable every frame even when the dialog is not open (no-op when `!_isOpen`).
- Only one dialog can be open at a time; a second `ShowSaveAsDialogAsync` call while a dialog
  is already open cancels the first via `_tcs.TrySetCanceled()`.
- Security: `Path.Combine` is used for all path construction; no raw string concatenation.
- Does NOT call `Directory.GetFiles` on the root of a drive (avoid excessive filesystem I/O);
  navigation starts from `Directory.GetCurrentDirectory()`.

**Success Conditions:**

1. _Unit test:_ Calling `ShowSaveAsDialogAsync` while another is pending cancels the prior task.

2. _Integration test (headless ImGui):_ Opening dialog, typing a filename, clicking Save:
   task resolves with the full path.

3. _Integration test (headless ImGui):_ Clicking Cancel resolves task with `null`.

4. _Unit test:_ Two rapid successive calls — first task is cancelled, second is pending.

---

### DD-P7-T03 — Wire ImGuiFileDialogService into WindowManager

**Design Reference:** [Phase 7.3](./DESIGN.md#73-registration)

**Scope:**
- Register `ImGuiFileDialogService` in the composition root of each subsystem that uses
  the Diagnostics panel (SimHost, ExCon, Orchestrator).
- In `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`, add a
  `IFileDialogService? _fileDialogService` field (set via property or constructor) and call
  `(_fileDialogService as ImGuiFileDialogService)?.Draw()` at the end of the main render method.

**Constraints:**
- If `WindowManager` does not have a render method, find the equivalent per-frame draw entry
  point and add the `Draw()` call there.
- The service must be drawn AFTER all other windows to guarantee the modal overlays everything.

**Success Conditions:**

1. _Integration test:_ `ImGuiFileDialogService.Draw()` is invoked each frame when registered
   in the `WindowManager`.

2. _Regression:_ Existing window rendering is unaffected.

---

## Phase 8: Cluster Log Merge

---

### DD-P8-T01 — DiagnosticLogMergeWorker

**Design Reference:** [Phase 8.1](./DESIGN.md#81-k-way-merge-algorithm)

**Scope:**
- Create `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticLogMergeWorker.cs`.
- Observes `MergeLogsIntent` on the local bus.
- Spawns `Task.Run(LongRunning)` to execute the K-way merge.
- Opens one `StreamReader` per source log file (NAS paths from the last completed operation's
  manifest filtered for `.log` files).
- Uses `PriorityQueue<(string Line, StreamReader Source), DateTime>`.
- Parses timestamps from the standardised `[YYYY-MM-DD HH:mm:ss.fff]` prefix via
  `ReadOnlySpan<char>` slicing.
- **Handles continuation lines:** when `TryParseTimestamp` fails on a line (e.g., an exception
  stack trace line), the line is appended to the most-recently-written output line rather than
  inserted as a new queue entry. This keeps stack traces attached to their originating log record
  in the merged output and prevents a crash from `DateTime.TryParseExact` on unparseable input.
- Writes output to `[NAS]/dumps/dump_{DATETIME}_logs_MERGED.log`.
- On completion: publishes `LogMergeCompletedEvent(string NasPath)`.

**Constraints:**
- Max memory usage is O(N) where N = number of source files (one line per stream in the queue).
- Timestamp parsing must not allocate a `string` per line; use `DateTime.TryParseExact` with
  a span overload.
- Continuation lines (no parseable `[YYYY-MM-DD ...]` prefix) must NOT cause an exception;
  they are buffered under the last successfully parsed timestamp.
- If a log file is inaccessible, it is skipped with a warning log entry in the merged output.

**Success Conditions:**

1. _Unit test:_ Merge 3 in-memory string sequences with interleaved timestamps. Output is
   correctly ordered by timestamp.

2. _Unit test:_ A log file containing a multi-line exception stack trace (lines 2+ have no
   timestamp prefix) merges correctly; all stack trace lines appear immediately after the
   originating log record in the output.

3. _Unit test:_ A file with an unparseable first line is skipped; remaining files merge cleanly.

4. _Unit test:_ Cancellation stops the merge; `LogMergeCompletedEvent` is NOT published.

---

### DD-P8-T02 — MergeLogsIntent and LogMergeCompletedEvent

**Design Reference:** [Phase 8.2](./DESIGN.md#82-integration)

**Scope:**
- Add `MergeLogsIntent` struct (no EventId needed — it is a local bus event only, not
  a DDS-carried event; `[DataPolicy(DataPolicy.NoRecord)]` is sufficient).
- Add `LogMergeCompletedEvent` struct with `string NasPath` field.
- Both defined in a new file
  `Hrot/Subsystems/Hrot.Orchestrator/Events/DiagnosticsMergeEvents.cs`.

**Constraints:**
- These events do NOT cross the DDS boundary.
- `LogMergeCompletedEvent` is consumed by `ClusterDiagnosticsPanel` to update the results tree.

**Success Conditions:**

1. _Compile test:_ Both types are visible from `ClusterDiagnosticsPanel`.

---

### DD-P8-T03 — Merged Log Entry in ClusterDiagnosticsPanel

**Design Reference:** [Phase 8.2](./DESIGN.md#82-integration)

**Scope:**
- Add `[ Generate Merged Cluster Log ]` button to the results section of
  `ClusterDiagnosticsPanel`. Button is only enabled after the operation is COMPLETED and
  at least one `.log` file appears in the manifest.
- Clicking the button publishes `MergeLogsIntent` with the NAS log file paths.
- Observing `LogMergeCompletedEvent`: add a "Cluster Aggregates" node at the top of the
  results tree with a "Merged Logs" file entry reusing the same context menu components as
  individual files (Copy Content, Copy NAS Path, Open from NAS, Save Local Copy As).

**Constraints:**
- The button is disabled (greyed out) while a merge is in progress.

**Success Conditions:**

1. _Unit test:_ Button is disabled when no logs are present in the manifest.

2. _Unit test:_ `LogMergeCompletedEvent` with a valid NAS path causes the "Cluster Aggregates"
   tree node to appear with the merged file entry.
