# Fdp.Core.Serialization.Migrations

**Namespace**: `Fdp.Core.Serialization.Migrations`
**Assembly**: `Fdp.Core` (`FDP/Engine/Fdp.Core/Fdp.Core.csproj`)
**Subfolder**: `FDP/Engine/Fdp.Core/Serialization/Migrations/`
**Date**: 2026-05-30
**Phases shipped**: Phase 1 (core library), Phase 2 (envelope rollout), Phase 3 (first migrator pair), Phase 4 (editor + CLI integration)

---

## Executive Overview

`Fdp.Core.Serialization.Migrations` is the **generic JSON document migration infrastructure**
for the FDP/HROT engine. It enables versioned JSON files authored by customers (scenarios,
blueprints, TKB definitions, road networks, replay metadata, behavior trees) to remain
readable and editable across binary versions of the engine, including across customer-initiated
version downgrades.

The library is a self-contained subsystem inside `Fdp.Core` — it has no dependencies on
higher-level HROT types and can therefore be used by any process in the cluster stack
(SimHost, IG, CGF, Editor, ClusterRunner). Format-specific migrators live in
application-layer assemblies (`Hrot.Common.Scenario.Migrations`, etc.) and are registered
into this core via `MigrationBootstrap`.

### Core responsibilities

- **Unified `$meta` envelope**: every versioned JSON document carries a `$meta` block with
  `docType`, `schemaVersion`, and optional diagnostic fields.
- **Registry and pipeline**: `MigrationRegistry` maps `(docType, fromVersion, toVersion)` to
  `IJsonDocumentMigrator` implementations; `MigrationPipeline` composes and runs migration
  chains.
- **Two adapter shapes**: `ReadOnlyMigrationAdapter` (cluster nodes, diagnostic tools) and
  `PersistentMigrationAdapter` (editor, CLI) encapsulate the full load-and-migrate contract.
- **Round-trip preservation via journals**: `PersistentMigrationAdapter` computes an
  `UnknownsJournal` on down-migration — capturing fields removed by the down-migrator — so
  that a subsequent save-back can restore them without data loss.
- **Sidecar file management**: pre-migration snapshots and unknowns journals are written to a
  `.migration-snapshots/` subdirectory adjacent to the source file.
- **Bootstrap**: `MigrationBootstrap` assembles the infrastructure once per process and
  returns a `MigrationServices` bundle.

### Role in the Larger Solution

```
+-------------------------------------------------------------------+
|  Application Layer                                                |
|  Hrot.Common.Scenario.Migrations  (ScenarioMigrationModule, etc.) |
|  Hrot.Editor                      (MigrationAlertManager, UI)     |
|  Hrot.ClusterRunner               (MigrateMode --mode migrate)    |
+------------------------------------+------------------------------+
                                     | registers into / uses
                                     v
+-------------------------------------------------------------------+
|  Fdp.Core.Serialization.Migrations  (this subsystem)             |
|  MigrationRegistry, MigrationPipeline                            |
|  ReadOnlyMigrationAdapter, PersistentMigrationAdapter            |
|  JsonEnvelope, MigrationBootstrap, UnknownsJournal               |
+-------------------------------------------------------------------+
                                     |
                                     v
+-------------------------------------------------------------------+
|  System.Text.Json.Nodes  (JsonObject DOM)                        |
|  FdpLog<T>               (NLog facade, engine convention)        |
+-------------------------------------------------------------------+
```

---

## Architecture

### Key Architectural Decisions

**D-01: Unified `$meta` envelope across all formats**
All versioned JSON documents begin with a `$meta` object as their first property. The
envelope is the single routing key for migration. A document without `$meta` is rejected
at the adapter boundary.

**D-02: `$` prefix, dotted doc-type strings**
Envelope property name `$meta` follows the existing engine precedent (`$guid` in TKB files).
Document type identifiers use dotted notation matching .NET namespaces: `"Hrot.Scenario"`,
`"Fdp.RoadNetwork"`, etc.

**D-03: Monotonic per-format integer schema versions**
`schemaVersion` is an independent integer per document type. There is no global engine
version number in the migration routing logic.

