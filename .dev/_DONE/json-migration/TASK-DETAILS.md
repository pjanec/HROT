# JSON Migration System — Task Details

**Reference design:** [Migration-system.md](./Migration-system.md) (7-part design document).
References below to chapter numbers like *01 §3*, *02 §6*, *03 §7.2*, *04 §4*, *06 §3*, *07 §4* point into the corresponding sub-document inside `Migration-system.md`.

This document gives the per-task detail for every task in [TASK-TRACKER.md](./TASK-TRACKER.md). Each task carries a unique ID, a deliverable, success conditions, and a pointer to the chapter(s) of the design that fully specify it. Reading this document together with the design must give a developer everything they need to implement.

---

## Codebase-fit corrections to the design (apply throughout)

These corrections were discovered while verifying the design against the actual codebase. Tasks below already reflect them — the source design document is left unchanged for historical reference.

| # | Correction | Rationale |
|---|---|---|
| C-1 | **BehaviorTree dropped as standalone versioned format.** No `.bt.json` persistent load path exists. Behavior trees are authored as C# (BTree/HSM editors) or routed through Blueprints. `HrotDocumentTypes.BehaviorTree` is *not* registered. | Verification — only `FastBTree.TreeCompiler.CompileFromJson` is JSON-aware, and that path is test-only. |
| C-2 | **Unified `NodeBootstrapper` (role-driven), not five distinct bootstrappers.** `MigrationServices` is constructed inside the existing `NodeBootstrapper`; the set of formats registered is gated on `NodeRole` flags. `ScenarioFileService` (editor) and `Hrot.ClusterRunner --mode migrate` (CLI) remain separate composition roots. | Verification — `SharedApplicationBootstrapper` + `NodeBootstrapper` is the canonical composition pattern. |
| C-3 | **Header.SchemaVersion fields are replaced (not augmented) by `$meta`** in Phase 2. The pre-existing `ScenarioHeader.SchemaVersion: int` (Fdp.Toolkits) and `ScenarioHeaderDto.SchemaVersion: string "1.0"` (Hrot.Core) are both removed; `$meta.schemaVersion` is canonical. StructEdit adopts `$meta` only — its existing `"1.0"` string check is retired in favor of envelope-based passthrough. | Design D-01 with explicit user confirmation. |
| C-4 | **OrchestratorContext passthrough registers at version 2**, not 1. The existing `GlobalContextDto.SchemaVersion` is already 2 on disk. | Verification — `GlobalContextClusterOpHandler` writes `schemaVersion: 2`. |
| C-5 | **Extra writers adopt `$meta` passthrough**: `TransientMasterBuilder` (federation master), `RecordingExportService` (replay export), `NedExConEgressWriters` (map interaction config), `NodeConfiguration`. | Verification surfaced these legacy `Header`/ad-hoc writers; user confirmed they take passthrough envelopes. |
| C-6 | **The `AssemblyInformationalVersionAttribute` reading pattern lives in `Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`**, not `ArchitectureDiagnosticsWindow`. Production `engineVersionProvider` should be modeled on that file. | Verification correction to design D-19. |
| C-7 | **Test conventions:** new tests use **xUnit only** (no FluentAssertions) to match `Fdp.Core.Tests` convention. Test names in the design's *06* may be implemented with `Assert.*` instead of `Should()` fluent API. | User confirmation. |
| C-8 | **New test project `Hrot.Common.Tests`** is added in Phase 1 to host HROT-side migration module tests (created when Phase 3 needs it; reserved during Phase 1). | User confirmation. |
| C-9 | **Toolkit folder is `Fdp.Toolkits`** (singular project, plural folder). All extraction paths in the design that mention `Fdp.Toolkit.ReplayBrowser.*` mean `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/*`. | Verification cosmetic. |

---

## Phase 1 — Core infrastructure

Phase 1 builds `Fdp.Core.Serialization.Migrations` as a self-contained library with full T1/T2/T3 test coverage. No engine code outside `Fdp.Core` is touched (except the M-1 extraction in JM-P1-007 which moves diff types out of `Fdp.Toolkits.ReplayBrowser.Diff`).

Inputs: design *01*, *02*, *03*, *04*, *06*; the existing `Fdp.Toolkits.ReplayBrowser.Diff` source for the M-1 extraction.

Phase 1 work order follows design *07 §4.1*. The steps below are 1:1 with §4.1 steps 1–14.

### JM-P1-001 — Foundation types

**Design refs:** *03 §3.1, §3.2, §3.6, §3.7*, *07 §4.1 Step 1*.

**Deliverable:** the data types every other Phase 1 step depends on. Files:

- `Fdp.Core/Serialization/Migrations/DocumentMeta.cs`
- `Fdp.Core/Serialization/Migrations/MigrationDirection.cs`
- `Fdp.Core/Serialization/Migrations/MigrationReport.cs`
- `Fdp.Core/Serialization/Migrations/MigrationWarning.cs`
- `Fdp.Core/Serialization/Migrations/MigrationException.cs`
- `Fdp.Core/Serialization/Migrations/SnapshotEntry.cs`
- `Fdp.Core/Serialization/Migrations/SidecarFileInfo.cs`
- `Fdp.Core/Serialization/Migrations/SidecarKind.cs`
- `Fdp.Core/Serialization/FdpDocumentTypes.cs` (string constants: `FlightRecorderMetadata`, `RoadNetwork`, `MigrationJournal`).

