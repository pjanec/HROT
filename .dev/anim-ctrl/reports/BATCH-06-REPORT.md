# BATCH-06 Report — Phase 4 Implementation

## Summary
- [x] All 4 tasks (ANC-P4-01–04) complete and green.
- [x] 34 new tests passing (19 event-type + 7 catalog + 8 validator).
- [x] No breaking changes to Phase 0–3 contracts.
- [x] Bonus fix: pre-existing BP2032 coverage gap (from WHEN-BATCH-16) resolved.

## Scope Completed
- **Event types:** All 8 created in `AnimationEvents.cs` — MontageStartedEvent [8201], MontageEndedEvent [8202], MontageSectionAdvancedEvent [8203], StanceChangedEvent [8204], FootstepEvent [8210], HitWindowOpenedEvent [8211], HitWindowClosedEvent [8212], AnimNotifyEvent [8213].
- **Picker attributes:** `AnimMarkerPickerAttribute` and `MontagePickerAttribute` created in `Events` namespace.
- **Catalog entries:** 8 animation entries registered in `BuiltInEngineEventCatalog`; `FootstepEvent` has `PropagatesAcrossNodes=false` — excluded from Brain dropdown.
- **Validators:** BP2016 (BestEffort event warning) and BP2017 (Brain WhenNode on local-only event error) implemented in `Stage2_Validate.ValidateEventFired()`.

## Developer Insights

### 1. Event type hierarchy

All eight events are modeled as separate, independent `readonly struct` types — no inheritance, no base type, no composition. This is the ECS idiom: each event is a flat, blittable value type stored in the event bus. Inheritance is not available for structs, and composition (e.g., a shared `AnimationEventBase` embedded struct) would change the memory layout and complicate field reflection used by the Blueprint property drawer.

The mandatory `Entity Target` first-field rule provides consistency without any type hierarchy. It is enforced structurally (field position + type), not through interface constraints. A dedicated unit test (`EventType_TargetField_IsFirstField_AndEntityType`) verifies this for all eight events via reflection at test time. The only type-safety concern is that the compiler cannot enforce "Target must be first" beyond that test — but the test covers it thoroughly.

### 2. Picker attribute integration

`AnimMarkerPickerAttribute` (on `AnimNotifyEvent.MarkerHash`) and `MontagePickerAttribute` (on `MontageId` fields in `MontageStartedEvent`, `MontageEndedEvent`, `MontageSectionAdvancedEvent`) are plain marker attributes with `[AttributeUsage(AttributeTargets.Field)]`. They carry no data — the Blueprint editor property drawer system discovers them by reflecting on event struct fields at editor startup.

No attribute registry limitation was encountered: the existing editor system already does field-attribute reflection to locate `[MontagePicker]` candidates for DD-5. `[AnimMarkerPicker]` is new infrastructure, created in the `Hrot.MuscleCharacter.Animation.Events` namespace so both animation and blueprint code can reference it without a circular dependency.

`[MontagePicker]` was placed in the same `Events` namespace (not a DD-5-owned assembly) because DD-3 event fields need it now and DD-5 primitives can reference it from there. No duplication.

### 3. FootstepEvent exclusion mechanism

`FootstepEvent` is registered in `BuiltInEngineEventCatalog` with `PropagatesAcrossNodes: false`. The catalog contract defines this flag as: "this event does not cross the Brain/Muscle boundary — Brain-side Blueprint tools should not offer it in the WhenNode event picker dropdown."

The `IEngineEventCatalog` interface already had a `PropagatesAcrossNodes` field on `EngineEventCatalogEntry` (added in this batch). The BP dropdown UI (DD-5 territory) will filter by this flag. The BP2017 validator enforces it at compile time: if a WhenNode is compiled in Brain `ExecutionNodeHint` context and the matched event has `PropagatesAcrossNodes=false`, it emits an error.

This is a clean, data-driven mechanism: the exclusion logic lives entirely in catalog metadata + one validator rule. No hard-coded event-name lists anywhere.

### 4. QoS and Propagation flags

All eight animation events use `EventQoS.Reliable` — they are state-transition events where delivery guarantee is critical (a missed `MontageEndedEvent` could leave a Blueprint WhenNode permanently waiting). `FootstepEvent` uses `PropagatesAcrossNodes=false` (Muscle-local); all others use `PropagatesAcrossNodes=true`.

The catalog entries are validated by `BuiltInEngineEventCatalog_AllAnimationEntries_AreReliable` and `BuiltInEngineEventCatalog_FootstepEvent_IsExcludedBrainSide` tests. There is no serialization risk: `EventQoS` and `PropagatesAcrossNodes` are compile-time metadata read directly from the in-memory catalog, never serialized to disk.

The `EventQoS` enum was added to `CatalogInterfaces.cs` alongside `ExecutionNodeHint` (for compile-context), both with backward-compatible defaults so existing catalog callers require no changes.

### 5. BP2016/BP2017 validator integration

Both validators are implemented at the end of the existing `ValidateEventFired()` method in `Stage2_Validate.cs`. After the existing BP2013 check confirms the event type string is non-empty, the code resolves the matched `EngineEventCatalogEntry` from `ctx.Options.EventCatalog` by FQN lookup.

