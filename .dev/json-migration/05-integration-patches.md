# Integration Patches — JSON Read/Write Touchpoints

**Phase:** Phase 2 — Envelope rollout
**Purpose:** Catalogue every JSON read/write touchpoint in the engine so that JM-P2-003 through JM-P2-008 developers have a precise before/after spec.
**Status:** Complete survey — gates all Phase 2 code patches

---

## Table of Touchpoints

| Touchpoint | Task | Adapter type | DocType constant |
|---|---|---|---|
| ScenarioFileService.LoadScenario/SaveScenario | JM-P2-003 | PersistentMigrationAdapter | HrotDocumentTypes.Scenario |
| ScenarioSerializer (Fdp.Toolkits) | JM-P2-003 | PersistentMigrationAdapter | HrotDocumentTypes.Scenario |
| HrotScenarioLoadHandler.PrepareAsync | JM-P2-003 | ReadOnlyMigrationAdapter | HrotDocumentTypes.Scenario |
| BlueprintJsonServices | JM-P2-004 | PersistentMigrationAdapter | HrotDocumentTypes.Blueprint |
| TkbLoadClusterStateHandler | JM-P2-005 | ReadOnlyMigrationAdapter | HrotDocumentTypes.TkbDefinition |
| RoadNetworkLoader.LoadFromJson | JM-P2-006 | ReadOnlyMigrationAdapter | FdpDocumentTypes.RoadNetwork |
| RecordingDumper/Program.cs | JM-P2-007 | ReadOnlyMigrationAdapter | FdpDocumentTypes.FlightRecorderMetadata |
| ReplayBrowserContext | JM-P2-007 | ReadOnlyMigrationAdapter | FdpDocumentTypes.FlightRecorderMetadata |
| TransientMasterBuilder | JM-P2-007 | JsonEnvelope.Write passthrough | FdpDocumentTypes.FlightRecorderMetadata |
| RecordingExportService | JM-P2-007 | JsonEnvelope.Write passthrough | FdpDocumentTypes.FlightRecorderMetadata |
| GlobalContextClusterOpHandler | JM-P2-008 | JsonEnvelope.Write passthrough | HrotDocumentTypes.OrchestratorContext (v2) |
| NedExConEgressWriters (MapInteractionConfig) | JM-P2-008 | JsonEnvelope.Write passthrough | HrotDocumentTypes.MapInteractionConfig (v1) |
| NodeConfiguration.LoadFrom | JM-P2-008 | JsonEnvelope.Write passthrough | HrotDocumentTypes.NodeConfiguration (v1) |
| EditDocumentJsonSerializer (StructEdit) | JM-P2-008 | JsonEnvelope.Write passthrough | HrotDocumentTypes.StructEdit (v1) |

---

## Detailed Touchpoints

---

### ScenarioFileService — JM-P2-003

**File(s):**
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs` (`ScenarioFileService`, `SaveScenario`/`LoadScenario`)

**Current JSON shape (summary):**

`SaveScenario` serializes into `HrotScenarioEnvelopeDto` which has a top-level `Header` object:
```json
{
  "Header": {
    "SubsystemType": "Hrot.Scenario",
    "SchemaVersion": "1.0",
    "TkbName": "..."
  },
  "Zones": { ... },
  "Entities": { ... }
}
```
`LoadScenario` peeks the file via `ValidateSubsystemType` which reads `Header.SubsystemType`. It also calls `_serializer.Deserialize` which reads the `Entities` sub-object.

**Target shape:**
```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1, ... },
  "Zones": { ... },
  "Entities": { ... }
}
```
`Header.SchemaVersion: string "1.0"` is removed (C-3). `SubsystemType` moves into `$meta.docType`. The `ValidateSubsystemType` helper is replaced by a call to `MigrationServices.Persistent.LoadAndMigrateAsync(path)` which peeks the envelope and validates the doc type.

**Adapter type:** PersistentMigrationAdapter (editor-side, full persistence with sidecar)

**DocType constant:** `HrotDocumentTypes.Scenario` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
var header = new ScenarioHeader("Hrot.Scenario", TkbName: _tkbDb?.ActiveTkbName);
var envelope = new HrotScenarioEnvelopeDto { Header = new ScenarioHeaderDto { SchemaVersion = "1.0", ... } };
File.WriteAllText(filePath, json);

// After:
var meta = new DocumentMeta("Hrot.Scenario", 1);
var dom = BuildDomWithoutHeader(...);
JsonEnvelope.Write(dom, meta);
await _migrationServices.Persistent.SaveAsync(dom, filePath, meta);
```