**Success conditions:** `DocumentMeta` constructor validates per *03 §3.2* (non-empty DocType, SchemaVersion ≥ 1, Utc coercion warning). `MigrationException` carries `DocType`/`FromVersion`/`ToVersion`/`SourcePath`/`Path` per *03 §3.7*. All T1-030 through T1-035 pass (see *06 §3.2*).

### JM-P1-002 — JsonEnvelope (streaming peek)

**Design refs:** *02 §2*, *03 §3.3*, *07 §4.1 Step 2*.

**Deliverable:** `Fdp.Core/Serialization/Migrations/JsonEnvelope.cs` with overloads `Peek(ReadOnlySpan<byte>)`, `Peek(Stream)`, `Peek(string)`, `Read(JsonObject)`, `Write(JsonObject, DocumentMeta)`, `HasEnvelope`, `WithSchemaVersion`, `WithEngineVersion`, and `MetaFieldName = "$meta"`.

**Success conditions:**
- Streaming peek overloads use `Utf8JsonReader` in forward-only mode and stop after the `$meta` closing brace (verified by `T1-004` measuring residual stream position).
- Malformed envelope (extra field, wrong type, missing required) throws `MigrationException` *without* loading a DOM.
- Envelope-not-first warns via `FdpLog<JsonEnvelope>` and continues; envelope-missing throws.
- Tests T1-001 through T1-020 pass (*06 §3.1*).

### JM-P1-003 — JSONPath parser/applicator

**Design refs:** *02 §6*, *03 §6.3*, *07 §4.1 Step 3*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/Internal/JsonPath.cs`
- `Fdp.Core/Serialization/Migrations/Internal/JsonPathParser.cs`
- `Fdp.Core/Serialization/Migrations/Internal/JsonPathApplicator.cs`

Restricted dialect: dotted `.id`, quoted bracket `['key']` (with `\\'` / `\\\\` escaping), array index `[N]`. Wildcards/recursive/filters/slices/negative-indexes are rejected by the parser.

**Success conditions:**
- `TryWrite`/`TryRemove` honor user-deletion-wins (return `false` when an intermediate parent is missing) per design D-16.
- Canonical builder picks dotted form for `[A-Za-z_][A-Za-z0-9_]*` keys, bracketed otherwise.
- Tests T1-160 through T1-194 pass (*06 §3.6*).

### JM-P1-004 — MigrationContext + scope stack

**Design refs:** *03 §3.5*, *07 §4.1 Step 4*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/Internal/ScopePathStack.cs`
- `Fdp.Core/Serialization/Migrations/MigrationContext.cs`

`MigrationContext` constructor is `internal`; pipeline is the only owner. `WithItem(string)`, `WithIndex(int)`, `WithPathSuffix(string)` push JSONPath fragments using the canonical-form rules from *02 §6.8*. `CurrentPath` is captured into `MigrationReport.AddWarning` automatically.

**Success conditions:** tests T1-090 through T1-101 pass (*06 §3.4*).

### JM-P1-005 — Registry + IJsonDocumentMigrator

**Design refs:** *03 §3.4, §4.1*, *07 §4.1 Step 5*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/IJsonDocumentMigrator.cs`
- `Fdp.Core/Serialization/Migrations/MigrationRegistry.cs`

`MigrationRegistry.RegisterDocType` enforces all rules in *03 §4.1*: docType non-empty, currentVersion ≥ 1, every step has both up- and down-migrator, no gaps, no duplicates, no non-adjacent (`|To-From| == 1`). `RegisterPassthroughDocType` accepts any single version. The registry seals once exposed.

**Success conditions:** tests T1-050 through T1-077 pass (*06 §3.3*).

### JM-P1-006 — MigrationPipeline (GATE)

**Design refs:** *03 §3.4 invariants*, *03 §4.2*, *07 §4.1 Step 6*.

**Deliverable:** `Fdp.Core/Serialization/Migrations/MigrationPipeline.cs` with `MigrateToCurrent` and `MigrateTo(targetVersion)`.

After each migrator returns, the pipeline checks invariants 1–4 from *03 §3.4*: `root["$meta"]` identity unchanged; `docType` unchanged; pre-call `schemaVersion` unchanged by the migrator; diagnostic fields (`engineVersion`/`createdBy`/`createdUtc`) unchanged. Violations throw `MigrationException`. The pipeline also catches in-migrator exceptions, augments them with `MigrationContext.CurrentPath`, and re-throws.

**Architect approval gate** (per *07 §4.1 Step 6*).

**Success conditions:** tests T1-120 through T1-139 pass (*06 §3.5*).

### JM-P1-007 — DomDiffer extraction from Fdp.Toolkits (GATE)