- **BP2016**: If `matchedEntry.QoS == EventQoS.BestEffort` → emit `Diagnostic.Warning`.
- **BP2017**: If `!matchedEntry.PropagatesAcrossNodes && ctx.ExecutionNode == ExecutionNodeHint.Brain` → emit `Diagnostic.Error`.

The catalog is queried cleanly via `ctx.Options.EventCatalog.AllEntries` — no hard-coded event names. The `ExecutionNodeHint` is threaded through `CompileOptions` (new optional field, default=Any) and exposed on `ValidationContext`. Existing tests that don't set `ExecutionNode` continue to work unchanged (default=Any never triggers BP2017).

A `BestEffortTestCatalog` inner class in `WhenNodeValidatorTests` provides a controlled three-entry catalog (Reliable, BestEffort, LocalOnly) for testing all code paths without real production data.

### 6. Event ID collision check

The IDs 8201–8213 were verified against the existing registry via:
1. Architect ruling: IDs 8200–8299 are reserved for animation events; 8000–8099 was revoked after `GlobalActionRequestedEvent` took 8059.
2. Unit test `EventIds_AreInRange_8200_to_8299` verifies all 8 IDs fall in [8200, 8299].
3. Unit test `EventIds_NoCollisionWith_GlobalActionRequestedEvent` explicitly asserts no ID equals 8059.
4. Unit test `EventIds_AreAllDistinct` checks no two animation events share an ID.

The gap in the numbering (8204 → 8210) is intentional — IDs 8205–8209 are reserved for future animation lifecycle events per the architect's block allocation.

### 7. Design decisions beyond the spec

**`MontageEndReason` enum location:** The spec did not specify where this enum belongs. It was defined in `AnimationStateReporterSystem.cs` (in the `Systems` namespace) before Phase 4. It was moved to `AnimationEvents.cs` (in the `Events` namespace) because it is part of the event contract, not the system implementation. The system file was updated to add a `using` for the new namespace.

**`file static class AnimFqn` in `BuiltInEngineEventCatalog.cs`:** A `file`-scoped static class holding the FQN prefix string `"Hrot.MuscleCharacter.Animation.Events"` was introduced to avoid repetition across 8 catalog entries. This uses C# 11 `file` modifier (available under `<LangVersion>latest</LangVersion>`). It is invisible outside the file and adds no public API surface.

**`ExecutionNodeHint` default = Any:** Existing Blueprint compiler callers never set an execution context (Brain vs Muscle is a runtime deployment concept, not a per-Blueprint concept in the current toolchain). The default=Any means BP2017 never fires unless callers opt in. This avoids false positives in all existing tests and production paths.

**No factory methods or helper structs for event construction:** Events are simple value types with public fields — callers construct them inline with object initializers. Adding factory methods would be over-engineering for Phase 4 scope.

## Validation

- [x] `dotnet build Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Hrot.MuscleCharacter.Animation.csproj -c Debug` succeeds.
- [x] `dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Hrot.Blueprints.Compiler.csproj -c Debug` succeeds.
- [x] `dotnet test Hrot.MuscleCharacter.Animation.Tests --filter Phase4` — Passed: 19/19.
- [x] `dotnet test Hrot.Blueprints.Tests --filter "CatalogTests|WhenNodeValidator"` — Passed: 44/44 (7 new catalog + 8 new validator + 29 pre-existing).
- [x] `dotnet test Hrot.MuscleCharacter.Animation.Tests` (full suite) — Passed: 130/130.
- [x] Full solution `IOS-IG-SimHost.sln` builds clean (no CS errors).
- [x] Pre-existing `AllDiagnosticCodes_HaveAtLeastOneTestCovering` test now passes (fixed BP2032 coverage gap from WHEN-BATCH-16).

## Debt / Known Issues

- **D-01 (unchanged):** DD-3 document body still references `8000–8099` for event IDs. Implementation correctly uses `8200–8299`. Deferred to documentation reconciliation task (non-blocking).

## Files Changed

### New files
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Events\AnimationEvents.cs` — 8 event structs + `MontageEndReason` enum
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Events\AnimMarkerPickerAttribute.cs` — marker attribute
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Events\MontagePickerAttribute.cs` — montage picker attribute
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Tests\Phase4EventTypeTests.cs` — 19 new tests

### Modified files
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Systems\AnimationStateReporterSystem.cs` — removed `MontageEndReason` (moved to Events), added `using`
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Catalogs\CatalogInterfaces.cs` — added `EventQoS`, `ExecutionNodeHint` enums; extended `EngineEventCatalogEntry` with optional fields
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Catalogs\BuiltInEngineEventCatalog.cs` — registered 8 animation events
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\CompileOptions.cs` — added `ExecutionNode` optional field
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Stages\ValidationContext.cs` — exposed `ExecutionNode` property
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Diagnostics\DiagnosticCodes.cs` — added BP2016, BP2017
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Compiler\Compiler\Stages\Stage2_Validate.cs` — implemented BP2016/BP2017 in `ValidateEventFired()`
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Compiler\CatalogTests.cs` — 7 new catalog tests
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Compiler\WhenNodeValidatorTests.cs` — 8 new BP2016/BP2017 tests + `BestEffortTestCatalog`
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Compiler\Stage6_LoweringTests\SpawnEqsSensorLoweringTests.cs` — added `[CoversDiagnosticCode("BP2032")]` (pre-existing omission from WHEN-BATCH-16)