---

### ScenarioSerializer (Fdp.Toolkits) — JM-P2-003

**File(s):**
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializerBuilder.cs` (`ScenarioSerializerBuilder`, constructor)
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` (Serialize/Deserialize)

**Current JSON shape (summary):**

`ScenarioSerializerBuilder` uses a `_subsystemType` string (e.g. `"Hrot.CGF"`) which is stored in the `Header.SubsystemType` field of the produced JSON. `ScenarioSerializer.Deserialize` peeks the `Header.SubsystemType` and skips the file if it doesn't match.

The serializer writes:
```json
{
  "Header": {
    "SubsystemType": "Hrot.SimHost",
    ...
  },
  "Entities": { ... }
}
```

**Target shape:**
```json
{
  "$meta": { "docType": "Hrot.SimHost", "schemaVersion": 1 },
  "Entities": { ... }
}
```
The `Header` object is replaced by `$meta`. `Serialize` calls `JsonEnvelope.Write`. `Deserialize` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync` to strip `$meta` before handing the DOM to the ECS deserializer.

**Adapter type:** PersistentMigrationAdapter (editor); ReadOnlyMigrationAdapter (cluster nodes)

**DocType constant:** `HrotDocumentTypes.Scenario` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before (Serialize):
root["Header"] = new JsonObject { ["SubsystemType"] = "Hrot.SimHost", ... };

// After (Serialize):
JsonEnvelope.Write(root, new DocumentMeta("Hrot.Scenario", 1));
```

---

### HrotScenarioLoadHandler.PrepareAsync — JM-P2-003

**File(s):**
- `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` (`HrotScenarioLoadHandler.PrepareAsync`)

**Current JSON shape (summary):**

`PrepareAsync` receives the staged scenario file path (via `_scenarioLoader`) and calls `_serializer.Deserialize`. The serializer internally reads `Header.SubsystemType` and the `Entities` node from the JSON file.

No version check is done in `PrepareAsync` directly — the `ScenarioSerializer` is responsible for accepting or rejecting the file based on `SubsystemType`.

**Target shape:**

After Phase 2, the scenario file has a `$meta` envelope. `PrepareAsync` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync(path)` first to migrate the document if needed, then hands the migrated JSON to the (updated) `_serializer.Deserialize`.

**Adapter type:** ReadOnlyMigrationAdapter (cluster node — no sidecar writes)

**DocType constant:** `HrotDocumentTypes.Scenario` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
var entityRequests = _extractor.Extract(await _scenarioLoader.LoadAsync(ct));

// After:
var outcome = await _migrationServices.ReadOnly.LoadAndMigrateAsync(path, ct);
var entityRequests = _extractor.Extract(outcome.Content);
```

---

### BlueprintJsonServices — JM-P2-004

**File(s):**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs` (`BlueprintJsonServices.Serialize`/`Deserialize`)

**Current JSON shape (summary):**

`BlueprintJsonServices` uses `System.Text.Json.JsonSerializer` directly with custom options (IncludeFields, CaseInsensitive). No version field exists in the current JSON output:
```json
{
  "Id": "...",
  "Name": "...",
  "Nodes": [ ... ]
}
```
There is no `Header`, no `SchemaVersion`, and no `$meta`.

**Target shape:**
```json
{
  "$meta": { "docType": "Hrot.Blueprints", "schemaVersion": 1 },
  "Id": "...",
  "Name": "...",
  "Nodes": [ ... ]
}
```
`Serialize` wraps the serialized `BlueprintAsset` DOM in a `$meta` envelope via `JsonEnvelope.Write`. `Deserialize` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync` to strip `$meta` before handing to the JSON deserializer.

**Adapter type:** PersistentMigrationAdapter (editor); ReadOnlyMigrationAdapter (compiler/runtime)

**DocType constant:** `HrotDocumentTypes.Blueprint` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
return JsonSerializer.Serialize(asset, _options);