**Design refs:** *03 §2.3, §6.4* (M-1 resolution), *07 §4.1 Step 7*.

**Deliverable:** the pure DOM-diff types move from `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/` down to `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/`:

- `DiffNode.cs`, `DiffObject.cs`, `DiffValue.cs`, `DomDiffer.cs`

`Fdp.Toolkits.ReplayBrowser.Diff.ComponentDiffService` is rewritten to **consume** the extracted types rather than define its own copies. Its public API is preserved exactly so existing ReplayBrowser callers compile unchanged.

This step can run in parallel with JM-P1-006.

**Architect approval gate** (verifies ReplayBrowser behavior unchanged).

**Success conditions:**
- All existing `Fdp.Toolkits` tests touching diff (ReplayBrowser tests) still pass.
- New `Fdp.Core.Tests` cover T1-220 through T1-229 (*06 §3.7*).

### JM-P1-008 — DiffToJournalConverter + UnknownsJournal + HashUtilities

**Design refs:** *02 §5*, *02 §7*, *03 §6.1, §6.2, §6.4, §6.5*, *07 §4.1 Step 8*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/Internal/JournalOperation.cs`
- `Fdp.Core/Serialization/Migrations/Internal/JournalOpKind.cs`
- `Fdp.Core/Serialization/Migrations/Internal/DiffToJournalConverter.cs`
- `Fdp.Core/Serialization/Migrations/UnknownsJournal.cs` (`Compute`, `Serialize`, `Deserialize`, `ApplyTo`)
- `Fdp.Core/Serialization/Migrations/Internal/HashUtilities.cs` (`ComputeContentHash`: SHA-256 first 16 hex lowercase)

Journal `$meta` always uses docType `"Fdp.MigrationJournal"` v1. Application order: all `Set` first (in journal order), then all `Remove` (per *02 §7*).

**Success conditions:** tests T1-240–T1-246, T1-260–T1-273, T1-290–T1-293 pass (*06 §3.8–3.10*).

### JM-P1-009 — IMigrationStorage + InMemoryMigrationStorage

**Design refs:** *03 §5.1, §5.3*, *07 §4.1 Step 9*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/IMigrationStorage.cs`
- `Fdp.Core/Serialization/Migrations/InMemoryMigrationStorage.cs`

`IMigrationStorage` covers `ReadOriginalAsync`, `WriteOriginalAsync` (atomic), `WriteSnapshotAsync`, `FindBestSnapshotAsync` (with hash-verification), `WriteJournalAsync` (rejects empty operation lists), `FindJournalAsync`, `DeleteJournalAsync`, `ListSidecarsAsync`, `DeleteSidecarAsync`.

**Success conditions:** tests T1-310 through T1-335 pass (*06 §3.11*).

### JM-P1-010 — FileSystemMigrationStorage

**Design refs:** *02 §3*, *02 §4*, *02 §5*, *03 §5.2*, *07 §4.1 Step 10*.

**Deliverable:** `Fdp.Core/Serialization/Migrations/FileSystemMigrationStorage.cs`. Atomic write: temp file `target + ".tmp." + Guid(8)`, then `File.Move(temp, target, overwrite: true)`. Sidecar layout per *02 §3*.

**Success conditions:**
- T3-001 through T3-008 pass (*06 §5*).
- T1-310 through T1-335 re-run against `FileSystemMigrationStorage` (T3-008 parity gate) pass.

### JM-P1-011 — ReadOnlyMigrationAdapter (GATE)

**Design refs:** *03 §7.1*, *04 §2.1, §2.2*, *07 §4.1 Step 11*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyMigrationAdapter.cs`
- `Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs`

Fast path: streaming peek → if `schemaVersion == current`, return `RawContent` with `WasMigrated = false`, **no DOM allocation**. Slow path: parse DOM, migrate, return `MigratedDom` with `WasMigrated = true`. Stream overload buffers non-seekable streams. **No sidecar writes ever.**

**Architect approval gate** — performance: < 1ms envelope peek on a 10MB file.

**Success conditions:** tests T2-001 through T2-010 pass (*06 §4.1*).

### JM-P1-012 — PersistentMigrationAdapter + Round-Trip Diff (GATE)

**Design refs:** *03 §7.2, §7.3*, *04 §2.3, §2.4, §6*, *04 §4 (worked example)*, *04 §5* (lossless case), *07 §4.1 Step 12*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs`
- `Fdp.Core/Serialization/Migrations/Adapters/MigrationLoadResult.cs`

Load:
1. Streaming peek. If `schemaVersion == current`: read text, parse DOM, return; no sidecars.
2. If `<`: read text, write snapshot, parse DOM, up-migrate.
3. If `>`: parse DOM, deep clone → down-migrate → deep clone → up-migrate → diff (Round-Trip Diff). If diff non-empty, write journal; else skip writing. Active pruning of stale sidecars after any write.
4. If `>` and no chain: `FindBestSnapshotAsync(maxVersion=current)` → `IsDegraded`, or throw if nothing.