**D-05: DOM-based migration**
Migrators operate on `System.Text.Json.Nodes.JsonObject`, mutating in place. No typed
V1/V2/V3 DTO classes are maintained. This naturally handles unknown fields and aligns with
the engine's convention of not retaining historical DTOs.

**D-06 + D-07: Adjacent-version, paired up/down migrators**
Each `IJsonDocumentMigrator` transforms exactly one version step (`FromVersion = N`,
`ToVersion = N+1` or `N-1`). Every schema bump requires both an up-migrator and a
down-migrator, landing together in the same PR.

**D-08: Two adapter shapes for three load contexts**

| Context | Adapter | Sidecar files written? |
|---|---|---|
| Cluster PrepareAsync, diagnostic read-only | `ReadOnlyMigrationAdapter` | No |
| Editor load/save, CLI `--mode migrate` | `PersistentMigrationAdapter` | Snapshots + journals |

**D-11: Core in `Fdp.Core`, migrators in application assemblies**
The generic infrastructure has zero application-layer dependencies. Format modules
(`ScenarioMigrationModule`, etc.) live in their respective assemblies and call
`MigrationRegistry.RegisterDocType` during bootstrap.

**D-12: `.migration-snapshots/` sidecar directory**
The `PersistentMigrationAdapter` writes a verbatim pre-up-migration copy of the original
file as `{baseName}.v{N}.{hash16}.snapshot.json` in a `.migration-snapshots/` subfolder
alongside the source file. Sidecars co-locate with the authored file for git and zip transport.

**D-13 + D-14: Unknowns journal for lossless down-migration**
After a down-migration, `DomDiffer` computes the diff between pre- and post-migration DOMs.
`DiffToJournalConverter` flattens the diff into a list of JSONPath-addressed `Set`/`Remove`
operations stored as `{baseName}.v{N}.{hash16}.unknowns.json`. On save-back, the journal
operations restore the higher-version-exclusive content around the user's edits.

**D-20: Domain-specific document-type constant classes**
`FdpDocumentTypes` (in `Fdp.Core.Serialization`) declares FDP-owned doc types.
`HrotDocumentTypes` (in `Hrot.Common.Scenario`) declares HROT-owned types. No central enum
exists inside the migration core.

---

## File Layout

```
FDP/Engine/Fdp.Core/Serialization/Migrations/
|-- DocumentMeta.cs
|-- JsonEnvelope.cs
|-- MigrationContext.cs
|-- MigrationDirection.cs
|-- MigrationException.cs
|-- MigrationPipeline.cs
|-- MigrationRegistry.cs
|-- MigrationReport.cs
|-- MigrationWarning.cs
|-- IJsonDocumentMigrator.cs
|-- IMigrationStorage.cs             (internal)
|-- FileSystemMigrationStorage.cs   (internal)
|-- InMemoryMigrationStorage.cs     (internal)
|-- SidecarFileHelper.cs
|-- SidecarFileInfo.cs
|-- SidecarKind.cs
|-- SnapshotEntry.cs
|-- UnknownsJournal.cs              (internal)
|-- Adapters/
|   |-- MigrationLoadResult.cs
|   |-- PersistentMigrationAdapter.cs
|   |-- ReadOnlyLoadOutcome.cs
|   +-- ReadOnlyMigrationAdapter.cs
|-- Bootstrap/
|   |-- MigrationBootstrap.cs
|   +-- MigrationServices.cs
+-- Internal/
    |-- DiffToJournalConverter.cs
    |-- HashUtilities.cs
    |-- JournalOperation.cs
    |-- JournalOpKind.cs
    |-- JsonPath.cs
    |-- JsonPathApplicator.cs
    |-- JsonPathParser.cs
    |-- ScopePathStack.cs
    +-- Diff/
        |-- DiffNode.cs
        |-- DiffObject.cs
        |-- DiffValue.cs
        +-- DomDiffer.cs
```

---

## The `$meta` Envelope

Every versioned JSON document must begin with a `$meta` object as its first property:

```json
{
  "$meta": {
    "docType":       "Hrot.Scenario",
    "schemaVersion": 2,
    "engineVersion": "0.7.2",
    "createdBy":     "Hrot.Editor",
    "createdUtc":    "2026-05-28T14:32:11Z"
  },
  "header": { ... },
  "entities": { ... }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `docType` | `string` | Yes | Document type identifier. Must match a registered doc type. |
| `schemaVersion` | `int` | Yes | Monotonic integer, >= 1. Per-format independent sequence. |
| `engineVersion` | `string?` | No | Engine build that last wrote the file. Diagnostic only. |
| `createdBy` | `string?` | No | Tool that authored or last wrote the file. Diagnostic only. |
| `createdUtc` | `DateTime?` | No | First-authored timestamp. Immutable across migrations. |

`engineVersion`, `createdBy`, and `createdUtc` are **never read by migration logic**. They
exist purely for support diagnostics. `createdUtc` is preserved across all migrations;
`engineVersion` is updated to the current binary on every save.

### Document type constant classes

```
FDP/Engine/Fdp.Core/Serialization/FdpDocumentTypes.cs   -- FDP-owned doc types
Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs   -- HROT-owned doc types
```

| Constant | Value | Format |
|---|---|---|
| `FdpDocumentTypes.FlightRecorderMetadata` | `"Fdp.FlightRecorder.Metadata"` | Replay metadata |
| `FdpDocumentTypes.RoadNetwork` | `"Fdp.RoadNetwork"` | Road network graph |
| `FdpDocumentTypes.MigrationJournal` | `"Fdp.MigrationJournal"` | Unknowns journal (internal) |
| `HrotDocumentTypes.Scenario` | `"Hrot.Scenario"` | Cross-node scenario payload |
| `HrotDocumentTypes.Blueprint` | `"Hrot.Blueprints"` | Compiled blueprint asset |
| `HrotDocumentTypes.BehaviorTree` | `"Hrot.BehaviorTree"` | Behavior tree file |
| `HrotDocumentTypes.TkbDefinition` | `"Hrot.Tkb"` | TKB entity definition |
| `HrotDocumentTypes.StructEdit` | `"Hrot.StructEdit"` | StructEdit session (passthrough) |
| `HrotDocumentTypes.MapInteractionConfig` | `"Hrot.MapInteractionConfig"` | Map interaction (passthrough) |
| `HrotDocumentTypes.OrchestratorContext` | `"Hrot.OrchestratorContext"` | Orchestrator context (passthrough v2) |
| `HrotDocumentTypes.NodeConfiguration` | `"Hrot.NodeConfiguration"` | Node config (passthrough) |

---

## Key Types

### `DocumentMeta` (sealed record)

Parsed representation of the `$meta` envelope. Constructed by `JsonEnvelope.Peek/Read`.

| Member | Type | Description |
|---|---|---|
| `DocType` | `string` | Document type identifier. |
| `SchemaVersion` | `int` | Schema version integer (>= 1). |
| `EngineVersion` | `string?` | Diagnostic: engine build. |
| `CreatedBy` | `string?` | Diagnostic: authoring tool. |
| `CreatedUtc` | `DateTime?` | Diagnostic: first-authored timestamp. |

Constructor validates that `DocType` is non-null/non-empty and `SchemaVersion >= 1`.
Non-UTC `createdUtc` values are coerced to UTC with a `FdpLog<DocumentMeta>.Warn`.

---

### `JsonEnvelope` (static class)

Reads and writes the `$meta` envelope. All members are static.

| Member | Description |
|---|---|
| `const string MetaFieldName` | `"$meta"` |
| `DocumentMeta Peek(ReadOnlySpan<byte>)` | Streaming forward-only parse from raw UTF-8 bytes. No DOM allocation. |
| `DocumentMeta Peek(Stream)` | Streaming parse from a readable stream. Seeks back past `$meta` if seekable. |
| `DocumentMeta Peek(string path)` | Loads file bytes and calls span overload. |
| `DocumentMeta Read(JsonObject)` | Reads `$meta` from an already-parsed DOM. |
| `void Write(JsonObject, DocumentMeta, Func<string>, string)` | Writes or updates `$meta` in a DOM. Preserves `createdUtc`. |
| `void EnsurePresent(JsonObject, string, int, Func<string>, string)` | Writes `$meta` only if absent (used by passthrough writers). |

The `Peek` overloads use `Utf8JsonReader` and never allocate a full DOM — on the fast path
(document already at current version) the adapter short-circuits without DOM allocation.

---

### `IJsonDocumentMigrator` (interface)

The per-step migration contract. All concrete migrators implement this.

```csharp
public interface IJsonDocumentMigrator
{
    string DocType    { get; }
    int    FromVersion { get; }
    int    ToVersion   { get; }
    void Apply(JsonObject root, MigrationContext ctx);
}
```

- `Apply` mutates `root` in-place. Must be deterministic and idempotent.
- Never touch `$meta` fields inside `Apply` — the pipeline manages them.
- Use `ctx.WithItem(...)` to push JSONPath scope before entering nested structures.
- Add notes/warnings via `ctx.Report.AddNote` / `ctx.Report.AddWarning`.

---

### `MigrationRegistry` (sealed class)

Thread-safe registry of document type registrations and their version migration chains.
Sealed after `MigrationBootstrap.Build` returns; any further registration throws.

| Member | Description |
|---|---|
| `RegisterDocType(docType, currentVersion, migrators)` | Registers a doc type with a full migrator chain. Validates adjacency, coverage, doc-type consistency. |
| `RegisterPassthroughDocType(docType, currentVersion)` | Registers a doc type that needs no migration (stable schema). |
| `Seal()` | Prevents further registration; called by `MigrationBootstrap`. |
| `GetCurrentVersion(docType)` | Returns the highest version registered. |
| `IsPassthrough(docType)` | Returns true for passthrough registrations. |
| `GetPath(docType, from, to)` | Returns the ordered list of migrators for a from->to chain. Throws `MigrationException` if the path is incomplete. |

Registration validates that:
- Every version step from 1 to `currentVersion - 1` has both an up and a down migrator.
- Each migrator's `DocType` matches the registered doc type.
- No duplicate `(from, to)` pairs are registered.

---

### `MigrationPipeline` (sealed class)

Orchestrates migration of a `JsonObject` through the registry's migrator chain.

| Member | Description |
|---|---|
| `MigrationPipeline(MigrationRegistry)` | Constructor. |
| `MigrationReport MigrateToCurrent(JsonObject root, string? sourcePath)` | Migrates to the current registered version. |
| `MigrationReport MigrateTo(JsonObject root, int targetVersion, string? sourcePath)` | Migrates to a specific version (up or down). |
| `int GetCurrentVersion(string docType)` | Delegates to registry. |

For each migrator in the chain the pipeline:
1. Calls `migrator.Apply(root, ctx)`.
2. Reads the updated `$meta` envelope.
3. Validates that `schemaVersion` was updated to the expected `ToVersion`.
4. Throws `MigrationException` if the migrator left the version unchanged.

---

### `MigrationContext` (sealed class)

Carries shared state for a single migration run. Passed to every `IJsonDocumentMigrator.Apply`
call. Tracks the JSONPath scope stack for accurate warning paths.

| Member | Description |
|---|---|
| `string? SourcePath` | File that was loaded, or null for in-memory migration. |
| `MigrationReport Report` | Accumulated report; migrators call `AddNote` / `AddWarning`. |
| `IDisposable WithItem(string key)` | Pushes a string scope segment (property name). |
| `IDisposable WithIndex(int i)` | Pushes an integer scope segment (array index). |

---

### `MigrationReport` (sealed class)

Structured summary of what a single migration run accomplished.

| Member | Description |
|---|---|
| `string DocType` | The document type migrated. |
| `int FromVersion` | Schema version before migration. |
| `int ToVersion` | Schema version after migration. |
| `MigrationDirection Direction` | `Up` or `Down`. |
| `TimeSpan Duration` | Total wall-clock time for the chain. |
| `IReadOnlyList<string> Notes` | Free-form notes added by migrators. |
| `IReadOnlyList<MigrationWarning> Warnings` | Non-fatal warnings with `Path` and `Message`. |

---

### `MigrationException` (sealed class)

Extends `InvalidOperationException`. All migration failures throw this type. Migration errors
are fail-loud — no silent fallthrough. Thrown for: missing `$meta`, unregistered doc type,
missing migrator step, migrator leaving version unchanged, storage I/O failures.

---

### `ReadOnlyMigrationAdapter` (sealed class)

Fast-path adapter for cluster nodes and diagnostic tools. Never writes sidecar files.

```
FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyMigrationAdapter.cs
```

| Member | Description |
|---|---|
| `ReadOnlyMigrationAdapter(MigrationPipeline)` | Constructor. |
| `Task<ReadOnlyLoadOutcome> LoadAndMigrateAsync(string path, CancellationToken)` | Load from file path. |
| `Task<ReadOnlyLoadOutcome> LoadAndMigrateAsync(Stream, string sourceId, CancellationToken)` | Load from stream. |

**Fast path**: if `diskVersion == currentVersion`, returns raw UTF-8 bytes as a string without
DOM allocation.

**Slow path**: parses the DOM, calls `MigrationPipeline.MigrateToCurrent`, returns the
migrated `JsonObject`.

#### `ReadOnlyLoadOutcome`

| Member | Description |
|---|---|
| `JsonObject Dom` | The migrated DOM (always non-null). |
| `DocumentMeta Meta` | The `$meta` as it now stands (current version). |
| `bool WasMigrated` | True if migration was performed. |
| `MigrationReport? Report` | Migration report, or null if no migration was needed. |

---

### `PersistentMigrationAdapter` (sealed class)

Editor- and CLI-facing adapter. Writes pre-migration snapshots, computes unknowns journals
for down-migrations, and applies journals on save-back.

```
FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs
```

Constructed exclusively via `MigrationBootstrap.Build/BuildForProduction`.

| Member | Description |
|---|---|
| `Task<MigrationLoadResult> LoadAndMigrateAsync(string path, CancellationToken)` | Load, migrate, write snapshot/journal sidecars as needed. |
| `Task SaveAsync(string path, MigrationLoadResult, CancellationToken)` | Serialize the (user-edited) DOM, apply journal if present, write atomically. |

**Load cases**:
- **Fast path (at current version)**: parse DOM, return. No sidecar I/O.
- **Up-migration**: write snapshot sidecar, call pipeline, return migrated DOM.
- **Down-migration**: deep-clone pre-migration DOM, call pipeline, compute `UnknownsJournal`
  via `DomDiffer` + `DiffToJournalConverter`, write journal sidecar.
- **Degraded fallback**: if the down-migration chain is unavailable, load the best snapshot
  at or below the current version; set `IsDegraded = true` on the result.

**Save**: serializes DOM to indented JSON, applies journal operations to restore
higher-version-exclusive fields, atomically renames via temp file, updates `$meta.engineVersion`.

#### `MigrationLoadResult`

| Member | Description |
|---|---|
| `JsonObject Dom` | The DOM as callers should see it, migrated to current version. |
| `DocumentMeta OriginalMeta` | `$meta` as it existed on disk before any migration. |
| `DocumentMeta CurrentMeta` | `$meta` after migration. |
| `bool WasMigrated` | True if `OriginalMeta.SchemaVersion != CurrentMeta.SchemaVersion`. |
| `bool HasUnknownsJournal` | True if a down-migration produced a non-empty journal. |
| `bool IsDegraded` | True if degraded snapshot fallback was used. |
| `string? UsedSnapshotPath` | Path of the snapshot used in degraded fallback, if any. |
| `MigrationReport? Report` | Migration report, or null if no migration was performed. |

---

### `MigrationServices` (sealed record)

Bundle of all migration infrastructure components, constructed once per process.

```csharp
public sealed record MigrationServices(
    MigrationRegistry Registry,
    MigrationPipeline Pipeline,
    ReadOnlyMigrationAdapter ReadOnly,
    PersistentMigrationAdapter Persistent);