// After:
var dom = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();
JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, 1));
return dom.ToJsonString();
```

---

### TkbLoadClusterStateHandler — JM-P2-005

**File(s):**
- `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs` (`TkbLoadClusterStateHandler.PrepareAsync`, `ExtractTkbNameFromLocalScenario`)

**Current JSON shape (summary):**

`TkbLoadClusterStateHandler` reads the local staged scenario file to extract `TkbName` from the scenario `Header`:
```json
{
  "Header": {
    "TkbName": "Ned_v4"
  }
}
```
The private method `ExtractTkbNameFromLocalScenario` uses `JsonDocument.Parse` to peek only the `Header.TkbName` field without fully deserializing. TKB definition files loaded from `.zip` archives are deserialized by `TkbDeserializer.ParseAndRegister`, which operates on the internal TKB format (not a versioned JSON scenario file).

**Target shape:**

After Phase 2, `ExtractTkbNameFromLocalScenario` peeks `$meta` via `JsonEnvelope.Peek` (if possible), then reads the TkbName from the body. Alternatively, since TkbName is not in `$meta` but in the body, the peek reads both envelope and the body key. The TKB `.zip` format itself is not a JSON document and requires no change.

**Adapter type:** ReadOnlyMigrationAdapter (cluster node, read-only peek of staged file)

**DocType constant:** `HrotDocumentTypes.TkbDefinition` (version 1 — for TKB definition files stored as `.json`, distinct from scenario files)

**Call-site patch (pseudo-code):**
```csharp
// Before:
using var doc = JsonDocument.Parse(scenarioJson);
doc.RootElement.GetProperty("Header").TryGetProperty("TkbName", out ...);

// After (after envelope adoption in the staged scenario file):
// $meta is peeked first, then TkbName read from the body node:
using var doc = JsonDocument.Parse(scenarioJson);
// If $meta is present, TkbName is no longer in Header but in root body:
doc.RootElement.TryGetProperty("TkbName", out ...);
// OR: keep reading from body-level after migration strips Header.
```

---

### RoadNetworkLoader.LoadFromJson — JM-P2-006

**File(s):**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Road/RoadNetworkLoader.cs` (`RoadNetworkLoader.LoadFromJson`)

**Current JSON shape (summary):**

`LoadFromJson` reads a road network JSON file using `JsonSerializer.Deserialize<RoadNetworkJson>`. There is no version field in the current format:
```json
{
  "Metadata": { "GridCellSize": 5.0, "WorldBounds": { ... } },
  "Nodes": [ ... ],
  "Segments": [ ... ]
}
```
No `Header`, no `SchemaVersion`, no version at all.

**Target shape:**
```json
{
  "$meta": { "docType": "Fdp.RoadNetwork", "schemaVersion": 1 },
  "Metadata": { ... },
  "Nodes": [ ... ],
  "Segments": [ ... ]
}
```
`LoadFromJson` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync(path)` to migrate and strip `$meta`, then deserializes the resulting body.

**Adapter type:** ReadOnlyMigrationAdapter (read-only load path, no sidecar)

**DocType constant:** `FdpDocumentTypes.RoadNetwork` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
string jsonContent = File.ReadAllText(jsonPath);
var roadData = JsonSerializer.Deserialize<RoadNetworkJson>(jsonContent);

// After:
var outcome = await _migrationServices.ReadOnly.LoadAndMigrateAsync(jsonPath, ct);
var roadData = JsonSerializer.Deserialize<RoadNetworkJson>(outcome.Content, _options);
```

---

### RecordingDumper/Program.cs — JM-P2-007

**File(s):**
- `FDP/Tools/Fdp.Tools.RecordingDumper/Program.cs` (`Program.Execute`, dispatches to `RecordingExportService.ExportToJson`)

**Current JSON shape (summary):**

`RecordingDumper` calls `RecordingExportService.ExportToJson`. The export service writes a JSON file with a `Header` block written via `Utf8JsonWriter`:
```json
{
  "Header": {
    "Format": "Fdp.FlightRecorder",
    "Version": "1.0",
    ...
  },
  "Frames": [ ... ]
}
```
The `Program.cs` itself only dispatches options; the JSON shape is determined by `RecordingExportService`.