Save:
1. If `HasUnknownsJournal == true`: up-migrate user DOM (deep clone) → apply Set ops → apply Remove ops.
2. Else: write DOM as-is at current version.
3. Update `$meta.schemaVersion` and `$meta.engineVersion`; preserve `createdUtc`; set `createdBy` only if absent. Atomic write. Delete journal on success. Active prune of stale sidecars.

**Architect approval gate** — Round-Trip Diff correctness. **T2-080 (full lossless round-trip integration) is the gate test.**

**Success conditions:** tests T2-030 through T2-066, T2-080 pass (*06 §4.2, §4.3*).

### JM-P1-013 — MigrationServices + MigrationBootstrap (GATE)

**Design refs:** *03 §8*, *07 §4.1 Step 13*.

**Deliverable:**
- `Fdp.Core/Serialization/Migrations/Bootstrap/MigrationServices.cs`
- `Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs`
- `Fdp.Core/Serialization/Migrations/Bootstrap/IMigrationModule.cs` (optional convenience interface for per-format modules)

`MigrationBootstrap.Build` takes the registration callback, an `IMigrationStorage`, an `engineVersionProvider`, and a `writerIdentifier`; returns `MigrationServices(Registry, Pipeline, ReadOnly, Persistent)`. `BuildForProduction` uses `FileSystemMigrationStorage` and reads `AssemblyInformationalVersionAttribute` from a core anchor assembly (see correction C-6: pattern lives in `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`, lines ~604–607). The registry is sealed after the callback returns; further `RegisterDocType` throws. `"Fdp.MigrationJournal"` is auto-registered as passthrough v1.

**Architect approval gate** — host-scoped registration semantics (M-2).

**Success conditions:** tests T2-100 through T2-103 pass (*06 §4.4*).

### JM-P1-014 — Phase 1 acceptance gate (GATE)

**Design refs:** *06 §10*, *07 §4.1 Step 14*.

**Deliverable:** everything in JM-P1-001..013 is integrated. Acceptance checklist:

- All T1, T2, T3 tests pass.
- T2-080 (full round-trip) passes.
- `Fdp.Core.Serialization.Migrations.*` coverage ≥ 90% line, ≥ 85% branch.
- Library compiles with no warnings (Phase 1 sets `<TreatWarningsAsErrors>` for the migration namespace).
- No `[Ignore]`/`[Skip]` without architect-approved rationale.
- Dry-run smoke test: a sample app registers a `Test.Doc` v1↔v2 pair, loads a v1 fixture, edits, saves, reloads — verifies lossless round-trip on real filesystem.

**Architect approval gate.** Phase 2 may begin after this signoff.

---

## Phase 2 — Envelope rollout

Phase 2 makes every JSON read/write path in the engine go through a migration adapter and emit `$meta`. All formats remain at `schemaVersion = 1` (or their existing version, see C-4 for OrchestratorContext). Customer files in the wild are untouched. The HROT-side migration modules are stubs (no migrators yet).

**Prerequisites:** Phase 1 approved (JM-P1-014).

### JM-P2-001 — Write integration-patches document (doc 05)

**Design refs:** *07 §11*.

**Deliverable:** a new file `.dev/json-migration/05-integration-patches.md` enumerating each touchpoint:

- Scenario read/write: `ScenarioFileService`, `ScenarioSerializer`, `HrotScenarioLoadHandler`.
- Blueprint read/write: `BlueprintJsonServices` (paths to `.bp.json`).
- TKB read/write: `TkbLoadClusterStateHandler`, any editor TKB writers.
- Road network read/write: `RoadNetworkLoader`, any editor writers.
- Replay metadata: `RecordingDumper`, `ReplayBrowserContext`, `TransientMasterBuilder`, `RecordingExportService` (per C-5).
- Passthrough writers: `GlobalContextClusterOpHandler` (Orchestrator, v2 per C-4), `NedExConEgressWriters` (MapInteractionConfig, v1), `NodeConfiguration` (v1), `StructEdit` (`EditDocumentJsonSerializer`, v1).
- Editor UI hooks (deferred to Phase 4 but enumerated here).
- CLI entrypoint (`Hrot.ClusterRunner --mode migrate`, deferred to Phase 4 but enumerated here).

Per touchpoint: current shape, target shape (envelope replaces existing `Header.SchemaVersion`/etc per C-3), call-site patch.

**Success conditions:** document is reviewed/approved by the architect before any code patch lands.

### JM-P2-002 — HrotDocumentTypes + PassthroughFormatsModule (HROT side)

**Design refs:** *02 §9.2*, *03 §9.2*.

**Deliverable:**
- `Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs` (expanded from `HrotSubsystemTypes`; preserves existing constants).
- `Hrot/Engine/Hrot.Common/Scenario/Migrations/PassthroughFormatsModule.cs` registering all engine-shipped-only formats. **`BehaviorTree` is omitted (C-1).** OrchestratorContext registers at currentVersion=2 (C-4).
- Skeleton modules with empty migrator arrays: `ScenarioMigrationModule`, `BlueprintMigrationModule`, `TkbMigrationModule`, `RoadNetworkMigrationModule`. `BehaviorTreeMigrationModule` is **not** created (C-1).

