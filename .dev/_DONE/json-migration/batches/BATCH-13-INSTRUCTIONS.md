# BATCH-13 Instructions

**Task:** JM-P2-008 — Patch passthrough writers (Orchestrator, MapInteractionConfig, NodeConfiguration, StructEdit)
**Goal:** Stamp `$meta` envelope on four remaining write paths; update their read paths accordingly.

**Reference files (read before coding):**
- Task definition: `.dev/json-migration/TASK-DETAILS.md` (section JM-P2-008)
- Integration map: `.dev/json-migration/05-integration-patches.md` (sections for GlobalContextClusterOpHandler, NedExConEgressWriters, NodeConfiguration.LoadFrom, EditDocumentJsonSerializer)
- Debt tracker: `.dev/json-migration/DEBT-TRACKER.md`
- `AGENTS.md` (editing invariants — mandatory)

---

## Codebase-fit constraints (non-negotiable)

- **C-7**: xUnit only — EXCEPT `StructEdit.Tests` which already uses FluentAssertions; follow the existing style there.
- **TreatWarningsAsErrors=true** on all modified projects.
- **D-020**: `NodeConfiguration.LoadFrom` swallows all exceptions by design. The Phase 2 change MUST preserve this behavior (catch `MigrationException` in the same `catch (Exception)` block).
- Do NOT change any test that is not directly affected by a production change in this batch.
- Preserve all existing comments unless factually wrong.

---

## Summary of all four changes

| Target | Namespace | Write change | Read change | SchemaVersion |
|--------|-----------|-------------|-------------|---------------|
| `GlobalContextClusterOpHandler` | `Hrot.Orchestrator` | `JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.OrchestratorContext, 2))` after DOM serialization | Add optional `ReadOnlyMigrationAdapter?` constructor param; bridge with `.GetAwaiter().GetResult()` | Strip `SchemaVersion = 2` from `GlobalContextDto` (C-4) |
| `NedExConEgressWriters.WriteMapConfig` | `Hrot.Network.NED.ExCon` | `JsonNode.Parse(config.ConfigJson)!.AsObject()` → `JsonEnvelope.Write(dom, ...)` → `dom.ToJsonString()` | No read path change | Remove `JsonSchemaVersion = MapConfigSchemaVersion` from DDS struct init |
| `NodeConfiguration.LoadFrom` | `Hrot.SimHost` | No write path | Add optional `ReadOnlyMigrationAdapter?` parameter; bridge with `.GetAwaiter().GetResult()`; keep exception swallowing | No `SchemaVersion` field in `NodeConfiguration` |
| `EditDocumentJsonSerializer.Serialize/Deserialize` | `StructEdit.Json` | Replace `structedit_version: "1.0"` with inline `$meta` object | Accept `$meta` presence as alternative to `structedit_version` check | Remove `SchemaVersion = "1.0"` string field |

---

## 1. GlobalContextClusterOpHandler — `Hrot.Orchestrator`

### 1a. Remove `SchemaVersion` from `GlobalContextDto`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs`

`GlobalContextDto` is defined at the end of this file. Remove the `SchemaVersion` property entirely:

```csharp
// REMOVE these lines from GlobalContextDto:
/// <summary>Schema version for forward-compatibility guards.</summary>
[JsonPropertyName("schemaVersion")]
public int SchemaVersion { get; set; } = 2;
```

### 1b. Patch `CommitSerializeLocal`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs`

Add these using directives at the top of the file (after existing `using` lines):
```csharp
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
```

Replace the serialization block in `CommitSerializeLocal` (currently creates `dto`, serializes and writes):

Before (current code):
```csharp
var dto = new GlobalContextDto
{
    StartWallTicks        = _pendingSaveWallTicks,
    SceneId               = _pendingSaveSceneId ?? string.Empty,
    ScenarioId            = _scenarioId,
    ScenarioTimeSeconds   = _pendingSaveScenarioTimeSeconds,
    SchemaVersion         = 2,
};

var json = JsonSerializer.Serialize(dto,
    new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(_pendingFilePath, json);
```