**Target shape:**
```json
{
  "$meta": { "docType": "Fdp.FlightRecorder.Metadata", "schemaVersion": 1, ... },
  "Frames": [ ... ]
}
```
`RecordingExportService` writes `$meta` via `JsonEnvelope.Write` (or a direct `Utf8JsonWriter` call that outputs the canonical envelope fields) and removes the legacy `Header` block.

**Adapter type:** JsonEnvelope.Write passthrough (write-only export, no read migration)

**DocType constant:** `FdpDocumentTypes.FlightRecorderMetadata` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before (RecordingExportService):
writer.WriteStartObject("Header");
writer.WriteString("Format", "Fdp.FlightRecorder");
writer.WriteString("Version", "1.0");
writer.WriteEndObject();

// After:
// Write $meta first using JsonEnvelope helper or inline:
var meta = new DocumentMeta(FdpDocumentTypes.FlightRecorderMetadata, 1);
JsonEnvelope.WriteToUtf8Writer(writer, meta);
// No "Header" block written.
```

---

### ReplayBrowserContext — JM-P2-007

**File(s):**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` (`ReplayBrowserContext.LoadRecording`)

**Current JSON shape (summary):**

`ReplayBrowserContext.LoadRecording` opens a `.fdp` binary recording file and seeks through frames using `PlaybackController`. There is no JSON read/write in `ReplayBrowserContext` itself — JSON is only produced by `RecordingExportService.ExportToJson`. `ReplayBrowserContext` is listed here because it is the host context for the replay subsystem that will eventually call the export service.

**Target shape:**

No direct JSON shape change in `ReplayBrowserContext`. The envelope change is applied at `RecordingExportService` (JM-P2-007 above). `ReplayBrowserContext` may receive a `MigrationServices` dependency in Phase 4 for reading migrated replay metadata.

**Adapter type:** N/A (no direct JSON read/write in this class; deferred to Phase 4)

**DocType constant:** `FdpDocumentTypes.FlightRecorderMetadata`

**Call-site patch (pseudo-code):** No change needed in this class for Phase 2.

---

### TransientMasterBuilder — JM-P2-007

**File(s):**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/TransientMasterBuilder.cs` (`TransientMasterBuilder.Build`)

**Current JSON shape (summary):**

`TransientMasterBuilder.Build` uses `ScenarioSerializer` to serialize/deserialize component data when merging federated node repositories. The `ScenarioSerializer` emits `Header.SubsystemType` in its output (before Phase 2 changes). The transient merge output is in-memory; the federation master JSON is not typically written to disk. If written, it would carry the legacy `Header` block.

**Target shape:**

After Phase 2, `ScenarioSerializer` wraps output in `$meta`. `TransientMasterBuilder` requires no direct changes beyond consuming the updated serializer. If the federation master is serialized to JSON:
```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1 },
  "Entities": { ... }
}
```

**Adapter type:** JsonEnvelope.Write passthrough (in-memory, write-only snapshot for federation)

**DocType constant:** `HrotDocumentTypes.Scenario` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// No changes needed in TransientMasterBuilder itself.
// The ScenarioSerializer update (JM-P2-003) automatically propagates here.
```

---

### RecordingExportService — JM-P2-007

**File(s):**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` (`RecordingExportService.ExportToJson`, `ExportChangelogToJson`)

**Current JSON shape (summary):**

`RecordingExportService.ExportToJson` streams an `.fdp` recording to JSON using `Utf8JsonWriter`. It writes a `Header` block:
```json
{
  "Header": {
    "Format": "FDP Recording Export",
    "Version": "1.0",
    "SourceFile": "...",
    "TotalFrames": 42,
    ...
  },
  "Frames": [ ... ]
}
```
The `Header` is written as the first object key, followed by `Frames`.

**Target shape:**
```json
{
  "$meta": {
    "docType": "Fdp.FlightRecorder.Metadata",
    "schemaVersion": 1,
    "createdBy": "Fdp.RecordingDumper",
    ...
  },
  "Frames": [ ... ]
}
```
The `Header` block is replaced by `$meta`. Existing non-`Header` fields (`SourceFile`, `TotalFrames`, etc.) move into the body or into `$meta` extension fields. `Utf8JsonWriter` writes the envelope fields first.

**Adapter type:** JsonEnvelope.Write passthrough (write-only streaming export, no migration read)

**DocType constant:** `FdpDocumentTypes.FlightRecorderMetadata` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
writer.WriteStartObject("Header");
writer.WriteString("Format", "FDP Recording Export");
writer.WriteString("Version", "1.0");
writer.WriteEndObject();

// After:
// Write $meta envelope inline (streaming; JsonEnvelope.Write requires JsonObject DOM):
writer.WriteStartObject("$meta");
writer.WriteString("docType", FdpDocumentTypes.FlightRecorderMetadata);
writer.WriteNumber("schemaVersion", 1);
writer.WriteEndObject();
// Remaining fields (SourceFile, TotalFrames) written directly to root object.
```