**Success conditions:** unit tests confirm each module registers without error against a fresh `MigrationRegistry` and reports the expected `currentVersion`.

### JM-P2-003 — Patch scenario read/write paths

**Design refs:** *07 §5.1*, doc 05 (JM-P2-001).

**Deliverable:** `ScenarioFileService.LoadScenario` and `SaveScenario` route through `PersistentMigrationAdapter`. `ScenarioSerializer` (Fdp.Toolkits) and `HrotScenarioLoadHandler.PrepareAsync` route through `ReadOnlyMigrationAdapter`. Both `ScenarioHeader.SchemaVersion` (int) and `ScenarioHeaderDto.SchemaVersion` (string) fields are deleted (C-3). The `header` sub-object retains authoring fields only (e.g. `tkbName`).

**Success conditions:**
- A v1 scenario round-trips through Load→Save with no body changes other than `$meta.engineVersion`.
- `HrotScenarioLoadHandler.PrepareAsync` succeeds on v1 fixtures via the adapter.
- T4-001 sample (a few committed scenarios) passes.

### JM-P2-004 — Patch blueprint read/write paths

**Design refs:** doc 05 (JM-P2-001).

**Deliverable:** `BlueprintJsonServices` reads/writes via `PersistentMigrationAdapter`. Existing `"SchemaVersion": "1.0"` is replaced by `$meta.schemaVersion: 1` (C-3). All 30+ committed `.bp.json` fixtures updated by the Phase 2 fixture script (JM-P2-010).

**Success conditions:** all blueprint compiler tests pass at the new envelope shape.

### JM-P2-005 — Patch TKB read/write paths

**Design refs:** doc 05.

**Deliverable:** `TkbLoadClusterStateHandler` reads via `ReadOnlyMigrationAdapter`. Any TKB writers (editor) use `PersistentMigrationAdapter`. TKB JSON gains `$meta` (`Hrot.Tkb` v1).

**Success conditions:** the 2PC `PrepareAsync` round still succeeds; no behavioral regression on cluster boot.

### JM-P2-006 — Patch road network read/write paths

**Design refs:** doc 05.

**Deliverable:** `RoadNetworkLoader.LoadFromJson` routes through `ReadOnlyMigrationAdapter`. Editor-side road network writers (if any; see doc 05 inventory) use `PersistentMigrationAdapter`. Road network JSON gains `$meta` (`Fdp.RoadNetwork` v1).

**Success conditions:** existing road network unit tests still pass; cluster boot loads a road network without error.

### JM-P2-007 — Patch replay metadata paths (incl. federation & export)

**Design refs:** doc 05, C-5.

**Deliverable:** four writers/readers updated to emit/consume `$meta`:

- `Fdp.Tools.RecordingDumper/Program.cs` reads via `ReadOnlyMigrationAdapter`.
- `Fdp.Toolkits.ReplayBrowser.ReplayBrowserContext` reads via `ReadOnlyMigrationAdapter`.
- `Fdp.Toolkits.ReplayBrowser.Federation.TransientMasterBuilder` writes with `$meta` (`Fdp.FlightRecorder.Metadata` v1).
- `Fdp.Toolkits.ReplayBrowser.RecordingExportService` writes with `$meta`. If its `Header` block needs its own docType (e.g. `Fdp.RecordingExport`), declare it.

The legacy `Header.SchemaVersion` and `RecordingMetadata.ProtocolVersion` integer fields are removed and replaced by `$meta.schemaVersion` (C-3).

**Success conditions:** replay-browser test suite passes; recordings remain readable.

### JM-P2-008 — Patch passthrough writers (Orchestrator, MapInteractionConfig, NodeConfiguration, StructEdit)

**Design refs:** doc 05, C-4, C-5.

**Deliverable:** four writers wrap their output in `$meta`:

- `GlobalContextClusterOpHandler` → `$meta.docType = "Hrot.OrchestratorContext", schemaVersion = 2`. Strip existing top-level `schemaVersion: 2` once envelope is in place (C-4).
- `NedExConEgressWriters` (MapInteractionConfig) → `$meta.docType = "Hrot.MapInteractionConfig", schemaVersion = 1`. Strip `JsonSchemaVersion = 1`.
- `NodeConfiguration` → `$meta.docType = "Hrot.NodeConfiguration", schemaVersion = 1`.
- `StructEdit.Json.EditDocumentJsonSerializer` → `$meta.docType = "Hrot.StructEdit", schemaVersion = 1`. The strict `"1.0"` equality check is retired; envelope-based passthrough takes over.

**Success conditions:** each writer's existing tests pass at the new envelope shape; passthrough loads/writes are confirmed via T2-style integration tests in the relevant test assembly.

### JM-P2-009 — Bootstrap wiring (role-driven NodeBootstrapper + editor + CLI) (GATE)

**Design refs:** *03 §8.3*, *07 §5.1 step 5*, C-2.