```

---

### `MigrationBootstrap` (static class)

Constructs the `MigrationServices` bundle.

```
FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs
```

| Member | Description |
|---|---|
| `MigrationServices BuildForProduction(Action<MigrationRegistry>, string writerIdentifier)` | Production factory. Uses `FileSystemMigrationStorage` and reads `AssemblyInformationalVersionAttribute` for engine version. |
| `internal MigrationServices Build(...)` | Full-control factory for tests (pass any `IMigrationStorage` and version provider). |

`BuildForProduction` auto-registers `"Fdp.MigrationJournal"` as a passthrough at version 1,
then calls `registerFormats`, then seals the registry. In production the engine version is
read from `typeof(EntityRepository).Assembly`'s `AssemblyInformationalVersionAttribute`.

---

### Sidecar Files

**Location**: `.migration-snapshots/` subdirectory alongside the source file.

**Snapshot** (written before up-migration):
```
{base}.v{N}.{hash16}.snapshot.json
```
A verbatim copy of the original pre-migration file bytes. Used as a fallback when
down-migration is unavailable in an older binary.

**Journal** (written after down-migration when there are non-empty operations):
```
{base}.v{N}.{hash16}.unknowns.json
```
A `Fdp.MigrationJournal` versioned JSON document. Contains a flat list of JSONPath-addressed
`Set`/`Remove` operations representing the higher-version-exclusive fields lost during
down-migration.

Example sidecar layout:
```
scenarios/urban-combat/
|-- scenario.json
|-- tkb-default.json
|-- roads-main.json
+-- .migration-snapshots/
    |-- scenario.v1.a3f8e2b4c1d09f55.snapshot.json
    +-- scenario.v2.d7e1a3f842b09c11.unknowns.json