---

### GlobalContextClusterOpHandler — JM-P2-008

**File(s):**
- `Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs` (`CommitSerializeLocal`, `CommitLoad`)

**Current JSON shape (summary):**

`CommitSerializeLocal` writes `Orchestrator.json` by serializing `GlobalContextDto`:
```json
{
  "startWallTicks": 637000000000000000,
  "sceneId": "exercise-001",
  "scenarioId": "scenario-001",
  "scenarioTimeSeconds": 125.4,
  "schemaVersion": 2
}
```
The naked `schemaVersion: 2` field is already at version 2 (C-4). `CommitLoad` reads the file with `JsonSerializer.Deserialize<GlobalContextDto>`.

**Target shape:**
```json
{
  "$meta": { "docType": "Hrot.OrchestratorContext", "schemaVersion": 2 },
  "startWallTicks": 637000000000000000,
  "sceneId": "exercise-001",
  "scenarioId": "scenario-001",
  "scenarioTimeSeconds": 125.4
}
```
The naked `schemaVersion` field on `GlobalContextDto` is removed; it is promoted into `$meta`. `CommitLoad` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync` before deserializing the body.

**Adapter type:** JsonEnvelope.Write passthrough (write); ReadOnlyMigrationAdapter (read)

**DocType constant:** `HrotDocumentTypes.OrchestratorContext` (version **2** — per C-4)

**Call-site patch (pseudo-code):**
```csharp
// Before (write):
var dto = new GlobalContextDto { ..., SchemaVersion = 2 };
var json = JsonSerializer.Serialize(dto, opts);
File.WriteAllText(_pendingFilePath, json);

// After (write):
var dto = new GlobalContextDto { ... }; // SchemaVersion property removed from DTO
var dom = JsonSerializer.SerializeToNode(dto, opts)!.AsObject();
var meta = new DocumentMeta(HrotDocumentTypes.OrchestratorContext, 2);
JsonEnvelope.Write(dom, meta);
File.WriteAllText(_pendingFilePath, dom.ToJsonString(opts));

// Before (read):
var dto = JsonSerializer.Deserialize<GlobalContextDto>(json, opts);

// After (read):
var outcome = await _migrationServices.ReadOnly.LoadAndMigrateAsync(filePath, ct);
var dto = JsonSerializer.Deserialize<GlobalContextDto>(outcome.Content, opts);
```

---

### NedExConEgressWriters (MapInteractionConfig) — JM-P2-008

**File(s):**
- `Hrot/Network/Hrot.Network.NED/ExCon/NedExConEgressWriters.cs` (`WriteMapConfig`)

**Current JSON shape (summary):**

`WriteMapConfig` constructs a `MapInteractionConfig` DDS message which carries `JsonSchemaVersion = 1` (an `int` field on the DDS struct, not a JSON field) and `ConfigurationJson` (a JSON string payload embedded in the DDS message). The `ConfigurationJson` payload is opaque JSON without any versioning envelope:
```json
{ "ContextId": "...", "Tools": [ ... ] }
```
The `JsonSchemaVersion` is a DDS-level field, not a JSON envelope field.

**Target shape:**

`ConfigurationJson` payload wraps in `$meta`:
```json
{
  "$meta": { "docType": "Hrot.MapInteractionConfig", "schemaVersion": 1 },
  "ContextId": "...",
  "Tools": [ ... ]
}
```
`JsonSchemaVersion` on the DDS struct is replaced by the envelope-based version. IG-side readers must accept the `$meta` envelope.

**Adapter type:** JsonEnvelope.Write passthrough (DDS write-only for ExCon side)

**DocType constant:** `HrotDocumentTypes.MapInteractionConfig` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
_configWriter.Write(new MapInteractionConfig
{
    JsonSchemaVersion = MapConfigSchemaVersion,  // = 1
    ConfigurationJson = config.ConfigJson,
});

// After:
var dom = JsonNode.Parse(config.ConfigJson)!.AsObject();
var meta = new DocumentMeta(HrotDocumentTypes.MapInteractionConfig, 1);
JsonEnvelope.Write(dom, meta);
_configWriter.Write(new MapInteractionConfig
{
    // JsonSchemaVersion field stripped (or set to 0 / ignored)
    ConfigurationJson = dom.ToJsonString(),
});
```