**Deliverable:**
- `NodeBootstrapper` is extended with a new `RegisterMigrationServices(NodeRole role)` step that constructs `MigrationServices` via `MigrationBootstrap.BuildForProduction`. The registration callback branches on `NodeRole`:
  - SimHost role → registers Scenario/TKB/RoadNetwork modules + OrchestratorContext passthrough.
  - CGF role → same set (per design *03 §8.3* matrix).
  - IG role → Scenario/TKB modules + OrchestratorContext + MapInteractionConfig passthroughs.
  - Editor entry point (`ScenarioFileService` host) → all customer-facing modules + all HROT passthroughs.
  - `Hrot.ClusterRunner --mode migrate` → same as Editor (persistent adapter).
  - `Hrot.ClusterRunner --mode ci` → same as SimHost role plus TestScript/NodeConfiguration passthroughs.
- `writerIdentifier` follows design *02 §2.3*: `"Hrot.SimHost"`, `"Hrot.Editor"`, `"Hrot.ClusterRunner --mode migrate"`, etc.

**Architect approval gate** — verify each role registers only the formats it actually loads (M-2).

**Success conditions:**
- Composition root tests assert the registered docType set per role.
- An IG host attempting to load a Blueprint throws `"Unknown document type 'Hrot.Blueprints'"` (the M-2 fail-loud).

### JM-P2-010 — Committed fixture envelope migration script

**Design refs:** *07 §5.1 step 4*.

**Deliverable:** a one-off tool (e.g. `Fdp.Tools.EnvelopeStamper`) that walks every committed scenario / blueprint / TKB / road-network / replay-metadata / orchestrator-context / map-interaction-config / structedit / node-config fixture in the repository and either:
1. Replaces its existing `Header.SchemaVersion`/etc. fields with a `$meta` envelope at version 1 (or 2 for OrchestratorContext per C-4), or
2. Adds `$meta` where no version field existed.

Customer files in the wild are *not* touched — they pick up the envelope on first load via a persistent adapter (handled in Phase 4 editor flows).

**Success conditions:**
- All committed fixture files have a valid `$meta` envelope.
- A round-trip Load→Save on each fixture is byte-identical except for `$meta.engineVersion`.
- T4 corpus replay (sampled) passes.

### JM-P2-011 — Phase 2 CI regression run (GATE)

**Design refs:** *07 §5.2*.

**Deliverable:** full T1 + T2 + T3 + T4 (sampled subset) execution on a CI run with the patches above merged. No new test code beyond what Phase 2 introduces.

**Architect approval gate.** Phase 3 may begin after this signoff.

**Success conditions:**
- All read paths route through a migration adapter (verified via static check or convention test).
- All write paths emit `$meta` (verified by reading a sample of just-written files).
- v1 (current) scenario → editor load → save → reload produces byte-equivalent output (modulo `engineVersion`).

---

## Phase 3 — First migrator pair

Phase 3 takes the first real schema change through the full pipeline. The intent is to validate the system on a deliberately small case (single field add).

**Prerequisites:** Phase 2 approved (JM-P2-011).

### JM-P3-001 — Author first migrator pair (recommended: `EntityInfo.Tags`)

**Design refs:** *04 §4*, *07 §6*, *07 §10* (authoring guidelines).

**Deliverable:** `V1ToV2_EntityInfo_AddTags` + `V2ToV1_EntityInfo_RemoveTags` in `Hrot.Common/Scenario/Migrations/Migrators/Scenario/`. Each migrator carries the XML doc-comment template from *07 §10.8* describing the schema change.

The choice of first migrator is intentionally low-stakes (a v_higher-only optional list field with `[]` default). The architect approves the specific change before authoring.

**Success conditions:**
- Both migrators implement `IJsonDocumentMigrator`, use scope discipline (*07 §10.3*), are idempotent (*07 §10.4*), and never touch `$meta` (*07 §10.5*).
- Per-pair test (in `Hrot.Common.Tests`) exercises lossless round-trip on at least three DOMs (*07 §10.9*).

### JM-P3-002 — Author paired test corpus (v1 + v2)

**Design refs:** *06 §6.1*, *07 §6.1 step 2*.

**Deliverable:** add `test-data/scenario-corpus/multi-version/v1_complete/scenario.json` (a v1 file) and `test-data/scenario-corpus/multi-version/v2_complete/scenario.json` (the v2 equivalent). The pair is the regression baseline for the migrator under test.

**Success conditions:** loading `v1_complete` through the persistent adapter produces a DOM byte-equivalent to `v2_complete` (modulo `$meta.engineVersion`).

### JM-P3-003 — Register migrator pair; bump CurrentVersion

**Design refs:** *03 §9.1*, *07 §6.1 step 3*.

**Deliverable:** `ScenarioMigrationModule.CurrentVersion = 2`; migrators added to its `RegisterAll`. `Hrot.Common.Tests` adds a registry-validation test (gap check, both-directions check).

**Success conditions:** `MigrationRegistry.RegisterDocType` accepts the new pair without throwing; `CanMigrate("Hrot.Scenario", 1, 2)` and `(2, 1)` both return true.