```

#### `SidecarKind` (enum)

| Value | Filename suffix | Description |
|---|---|---|
| `Snapshot` | `.snapshot.json` | Pre-migration verbatim copy. |
| `Journal` | `.unknowns.json` | Down-migration unknowns journal. |

#### `SidecarFileInfo` (sealed record)

Parsed from the sidecar filename without I/O.

| Member | Description |
|---|---|
| `FileName` | The sidecar filename (no path). |
| `Kind` | `SidecarKind.Snapshot` or `Journal`. |
| `Version` | The schema version embedded in the filename. |
| `ContentHash` | The hex-16 SHA-256 content hash embedded in the filename. |

#### `SnapshotEntry`

Returned by `IMigrationStorage.FindBestSnapshotAsync`.

| Member | Description |
|---|---|
| `int Version` | Schema version of the snapshot. |
| `string Content` | The raw JSON text of the snapshot. |

---

### Storage Abstraction

`IMigrationStorage` (internal interface) abstracts sidecar I/O so tests can use
`InMemoryMigrationStorage` without touching the filesystem.

| Implementation | Usage |
|---|---|
| `FileSystemMigrationStorage` | Production: writes to `.migration-snapshots/` using atomic temp-and-rename. |
| `InMemoryMigrationStorage` | Tests: holds all content in a `Dictionary<string, string>`. |

The interface is internal because it references the internal `UnknownsJournal` type.
External callers use `MigrationBootstrap.BuildForProduction` which wires up
`FileSystemMigrationStorage` automatically.

---

### Internal: JSONPath Dialect

```
FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/
    JsonPath.cs, JsonPathParser.cs, JsonPathApplicator.cs
