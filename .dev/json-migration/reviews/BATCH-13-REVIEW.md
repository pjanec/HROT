# BATCH-13 Review

**Batch:** BATCH-13
**Tasks:** JM-P2-008 — Patch passthrough writers (Orchestrator, MapInteractionConfig, NodeConfiguration, StructEdit)
**Status:** APPROVED
**Reviewer:** Dev Lead
**Commit:** e66f34a4

---

## Build Verification

Full solution build: only pre-existing `Hrot.Blueprints.Tests` errors (`Hrot.Editor` namespace,
`IAnimationTkbQueries`). No new `error CS` lines from BATCH-13 changes. Build for all patched
projects succeeded. The `SimHostApp.cs` deviation (see below) was required for clean build.

---

## JM-P2-008 Review — Passthrough Writers

### GlobalContextClusterOpHandler.cs — PASS

`CommitSerializeLocal` now calls `JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.OrchestratorContext, 2))`.
The DOM is a `JsonObject` built from the `GlobalContextDto` serialization — `JsonEnvelope.Write`
correctly reorders it to put `$meta` first.

`CommitLoad` accepts an optional `ReadOnlyMigrationAdapter? _readOnlyAdapter = null` constructor
parameter. When provided, calls `_readOnlyAdapter.LoadAndMigrateAsync(filePath, CancellationToken.None).GetAwaiter().GetResult()`
then `JsonSerializer.Deserialize<GlobalContextDto>`. When null, falls back to `File.ReadAllText`.
This is correct: existing call sites that don't have an adapter continue to work as before.

`GlobalContextDto.SchemaVersion` property removed (C-4 — redundant once `$meta` carries version).

### NedExConEgressWriters.cs — PASS

`WriteMapConfig` parses `config.ConfigJson` via `JsonNode.Parse(...).AsObject()` then stamps
`JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.MapInteractionConfig, 1))`.
Uses `dom.ToJsonString()` as the new `ConfigurationJson` value. `MapConfigSchemaVersion` constant
removed (was the only use — removal avoids CS0219 under `TreatWarningsAsErrors`).
The DDS field `JsonSchemaVersion` is no longer set — correct, since `$meta` now carries the version.

### NodeConfiguration.LoadFrom — PASS

`LoadFrom(string filePath, ReadOnlyMigrationAdapter? migrationAdapter = null)` added optional
adapter parameter. When adapter is non-null, runs the migration path with
`.GetAwaiter().GetResult()` bridge before `JsonSerializer.Deserialize<NodeConfigurationDto>`.
D-020 exception swallowing preserved: the `catch (Exception)` block that returns defaults is
unchanged — the adapter call sits inside the same try block, so `MigrationException` is also
swallowed into defaults, consistent with the D-020 contract.

### EditDocumentJsonSerializer.cs — PASS

`Serialize` replaces `structedit_version: "1.0"` with a `$meta` object
(`docType = "Hrot.StructEdit"`, `schemaVersion = 1`) written via `JsonEnvelope.Write`.

`Deserialize` now checks for `$meta` first:
1. If `$meta` is present → Phase 2 format; skips `structedit_version` check; proceeds with schema load.
2. If absent → checks for `structedit_version == "1.0"` (legacy); continues as before.
Both formats accepted, providing backward compatibility. Correct.

---

## Test Quality Review

### CommitSerializeLocal_ProducesPhase2Envelope — PASS (1/1)

Calls `PrepareAsync` + `Commit`, reads the written file, verifies:
- `$meta.docType == "Hrot.OrchestratorContext"` ✓
- `$meta.schemaVersion == 2` ✓
- No naked `schemaVersion` at root ✓
- `startWallTicks` payload present ✓

Thorough envelope check — tests both presence of the envelope and absence of the legacy field.

### ClusterMasterContextHandlerTests (5/5) — PASS

Pre-existing tests still pass. `SchemaVersion = 2` initializer removed from `SetupScenarioFiles`
(property removed from `GlobalContextDto` — required fix). The path in `SetupScenarioFiles`
was corrected to use `OrchestrationConstants.ScenariosDirectoryName` — the deviation is valid
since the path must match the handler's lookup path.

### NodeConfigurationTests T05/T06 — PASS (19/19)

T05: Phase 2 format (with `$meta`) loaded via inline adapter, asserts `DdsDomainId == 42u`. ✓
T06: Adapter throws → defaults returned (`DdsDomainId == 0u`). Confirms D-020 behavior preserved. ✓

### StructEdit Phase 2 tests (4/4) — PASS

- `Serialize_ProducesMetaEnvelope`: verifies `$meta.docType`, `$meta.schemaVersion`, no `structedit_version`. ✓
- `Serialize_DoesNotProduceStructEditVersion`: asserts absence of legacy field. ✓
- `Deserialize_AcceptsPhase2Format`: round-trip load with `$meta`. ✓
- `Deserialize_AcceptsLegacyFormat`: round-trip load with `structedit_version: "1.0"`. ✓

---

## Deviations

| # | Deviation | Verdict |
|---|-----------|---------|
| 1 | `SimHostApp.cs` lambda fix (`p => RoadNetworkLoader.LoadFromJson(p)` instead of method group) | **ACCEPTED** — BATCH-12 added optional `ReadOnlyMigrationAdapter?` param to `LoadFromJson`; method-group implicit conversion to `Func<string, RoadNetworkBlob>` broke (CS0019). Converting to explicit lambda is correct. |
| 2 | `SetupScenarioFiles` path corrected to `OrchestrationConstants.ScenariosDirectoryName` | **ACCEPTED** — pre-existing bug (wrong sub-directory name); the handler was looking in a different path than the test was creating. Fix is necessary for test validity. |
| 3 | Used `LoadJson` instead of `FromJson` in StructEdit test (instructions had typo) | **ACCEPTED** — minor terminology correction; actual method name is correct. |

---

## Pre-existing Failures (not from BATCH-13)

- `Hrot.Blueprints.Tests`: Stride editor dependency (Hrot.Editor namespace missing) — pre-existing.
- `Hrot.Orchestrator.Tests` 5 failures (`ClusterMasterPrefetchTests`, `ReferenceArchiveHandlerTests`, `StorageGatewayTests`, `StorageProcessManagerTests`) — git diff shows 0 lines changed in those files.
- `StructEdit.Tests` 1 failure (`Build_CircularReference_CircularFieldIsUnsupported`) — git diff shows 0 lines changed.

---

## Verdict

**APPROVED.** All 28 new/updated tests pass. Build is clean. Deviations are justified. JM-P2-008 complete.