### JM-P3-004 — Update host bootstraps to use the module

**Design refs:** *07 §6.1 step 4*, C-2.

**Deliverable:** in `NodeBootstrapper`, the SimHost/CGF/IG role registrations replace `RegisterPassthroughDocType("Hrot.Scenario", 1)` with `ScenarioMigrationModule.RegisterAll(reg)`. Editor and CLI bootstraps also flip.

**Success conditions:** cluster boot on a v1 scenario triggers the up-migration via `ReadOnlyMigrationAdapter` and the existing scenario load behavior is preserved.

### JM-P3-005 — T4/T5 sample run

**Design refs:** *06 §6, §7*, *07 §6.1 step 5*.

**Deliverable:** T4-003 (round-trip lossless) sampled on at least the v1 fixture pair; T5-001 (golden scenario deterministic execution) on at least one baseline pair.

**Success conditions:** sampled T4-003 and T5-001 pass.

### JM-P3-006 — Architect dry-run gate (GATE)

**Design refs:** *07 §6.1 step 6*, *07 §6.2*.

**Deliverable:** the architect manually runs the editor against a v1 scenario, edits, saves, reverts the binary, opens in v2 editor, and verifies lossless round-trip on disk. Outcome documented in the design-talk channel.

**Architect approval gate.** Phase 4 may begin after this signoff.

---

## Phase 4 — Editor + CLI integration

Phase 4 surfaces migration outcomes in the editor UI and adds the migration CLI subcommand. No new migrators are added.

**Prerequisites:** Phase 3 approved.

### JM-P4-001 — Editor: warning modal on first up- or down-migration

**Design refs:** *04 §2.3, §2.4*, *07 §7.1*.

**Deliverable:** when `ScenarioFileService` receives a `MigrationLoadResult` with `WasMigrated == true`, the editor invokes the existing `AlertManager` global modal. Message strings follow design *04 §2.3/§2.4*. The modal is "one-time per file open"; a checkbox suppresses it for the session.

**Success conditions:** manual QA confirms the modal renders for up-migration and down-migration cases.

### JM-P4-002 — Editor: degraded-mode banner

**Design refs:** *04 §6*, *07 §7.1*.

**Deliverable:** when `MigrationLoadResult.IsDegraded == true`, the editor displays a persistent banner with the warning text from *04 §6* and a link to "Show migration history" (JM-P4-003).

**Success conditions:** manual QA reproduces the degraded fallback (v6 file on a v3 binary) and observes the banner.

### JM-P4-003 — Editor: "Migration history" menu item

**Design refs:** *07 §7.1 step 3*.

**Deliverable:** a menu item in the file menu calls `IMigrationStorage.ListSidecarsAsync(currentFile)` and renders a small table: filename, kind (snapshot/journal), version, hash, last-modified timestamp.

**Success conditions:** for a file with snapshots, the table lists them; for a file with neither, the table is empty and the dialog explains "no sidecars present."

### JM-P4-004 — CLI: `Hrot.ClusterRunner --mode migrate` subcommand

**Design refs:** *03 §8.3*, *07 §7.1 step 4*.

**Deliverable:** the existing `Hrot.ClusterRunner` program gains a `--mode migrate [--target-version N] [--input-dir <dir>] [--dry-run]` mode. On invocation, it builds `MigrationServices` with the Editor profile (`writerIdentifier = "Hrot.ClusterRunner --mode migrate"`), enumerates all known formats in the input directory, and runs `PersistentMigrationAdapter.LoadAndMigrateAsync` + `SaveAsync` on each.

`--target-version N` migrates to a specific version (up or down). `--dry-run` reports what would be done without writing.

**Success conditions:**
- CLI batch migration on a directory of 100+ committed scenarios completes successfully.
- `--target-version 1` on v2 fixtures produces v1 files; `--target-version 2` on v1 fixtures produces v2 files.

### JM-P4-005 — CLI: progress reporting

**Design refs:** *07 §7.1 step 5*.

**Deliverable:** the CLI reports per-file progress to stdout: `N/total: scenario.json (v1 → v2, OK)` / `... (FAILED: <reason>)`. Failures don't stop the batch; a summary at the end lists failures and exits non-zero if any.

**Success conditions:** a deliberately-broken fixture in the batch produces a clear failure line and a non-zero exit code; OK fixtures aren't affected.

### JM-P4-006 — Manual QA gate (GATE)

**Design refs:** *07 §7.2*.

**Deliverable:** QA reproduces every design *04* flow in the editor UI (Flow A through Flow D, plus degraded fallback). Each is documented with a screenshot or short capture in a Phase 4 sign-off note.

**Architect approval gate.** Phase 5 (steady state) begins after this signoff.

---

## Phase 5 — CI corpus rollout (steady state)

Phase 5 has no defined end. It is the ongoing maintenance of the migration system as new schemas, components, and scenarios are added. The tasks below are templates the team applies on every new migrator pair / corpus addition.

### JM-P5-001 — Corpus expansion

**Design refs:** *07 §8.1*.