```

A minimal JSONPath dialect sufficient for migration journal operations:

- Property access: `$.entities.abc123.EntityInfo.Tags`
- Array index: `$.components[0].value`
- `JsonPathParser.Parse(string)` returns a `JsonPath` instance.
- `JsonPath.Read(JsonObject)` reads the value at the path (returns `null` if not present).
- `JsonPath.Apply(JsonObject, action)` mutates the value at the path.
- `JsonPathParser.Build(List<object>)` constructs a path string from a stack of string/int segments.

---

### Internal: DOM Differ

```
FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/
    DomDiffer.cs, DiffNode.cs, DiffObject.cs, DiffValue.cs
```

Structural diff of two `JsonObject` DOMs. Extracted from `Fdp.Toolkits.ReplayBrowser.Diff`
(JM-P1-007) so the migration core has no dependency on toolkits.

`DomDiffer.Diff(pre, post, compareArraysElementWise)` returns a `DiffNode` tree where only
modified nodes are marked. `DiffToJournalConverter.Convert(diffRoot, preMigrationDom)` walks
this tree and produces the flat `IReadOnlyList<JournalOperation>` for the unknowns journal.

`$meta` is always excluded from journal operations — the converter skips it when walking the
root-level diff tree.

---

## Test Coverage

Tests live in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/`.