After:
```csharp
var dto = new GlobalContextDto
{
    StartWallTicks        = _pendingSaveWallTicks,
    SceneId               = _pendingSaveSceneId ?? string.Empty,
    ScenarioId            = _scenarioId,
    ScenarioTimeSeconds   = _pendingSaveScenarioTimeSeconds,
};

var serializeOpts = new JsonSerializerOptions { WriteIndented = true };
var dom = JsonSerializer.SerializeToNode(dto, serializeOpts)!.AsObject();
JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.OrchestratorContext, 2));
File.WriteAllText(_pendingFilePath, dom.ToJsonString(serializeOpts));
```

### 1c. Add optional `ReadOnlyMigrationAdapter` to constructor

Add a `ReadOnlyMigrationAdapter? _readOnlyAdapter` field and inject it:

Add field:
```csharp
private readonly ReadOnlyMigrationAdapter? _readOnlyAdapter;
```

Add `Fdp.Core.Serialization.Migrations.Adapters` using (or use full namespace inline).

Modify the public constructor to accept an optional adapter:
```csharp
public GlobalContextClusterOpHandler(DdsParticipant participant, string scenarioId, ReadOnlyMigrationAdapter? readOnlyAdapter = null)
{
    _contextWriter  = new DdsWriter<OrchestratorContextTopic>(participant);
    _scenarioId     = scenarioId ?? string.Empty;
    _readOnlyAdapter = readOnlyAdapter;
}
```

Also update the internal test constructor:
```csharp
internal GlobalContextClusterOpHandler(DdsWriter<OrchestratorContextTopic> contextWriter, string scenarioId, ReadOnlyMigrationAdapter? readOnlyAdapter = null)
{
    _contextWriter  = contextWriter;
    _scenarioId     = scenarioId ?? string.Empty;
    _readOnlyAdapter = readOnlyAdapter;
}
```

### 1d. Patch `CommitLoad` to use adapter when present

In `CommitLoad`, replace the read block:

Before:
```csharp
var json = File.ReadAllText(filePath);
var dto  = JsonSerializer.Deserialize<GlobalContextDto>(json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

After:
```csharp
string json;
if (_readOnlyAdapter != null)
{
    var outcome = _readOnlyAdapter.LoadAndMigrateAsync(filePath, CancellationToken.None).GetAwaiter().GetResult();
    json = outcome.AsJsonString();
}
else
{
    json = File.ReadAllText(filePath);
}
var dto  = JsonSerializer.Deserialize<GlobalContextDto>(json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

### 1e. Update `ClusterMasterContextHandlerTests.cs`

**File:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterContextHandlerTests.cs`

`SetupScenarioFiles` creates a `GlobalContextDto` with `SchemaVersion = 2` which will no longer compile after removing the property. Remove that line:

Before:
```csharp
var ctxDto = new GlobalContextDto
{
    StartWallTicks      = wallTicks,
    SceneId             = "scene_" + scenarioId,
    ScenarioId          = scenarioId,
    ScenarioTimeSeconds = simTime,
    SchemaVersion       = 2,
};
```

After:
```csharp
var ctxDto = new GlobalContextDto
{
    StartWallTicks      = wallTicks,
    SceneId             = "scene_" + scenarioId,
    ScenarioId          = scenarioId,
    ScenarioTimeSeconds = simTime,
};
```

### 1f. Add new test for `CommitSerializeLocal`

**File:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterContextHandlerTests.cs`

Add a new xUnit `[Fact]` test:

```
Name: CommitSerializeLocal_ProducesPhase2Envelope
Description:
  Build a GlobalContextClusterOpHandler (using the internal test constructor with a no-op
  DdsWriter<OrchestratorContextTopic>). Set LocalTempRoot = _tempDir. Set ScenarioTimeSeconds = 42.0.

  Call PrepareAsync(cmd, ct) with a NodeOpType.SerializeLocal command. Then call Commit(cmd, null).

  After Commit, read the written Orchestrator.json. Parse as JsonDocument. Assert:
  - root["$meta"] is present (not null/undefined)
  - root["$meta"]["docType"].GetString() == "Hrot.OrchestratorContext"
  - root["$meta"]["schemaVersion"].GetInt32() == 2
  - root.TryGetProperty("schemaVersion", out _) is FALSE (naked schemaVersion not present)
  - root["startWallTicks"] is present (the payload data was written)
```

For the test constructor, use a DDS participant instance (DDS is available in `Hrot.Orchestrator.Tests`):
```csharp
using var participant = new DdsParticipant(15);
var handler = new GlobalContextClusterOpHandler(participant, "test-scenario");
handler.LocalTempRoot = _tempDir;
handler.ScenarioTimeSeconds = 42.0;
var cmd = new NodeOpCommand { Operation = NodeOpType.SerializeLocal };
await handler.PrepareAsync(cmd, CancellationToken.None);
handler.Commit(cmd, null);
```

Then find the written file (check `handler.CommitManifestEntry.SourceUnc`) and read/parse it.

Required using directives:
```csharp
using System.Text.Json;
using System.Threading;
// NodeOpCommand should already be imported
```

---

## 2. NedExConEgressWriters — `Hrot.Network.NED.ExCon`

### 2a. Patch `WriteMapConfig`

**File:** `Hrot/Network/Hrot.Network.NED/ExCon/NedExConEgressWriters.cs`

Add using directives:
```csharp
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
```

Replace `WriteMapConfig` body:

Before:
```csharp
public void WriteMapConfig(MapConfigDto config)
{
    _configWriter.Write(new MapInteractionConfig
    {
        MapGroupId        = _mapGroupId,
        MapId             = 0,
        ActiveContextId   = config.ActiveContextId,
        JsonSchemaVersion = MapConfigSchemaVersion,
        ConfigurationJson = config.ConfigJson,
    });
}
```

After:
```csharp
public void WriteMapConfig(MapConfigDto config)
{
    var dom = JsonNode.Parse(config.ConfigJson)!.AsObject();
    JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.MapInteractionConfig, 1));
    _configWriter.Write(new MapInteractionConfig
    {
        MapGroupId        = _mapGroupId,
        MapId             = 0,
        ActiveContextId   = config.ActiveContextId,
        ConfigurationJson = dom.ToJsonString(),
    });
}
```

The `MapConfigSchemaVersion` field constant may now be unused — check if it's used elsewhere in the file. If it is ONLY used in `WriteMapConfig`, remove the private constant declaration `private const int MapConfigSchemaVersion = 1;` to avoid an `error CS0219` (unused variable warning promoted to error). If it is used elsewhere, leave it.

**No new test** for `NedExConEgressWriters` — the DDS integration requires a full participant and there is no existing `Hrot.Network.NED.Tests` project. The production code change is minimal and safe.

---

## 3. NodeConfiguration.LoadFrom — `Hrot.SimHost`

### 3a. Patch `LoadFrom`

**File:** `Hrot/Subsystems/Hrot.SimHost/NodeConfiguration.cs`

Add using directives:
```csharp
using Fdp.Core.Serialization.Migrations.Adapters;
using System.Threading;
```

Change the `LoadFrom` signature to accept an optional adapter:
```csharp
public static NodeConfiguration LoadFrom(string filePath, ReadOnlyMigrationAdapter? migrationAdapter = null)
```

Expand the method body:

Before:
```csharp
public static NodeConfiguration LoadFrom(string filePath)
{
    if (!File.Exists(filePath))
        return new NodeConfiguration();

    try
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<NodeConfiguration>(json, _jsonOptions)
               ?? new NodeConfiguration();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"[NodeConfiguration] Failed to parse '{filePath}': {ex.Message} — using defaults.");
        return new NodeConfiguration();
    }
}
```

After:
```csharp
public static NodeConfiguration LoadFrom(string filePath, ReadOnlyMigrationAdapter? migrationAdapter = null)
{
    if (!File.Exists(filePath))
        return new NodeConfiguration();

    try
    {
        string json;
        if (migrationAdapter != null)
        {
            var outcome = migrationAdapter.LoadAndMigrateAsync(filePath, CancellationToken.None).GetAwaiter().GetResult();
            json = outcome.AsJsonString();
        }
        else
        {
            json = File.ReadAllText(filePath);
        }
        return JsonSerializer.Deserialize<NodeConfiguration>(json, _jsonOptions)
               ?? new NodeConfiguration();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"[NodeConfiguration] Failed to parse '{filePath}': {ex.Message} — using defaults.");
        return new NodeConfiguration();
    }
}
```

The `catch (Exception)` already catches `MigrationException` since it derives from `Exception`.
D-020 behavior is preserved. ✓

### 3b. Add tests

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/NodeConfigurationTests.cs`

Add two new `[Fact]` tests using xUnit only (no FluentAssertions).

**T05 — Phase 2 format loads via adapter**:
```
Name: NodeConfiguration_LoadFrom_Phase2Format_WithAdapter_LoadsCorrectly
Write a temp file containing Phase 2 JSON:
{
  "$meta": { "docType": "Hrot.NodeConfiguration", "schemaVersion": 1 },
  "DdsDomainId": 99,
  "SimulationRateHz": 30
}
Build:
  var registry = new MigrationRegistry();
  registry.RegisterPassthroughDocType("Hrot.NodeConfiguration", 1);
  var adapter = new ReadOnlyMigrationAdapter(new MigrationPipeline(registry));
Call NodeConfiguration.LoadFrom(tempPath, adapter).
Assert DdsDomainId == 99u and SimulationRateHz == 30.
Cleanup temp file.
```

Required using directives:
```csharp
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
```

**T06 — Adapter throws → defaults returned (D-020 preserved)**:
```
Name: NodeConfiguration_LoadFrom_WithAdapter_StillReturnsDefaults_WhenAdapterThrows
Write a LEGACY temp file (no $meta):
{ "DdsDomainId": 7 }
Build the same adapter with "Hrot.NodeConfiguration" passthrough.
Call NodeConfiguration.LoadFrom(tempPath, adapter).
Because the adapter calls JsonEnvelope.Peek which throws MigrationException on a file
without $meta, the catch block in LoadFrom must intercept it and return defaults.
Assert DdsDomainId == 42u (the default value).
Assert NO exception is thrown.
Cleanup temp file.
```

Verify the test logic is sound: `ReadOnlyMigrationAdapter.LoadAndMigrateAsync` → `ProcessBytes` → `JsonEnvelope.Peek` → throws `MigrationException` for no-`$meta` file → caught by `catch (Exception)` in `LoadFrom` → returns `new NodeConfiguration()`.

---

## 4. EditDocumentJsonSerializer — `StructEdit.Json`

### 4a. Patch `Serialize`

**File:** `FDP/ExtDeps/StructEdit/src/StructEdit.Json/EditDocumentJsonSerializer.cs`

Replace the `structedit_version` line in `Serialize`:

Before:
```csharp
writer.WriteStartObject();
writer.WriteString("structedit_version", SchemaVersion);
writer.WriteString("rootTypeName", document.RootComponentType.AssemblyQualifiedName);
```

After:
```csharp
writer.WriteStartObject();
writer.WriteStartObject("$meta");
writer.WriteString("docType", "Hrot.StructEdit");
writer.WriteNumber("schemaVersion", 1);
writer.WriteEndObject();
writer.WriteString("rootTypeName", document.RootComponentType.AssemblyQualifiedName);
```

The `structedit_version` field is no longer written. The `SchemaVersion` constant may now be
unused — it is still referenced in `Deserialize` below (in the legacy branch), so keep it.

### 4b. Patch `Deserialize`

**File:** `FDP/ExtDeps/StructEdit/src/StructEdit.Json/EditDocumentJsonSerializer.cs`

Replace the `structedit_version` validation block:

Before:
```csharp
// 1. Validate schema version
if (!root.TryGetProperty("structedit_version", out var versionEl)
    || versionEl.GetString() != SchemaVersion)
{
    var found = root.TryGetProperty("structedit_version", out var v) ? v.GetString() : "<missing>";
    throw new EditJsonMismatchException(
        "structedit_version",
        $"JSON schema version mismatch. Expected '{SchemaVersion}', found '{found}'.");
}
```

After:
```csharp
// 1. Validate schema version
// Phase 2 format: $meta envelope is present — skip structedit_version check.
// Legacy format: validate structedit_version == "1.0".
bool hasMetaEnvelope = root.TryGetProperty("$meta", out _);
if (!hasMetaEnvelope)
{
    if (!root.TryGetProperty("structedit_version", out var versionEl)
        || versionEl.GetString() != SchemaVersion)
    {
        var found = root.TryGetProperty("structedit_version", out var v) ? v.GetString() : "<missing>";
        throw new EditJsonMismatchException(
            "structedit_version",
            $"JSON schema version mismatch. Expected '{SchemaVersion}', found '{found}'.");
    }
}
```

No new using directives needed (already has `using System.Text.Json`).

### 4c. Add tests to `StructEdit.Tests`

**File:** `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Json/JsonSerializationTests.cs`

This file uses FluentAssertions — follow the existing style.

Add three new `[Fact]` tests at the end of the `JsonSerializationTests` class:

**Serialize produces `$meta` envelope**:
```csharp
[Fact]
public void Serialize_ProducesMetaEnvelope()
{
    using var session = JsonTestHelper.Open(new ScalarComponent { Score = 5 });
    var json = session.ToJson();

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    root.TryGetProperty("$meta", out var meta).Should().BeTrue();
    meta.GetProperty("docType").GetString().Should().Be("Hrot.StructEdit");
    meta.GetProperty("schemaVersion").GetInt32().Should().Be(1);
}
```

**Serialize does NOT produce `structedit_version`**:
```csharp
[Fact]
public void Serialize_DoesNotProduceStructEditVersion()
{
    using var session = JsonTestHelper.Open(new ScalarComponent { Score = 5 });
    var json = session.ToJson();

    using var doc = JsonDocument.Parse(json);
    doc.RootElement.TryGetProperty("structedit_version", out _).Should().BeFalse();
}
```

**Deserialize accepts Phase 2 format (with `$meta`, without `structedit_version`)**:
```csharp
[Fact]
public void Deserialize_AcceptsPhase2Format()
{
    // Round-trip: serialize (produces $meta), then deserialize
    var original = new ScalarComponent { Score = 99 };
    using var writeSession = JsonTestHelper.Open(original);
    writeSession.Document.Root.Children
        .First(c => c.Name == "Score").Binding!.SetRawValue("99");
    var json = writeSession.ToJson();

    // Verify $meta is present and structedit_version is absent
    json.Should().Contain("\"$meta\"");
    json.Should().NotContain("structedit_version");

    // Deserialize into a fresh session
    var readTarget = new ScalarComponent { Score = 0 };
    using var readSession = JsonTestHelper.Open(readTarget);
    var act = () => readSession.FromJson(json);
    act.Should().NotThrow();
}
```

**Deserialize still accepts legacy format (with `structedit_version: "1.0"`, without `$meta`)**:
```csharp
[Fact]
public void Deserialize_AcceptsLegacyFormat()
{
    var target = new ScalarComponent { Score = 0 };
    using var session = JsonTestHelper.Open(target);
    // Build legacy JSON manually
    var legacyJson = """
        {
          "structedit_version": "1.0",
          "rootTypeName": null,
          "scope": "$",
          "nodes": []
        }
        """;
    // Replace rootTypeName with actual type
    legacyJson = legacyJson.Replace("null",
        $"\"{target.GetType().AssemblyQualifiedName}\"");

    var act = () => session.FromJson(legacyJson);
    act.Should().NotThrow<EditJsonMismatchException>();
}
```

NOTE: `session.ToJson()` / `session.FromJson()` are extension methods from `EditSessionJsonExtensions` — use them to call `Serialize`/`Deserialize` indirectly.

For the legacy format test, `nodes: []` is acceptable — `Deserialize` will do nothing if the array is empty (no `ProcessNodes` work). The important assertion is that no `EditJsonMismatchException` is thrown.

---

## Build verification

After all changes, run:
```
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5
```
Expected: no new `error CS` lines.

Then run targeted tests:
```
dotnet test "Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj" -c Debug --no-build --filter "CommitSerializeLocal" 2>&1 | Select-Object -Last 5
dotnet test "Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj" -c Debug --no-build --filter "NodeConfiguration" 2>&1 | Select-Object -Last 5
dotnet test "FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/StructEdit.Tests.csproj" -c Debug --no-build --filter "Serialize_ProducesMetaEnvelope|Deserialize_AcceptsPhase2|Deserialize_AcceptsLegacy|Serialize_DoesNotProduce" 2>&1 | Select-Object -Last 5
```

Also run the existing ClusterMaster context handler tests to confirm no regressions:
```
dotnet test "Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj" -c Debug --no-build --filter "ClusterMasterContextHandler" 2>&1 | Select-Object -Last 5
```

---

## Deliverable

Write a `BATCH-13-REPORT.md` to `.dev/json-migration/reports/` with:
- Summary of all file changes (list each file modified)
- Test results for each filter above (pass/fail counts)
- Any deviations from instructions (with justification)
- Any new debt items discovered