---

### NodeConfiguration.LoadFrom — JM-P2-008

**File(s):**
- `Hrot/Subsystems/Hrot.SimHost/NodeConfiguration.cs` (`NodeConfiguration.LoadFrom`, `Parse`)

**Current JSON shape (summary):**

`NodeConfiguration.LoadFrom` reads `config.json` using `JsonSerializer.Deserialize<NodeConfiguration>`. There is no version field in the current format:
```json
{
  "DdsDomainId": 0,
  "SimulationRateHz": 60,
  "GeodeticOrigin": { "Latitude": 50.0, "Longitude": 14.0, "Altitude": 200.0 },
  "LocalTempRoot": "C:\\FDP_Temp"
}
```

**Target shape:**
```json
{
  "$meta": { "docType": "Hrot.NodeConfiguration", "schemaVersion": 1 },
  "DdsDomainId": 0,
  "SimulationRateHz": 60,
  "GeodeticOrigin": { "Latitude": 50.0, "Longitude": 14.0, "Altitude": 200.0 },
  "LocalTempRoot": "C:\\FDP_Temp"
}
```
`LoadFrom` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync(path)` to strip `$meta` before deserializing. A passthrough migration means version 1 documents are accepted without modification.

**Adapter type:** JsonEnvelope.Write passthrough (read-only, no sidecar)

**DocType constant:** `HrotDocumentTypes.NodeConfiguration` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before:
var json = File.ReadAllText(filePath);
return JsonSerializer.Deserialize<NodeConfiguration>(json, _jsonOptions) ?? new NodeConfiguration();

// After (actual implementation -- synchronous wrapper chosen, not async):
// A surrounding try-catch preserves the "never throws" contract.
var outcome = _migrationAdapter.LoadAndMigrateAsync(filePath, CancellationToken.None).GetAwaiter().GetResult();
return JsonSerializer.Deserialize<NodeConfiguration>(outcome.AsJsonString(), _jsonOptions) ?? new NodeConfiguration();
// Note: GetAwaiter().GetResult() is safe here; LoadFrom runs on a startup thread with no
// SynchronizationContext. See Key Finding 5.
```

---

### EditDocumentJsonSerializer (StructEdit) — JM-P2-008

**File(s):**
- `FDP/ExtDeps/StructEdit/src/StructEdit.Json/EditDocumentJsonSerializer.cs` (`EditDocumentJsonSerializer.Serialize`/`Deserialize`)
- `FDP/ExtDeps/StructEdit/src/StructEdit.Json/EditSessionJsonExtensions.cs` (`ToJson`/`FromJson`)

**Current JSON shape (summary):**

`Serialize` writes:
```json
{
  "structedit_version": "1.0",
  "rootTypeName": "My.NS.MyType, MyAssembly, ...",
  "scope": "$",
  "nodes": [ ... ]
}
```
`Deserialize` checks `versionEl.GetString() != "1.0"` and throws if mismatched. The `structedit_version` is a string `"1.0"`.

**Target shape:**
```json
{
  "$meta": { "docType": "Hrot.StructEdit", "schemaVersion": 1 },
  "rootTypeName": "...",
  "scope": "$",
  "nodes": [ ... ]
}
```
The `structedit_version: "1.0"` check is retired (C-3). `$meta.schemaVersion = 1` replaces it. `Serialize` writes `$meta` first (via `JsonEnvelope.Write` or inline). `Deserialize` calls `ReadOnlyMigrationAdapter.LoadAndMigrateAsync` to strip `$meta` before processing the `rootTypeName`/`nodes` keys.