| Test class | What it covers |
|---|---|
| `DocumentMetaTests` | Construction, validation, UTC coercion. |
| `JsonEnvelopeTests` | Peek (bytes, stream, file), Read, Write, EnsurePresent. |
| `MigrationRegistryTests` | Registration, passthrough, seal, path resolution, error cases. |
| `MigrationPipelineTests` | Up-chain, down-chain, passthrough, version-not-updated guard. |
| `MigrationContextTests` | Scope stack, report accumulation. |
| `ReadOnlyMigrationAdapterTests` | Fast path, up-migration, missing-file, bad-envelope. |
| `PersistentMigrationAdapterTests` | Fast path, up-migration (snapshot), down-migration (journal), degraded fallback, save-back journal restoration. |
| `UnknownsJournalTests` | Compute, serialize, deserialize, apply, user-deletion-wins rule. |
| `FileSystemMigrationStorageTests` | Snapshot write/read, journal write/find, hash verification. |
| `InMemoryMigrationStorageTests` | In-memory variants of the same operations. |
| `MigrationBootstrapTests` | End-to-end round-trip through a stub migrator pair. |
| `EndToEndSmokeTests` | Full pipeline with real fixture files. |

Run migration tests specifically:

```powershell
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"
```

---

## Dependencies

### Project References

`Fdp.Core.Serialization.Migrations` lives inside `Fdp.Core` which has no `<ProjectReference>`
elements. It depends only on framework and NuGet packages available to `Fdp.Core`:

| Dependency | Type | Purpose |
|---|---|---|
| `System.Text.Json` | Framework | `Utf8JsonReader`, `JsonObject` DOM, `JsonSerializer` |
| `FdpLog<T>` | Internal (Fdp.Core) | NLog-backed logging facade (`FDP/Engine/Fdp.Core/Logging/FdpLog.cs`) |
| `FdpDocumentTypes` | Internal (Fdp.Core) | FDP-owned doc type constants |
| `EntityRepository` | Internal (Fdp.Core) | Assembly anchor for `AssemblyInformationalVersionAttribute` resolution |

Application-layer format modules (e.g. `Hrot.Common.Scenario.Migrations`) reference
`Fdp.Core` and call into this namespace; the reverse dependency does not exist.

---

## Usage Examples

### Example 1 -- Bootstrap (production host)

```csharp
// In the host's composition root (e.g. NodeBootstrapper).
var migrations = MigrationBootstrap.BuildForProduction(
    registerFormats: reg =>
    {
        ScenarioMigrationModule.RegisterAll(reg);
        TkbMigrationModule.RegisterAll(reg);
        RoadNetworkMigrationModule.RegisterAll(reg);
    },
    writerIdentifier: "Hrot.SimHost");

// migrations.ReadOnly   -- for cluster PrepareAsync loads
// migrations.Persistent -- for editor / CLI save paths
```

### Example 2 -- Cluster load (read-only adapter)

```csharp
// Inside HrotScenarioLoadHandler.PrepareAsync:
var outcome = await _migrations.ReadOnly.LoadAndMigrateAsync(scenarioPath, ct);
var scenario = JsonSerializer.Deserialize<ScenarioDto>(outcome.Dom, _options);

if (outcome.WasMigrated)
    FdpLog<HrotScenarioLoadHandler>.Info(
        "Scenario migrated from v{0} to v{1}",
        outcome.Report!.FromVersion, outcome.Report.ToVersion);
```

### Example 3 -- Editor load (persistent adapter)

```csharp
// Inside ScenarioFileService.LoadScenario:
var result = await _migrations.Persistent.LoadAndMigrateAsync(path, ct);
_alertManager.OnScenarioLoaded(result);   // queues modal if WasMigrated

var scenario = JsonSerializer.Deserialize<ScenarioDto>(result.Dom, _options);
_currentLoadResult = result;              // kept for the subsequent SaveScenario call
```

### Example 4 -- Editor save (persistent adapter)