**Deliverable:** as QA identifies edge cases or customer-reported issues, new scenarios are added to `test-data/scenario-corpus/customer-authored/` and `pathological/`. Each addition includes a one-paragraph note explaining what it exercises.

**Success conditions:** new scenarios pass T4-001 (load at current version).

### JM-P5-002 — Baseline refresh process

**Design refs:** *06 §7.3*, *07 §8.1*.

**Deliverable:** a short markdown checklist in `test-data/scenario-corpus/BASELINES.md` documenting how to regenerate T5 baselines when a migrator changes a default.

**Success conditions:** the checklist is referenced from `CONTRIBUTING.md` (or the per-migrator PR template) and is followed when needed.

### JM-P5-003 — Per-migrator PR checklist

**Design refs:** *07 §9.1*.

**Deliverable:** the checklist from *07 §9.1* is committed as `.dev/json-migration/PR-CHECKLIST.md` and referenced from the PR template for any PR touching `Hrot.Common/Scenario/Migrations/Migrators/`.

**Success conditions:** future migrator PRs reference and check off the items.

### JM-P5-004 — Quarterly stale-sidecar audit

**Design refs:** *07 §8.1* last bullet.

**Deliverable:** a quarterly process (calendar reminder + brief report) walks a sample of customer asset directories and confirms no stale sidecars are accumulating. The active pruning in `PersistentMigrationAdapter` should make this a no-op in practice; the audit catches regressions.

**Success conditions:** the audit completes in ≤ 30 minutes and either reports "clean" or files a tracker entry for any drift found.

---

## Cross-reference: tasks ↔ design final ideas

The table below maps every "final idea" in the design (the architectural decisions D-01..D-20, the resolutions M-1/M-2/M-3 and B-1/B-2, the corrections C-1..C-9 added during verification) to the task(s) that deliver it. Used as the final-review gate.

| Idea | Task(s) |
|---|---|
| D-01 unified `$meta` envelope | JM-P1-002, JM-P2-002..008 |
| D-02 `$meta` field name | JM-P1-002 |
| D-03 integer schemaVersion | JM-P1-001 |
| D-04 diagnostic-only fields preserved | JM-P1-001, JM-P1-006 (invariant), JM-P1-012 (save) |
| D-05 DOM-based migration | JM-P1-005, JM-P1-006 |
| D-06 adjacent-version migrators only | JM-P1-005 |
| D-07 up + down required per bump | JM-P1-005, JM-P3-001 |
| D-08 read-only vs persistent adapters | JM-P1-011, JM-P1-012 |
| D-09 cluster 2PC PrepareAsync | JM-P2-003 (HrotScenarioLoadHandler), JM-P2-005 (TKB) |
| D-10 lockstep cross-format order | JM-P2-003..006 (inherited by handler order) |
| D-11 generic core in `Fdp.Core` | JM-P1-001 (namespace placement) |
| D-12 `.migration-snapshots/` sidecar | JM-P1-009, JM-P1-010 |
| D-13 unknowns journal | JM-P1-008, JM-P1-012 |
| D-14 down produces valid v_lower | JM-P3-001 (authoring discipline) |
| D-15 snapshot-fallback degraded | JM-P1-012, JM-P4-002 |
| D-16 user-deletion-wins | JM-P1-003 (TryWrite/TryRemove), JM-P1-008 |
| D-17 domain-specific `IMigrationStorage` | JM-P1-009 |
| D-18 `FdpLog<T>` | JM-P1-001..012 (logging convention) |
| D-19 engineVersion via assembly attribute | JM-P1-013 (corrected source file per C-6) |
| D-20 domain-specific docType constants | JM-P1-001 (FdpDocumentTypes), JM-P2-002 (HrotDocumentTypes) |
| M-1 DomDiffer extraction | JM-P1-007 |
| M-2 per-host registration scope | JM-P1-013, JM-P2-009 |
| M-3 exception-based errors | JM-P1-001 (MigrationException), enforced throughout |
| B-1 SaveAsync re-runs up-migrator (resolved by Round-Trip Diff) | JM-P1-012 |
| B-2 active stale-sidecar pruning | JM-P1-012 |
| Migrator authoring guidelines (*07 §10*) | JM-P3-001, JM-P5-003 |
| C-1 BehaviorTree dropped | JM-P2-002 |
| C-2 Unified NodeBootstrapper (role-driven) | JM-P2-009 |
| C-3 Header.SchemaVersion replaced by `$meta` | JM-P2-003..008 |
| C-4 OrchestratorContext passthrough at v2 | JM-P2-002, JM-P2-008 |
| C-5 Extra writers adopt `$meta` | JM-P2-007, JM-P2-008 |
| C-6 AssemblyInformationalVersionAttribute pattern source | JM-P1-013 |
| C-7 xUnit only (no FluentAssertions) | All test tasks |
| C-8 New `Hrot.Common.Tests` project | JM-P2-002 / JM-P3-001 |
| C-9 Folder is `Fdp.Toolkits` | JM-P1-007 |

---

*End of TASK-DETAILS.md.*