**Adapter type:** JsonEnvelope.Write passthrough (stable schema; no migration chain needed)

**DocType constant:** `HrotDocumentTypes.StructEdit` (version 1)

**Call-site patch (pseudo-code):**
```csharp
// Before (Serialize):
writer.WriteString("structedit_version", SchemaVersion);  // "1.0"

// After (Serialize):
writer.WriteStartObject("$meta");
writer.WriteString("docType", "Hrot.StructEdit");
writer.WriteNumber("schemaVersion", 1);
writer.WriteEndObject();
// structedit_version no longer written

// Before (Deserialize):
if (versionEl.GetString() != SchemaVersion) throw ...

// After (Deserialize):
// $meta is stripped by adapter before reaching this code.
// No structedit_version field check needed.
```

---

## Editor UI Hooks (Phase 4 only — enumerated here for completeness)

### Warning Modal — JM-P4-001 (deferred)

**Location:** `ScenarioFileService.LoadScenario` — after the migrated load returns, if `outcome.Report.Warnings.Count > 0` or `outcome.MigratedFromVersion != outcome.CurrentVersion`, show a modal.

### Degraded-mode Banner — JM-P4-002 (deferred)

**Location:** Editor shell — subscribes to `MigrationWarningEvent` published during load and shows a banner with the warning text.

### Migration History Menu — JM-P4-003 (deferred)

**Location:** `ScenarioFileService` or editor toolbar — lists snapshot files from `.migration-snapshots/` adjacent to the loaded file.

---

## CLI Entrypoint (Phase 4 only — enumerated here for completeness)

### Hrot.ClusterRunner --mode migrate — JM-P4-004 (deferred)

**Location:** `Hrot/Runner/Hrot.ClusterRunner/` entry point — a new CLI verb `--mode migrate --path <file>` that calls `PersistentMigrationAdapter.MigrateInPlaceAsync(path)` and exits.

---

## Key Findings

1. **Blueprint has no existing version field** — the cleanest format to add `$meta` since there is no legacy header to strip.

2. **OrchestratorContext is already at version 2 on disk** — `GlobalContextDto.SchemaVersion = 2` is a naked root field, not inside `Header`. The passthrough registration at `currentVersion = 2` (C-4) is essential to avoid false-positive "needs migration" detection.

3. **StructEdit uses a string `"1.0"` equality check** — this check is intentionally brittle (throws on any version mismatch). Phase 2 retires it entirely. Existing StructEdit files without `$meta` cannot be loaded after Phase 2 without a one-time migration tool (or a backward-compat shim period).

4. **MapInteractionConfig versioning is DDS-level, not JSON-level** — `JsonSchemaVersion` is an `int` field on a DDS struct (`MapInteractionConfig`), not inside a JSON payload. The Phase 2 patch moves it into the `ConfigurationJson` envelope.

5. **RoadNetworkLoader and NodeConfiguration.LoadFrom are synchronous** -- Both use option (b): `.GetAwaiter().GetResult()` sync wrapper around `ReadOnlyMigrationAdapter.LoadAndMigrateAsync`. Option (a) (making them async) was considered but rejected because it would require cascading async changes through `ZoneManagerService`, `EditorZoneAuthoringSystem`, and `SimHostApp` entry points. The sync-wrapper approach is safe for these specific paths because they run during startup/editor setup on a thread that is not a UI thread and has no running `SynchronizationContext` that would deadlock. Future work: if these call sites move to async entry points, remove the `.GetAwaiter().GetResult()` calls.

6. **NodeConfiguration `LoadFrom` never throws** — it swallows all exceptions and returns defaults. The Phase 2 adapter call must be guarded accordingly to preserve this behavior.

7. **RecordingExportService uses streaming `Utf8JsonWriter`** — `JsonEnvelope.Write` requires a `JsonObject` DOM. The Phase 2 patch must either: (a) write `$meta` inline at the start of the streaming write, or (b) buffer the output into a DOM and then call `JsonEnvelope.Write`. Inline streaming is preferred to keep memory bounded.