```csharp
// Inside ScenarioFileService.SaveScenario:
// dom has been mutated by the editor UI
JsonEnvelope.Write(dom, new DocumentMeta(
    docType:       HrotDocumentTypes.Scenario,
    schemaVersion: ScenarioMigrationModule.CurrentVersion),
    _engineVersionProvider,
    "Hrot.Editor");

await _migrations.Persistent.SaveAsync(path, _currentLoadResult with { Dom = dom }, ct);
```

### Example 5 -- Authoring a migrator pair

```csharp
// Up-migrator (v1 -> v2): adds the Tags field to EntityInfo.
internal sealed class V1ToV2_EntityInfo_AddTags : IJsonDocumentMigrator
{
    public string DocType    => HrotDocumentTypes.Scenario;
    public int FromVersion   => 1;
    public int ToVersion     => 2;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        int count = 0;
        using (ctx.WithItem("entities"))
        {
            EntityPatch.OnEachEntity(root, (id, entity) =>
            {
                using var _ = ctx.WithItem(id);
                if (entity["EntityInfo"] is not JsonObject info) return;
                if (info.ContainsKey("Tags")) return;   // idempotent
                info["Tags"] = new JsonArray();
                count++;
            });
        }
        ctx.Report.AddNote($"Added empty Tags array to EntityInfo on {count} entities.");
        FdpLog<V1ToV2_EntityInfo_AddTags>.Info("v1->v2: Tags added on {0} entities", count);
    }
}

// Down-migrator (v2 -> v1): removes Tags (lossy).
internal sealed class V2ToV1_EntityInfo_RemoveTags : IJsonDocumentMigrator
{
    public string DocType    => HrotDocumentTypes.Scenario;
    public int FromVersion   => 2;
    public int ToVersion     => 1;

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        using (ctx.WithItem("entities"))
        {
            EntityPatch.OnEachEntity(root, (id, entity) =>
            {
                using var _ = ctx.WithItem(id);
                if (entity["EntityInfo"] is not JsonObject info) return;
                info.Remove("Tags");
            });
        }
        ctx.Report.AddNote("Removed Tags field from EntityInfo (lossy down-migration).");
    }
}
```

### Example 6 -- Registering a migrator pair

```csharp
// Inside ScenarioMigrationModule.RegisterAll:
registry.RegisterDocType(
    HrotDocumentTypes.Scenario,
    currentVersion: 2,
    migrators: new IJsonDocumentMigrator[]
    {
        new V1ToV2_EntityInfo_AddTags(),
        new V2ToV1_EntityInfo_RemoveTags(),
    });
```

### Example 7 -- Passthrough registration

```csharp
// Inside PassthroughFormatsModule.RegisterAll:
registry.RegisterPassthroughDocType(HrotDocumentTypes.StructEdit,         currentVersion: 1);
registry.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext, currentVersion: 2);
```

---

## Migration Authoring Guidelines

1. **Operate on `JsonObject` DOM only.** No typed DTO casts. No `System.Text.Json.Serialization`.
2. **Idempotent.** If the field to add already exists, skip silently.
3. **Deterministic.** No wall-clock, no env vars, no unseeded random in migrator logic.
4. **Scope discipline.** Push `ctx.WithItem(...)` before entering nested structures.
5. **Never touch `$meta`.** The pipeline manages it; migrators that touch `$meta` will cause
   the post-migrator version check to fail.
6. **One log line per migrator run.** Use `FdpLog<T>.Info` with index-based templates.
7. **Atomic per-entity changes.** All changes to a single entity complete before moving on.
8. **Paired up/down.** Every migrator must have its partner in the same PR with paired test fixtures.

---

## Sidecar Directory Convention Summary

```
<document-dir>/
|-- my-scenario.json                          (current file)
+-- .migration-snapshots/
    |-- my-scenario.v1.a3f8e2b4c1d09f55.snapshot.json   (verbatim pre-up-migration backup)
    +-- my-scenario.v2.d7e1a3f842b09c11.unknowns.json   (journal from a down-migration)
```

The hash in the filename is the first 16 hex chars of the SHA-256 of the pre-migration
file content. It is verified on read; a mismatch throws `MigrationException`.
