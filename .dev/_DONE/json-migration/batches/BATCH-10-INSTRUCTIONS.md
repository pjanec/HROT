# BATCH-10 — Corrective Fixes (D-017/D-018) + Scenario Envelope Rollout (JM-P2-003)

**Batch Number:** BATCH-10
**Tasks:** D-017 (corrective), D-018 (corrective), JM-P2-003
**Phase:** Phase 2 — Envelope rollout
**Estimated Effort:** 10-14 hours
**Workspace root:** `d:\Work\IOS-IG-SimHost-FDP`

---

## 1. Onboarding

**Read these before starting:**

- `.dev/json-migration/TASK-DETAILS.md#jm-p2-003--patch-scenario-readwrite-paths` — full task spec
- `.dev/json-migration/05-integration-patches.md` — exact touchpoints for JM-P2-003
- `.dev/json-migration/reviews/BATCH-09-REVIEW.md` — what D-017 and D-018 need fixed
- `.dev/json-migration/Migration-system.md` §5.1 — Phase 2 goals

**Key Phase 2 constraint:** After all Phase 2 patches land, every JSON file has a `$meta`
envelope. `JsonEnvelope.Peek/Read` **throws** `MigrationException` when `$meta` is absent.
Unit tests must therefore create or reference JSON documents that already include `$meta`.

**Codebase-fit corrections that apply:**

| ID | Rule |
|----|------|
| C-3 | `ScenarioHeader.SchemaVersion` (int, Fdp.Toolkits) and `ScenarioHeaderDto.SchemaVersion` (string, Hrot.Core) are BOTH deleted. `$meta.schemaVersion` is canonical from Phase 2 onward. |
| C-7 | xUnit only, no FluentAssertions. Use `Assert.*` |

---

## 2. Corrective Task D-017 — Convert skeleton modules to static classes

**Priority:** P2 — must fix before JM-P2-009 bootstrap wiring.

**Files to change:**
- `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs`
- `Hrot/Engine/Hrot.Common/Scenario/Migrations/BlueprintMigrationModule.cs`
- `Hrot/Engine/Hrot.Common/Scenario/Migrations/TkbMigrationModule.cs`
- `Hrot/Engine/Hrot.Common/Scenario/Migrations/RoadNetworkMigrationModule.cs`

**Change:** Convert each from `public sealed class` with instance `RegisterAll` to
`public static class` with `public static void RegisterAll`. The design spec
(Migration-system.md §9.1) shows static classes so bootstrap callers can call
`ScenarioMigrationModule.RegisterAll(reg)` without instantiating.

**Test update:** `Hrot/Engine/Hrot.Common.Tests/Migrations/ModuleRegistrationTests.cs`
Tests T02–T05 currently use `new ScenarioMigrationModule()`. Update to static calls:
```csharp
// Before: var module = new ScenarioMigrationModule(); module.RegisterAll(reg)
// After:  ScenarioMigrationModule.RegisterAll(reg)
```

---

## 3. Corrective Task D-018 — Document null contract on ReadOnlyLoadOutcome.Report

**Priority:** P3.

**File to change:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs`

Add an XML doc comment to `Report` property clarifying that it is `null` on the fast
path (no migration occurred). The property signature stays unchanged; only the doc
comment changes. Example:
```csharp
/// <summary>
/// The migration report produced during load. <b>Null</b> when the document
/// was already at the current version (fast path) and no migration was applied.
/// Callers must null-check before accessing <see cref="MigrationReport.Warnings"/>.
/// </summary>
public MigrationReport? Report { get; }
```

---

## 4. Task JM-P2-003 — Patch scenario read/write paths

**Full spec:** `.dev/json-migration/TASK-DETAILS.md#jm-p2-003--patch-scenario-readwrite-paths`
**Integration-patches section:** `.dev/json-migration/05-integration-patches.md` — sections
for ScenarioFileService, ScenarioSerializer (Fdp.Toolkits), and HrotScenarioLoadHandler.

### 4.1 Read these files first

Before making any changes:
1. `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` — `Serialize` and `Deserialize`
2. `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioHeader.cs` — record type
3. `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs` — DTO type
4. `Hrot/Engine/Hrot.Core/Scenario/Map/HrotScenarioEnvelopeDto.cs` — envelope DTO
5. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs` — full file
6. `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` — full file
7. `Hrot/Engine/Hrot.Presentation.Tests/ScenarioFileServiceZoneTests.cs` — to understand test impact
8. `Hrot/Engine/Hrot.Presentation.Tests/ScenarioFileServiceTkbTests.cs` — to understand test impact
9. `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs` — `Build` method
10. `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs` — for module registration

### 4.2 Changes to ScenarioHeader.cs (Fdp.Toolkits)

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioHeader.cs`

Remove `SchemaVersion` parameter (C-3). The `SubsystemType` parameter stays — it is still
needed for `ScenarioSerializerBuilder` to store in `_subsystemType` (used internally for the
`SubsystemType` property). The `TkbName` parameter also stays.

```csharp
// Before:
public record ScenarioHeader(string SubsystemType, int SchemaVersion = 1, string? TkbName = null);

// After:
public record ScenarioHeader(string SubsystemType, string? TkbName = null);
```

Update all callers that pass `SchemaVersion:` to `new ScenarioHeader(...)`. Search for
`new ScenarioHeader(` and remove any `SchemaVersion:` argument. Key callers:
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/HierarchySerializationIntegrationTests.cs` (if any)
- Any other file that constructs `ScenarioHeader`

### 4.3 Changes to ScenarioHeaderDto.cs (Hrot.Core)

**File:** `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs`

Remove the `SchemaVersion` property (C-3). The `SubsystemType` and `TkbName` properties stay
(they are still used by `ScenarioFileService.ValidateSubsystemType` and `TkbLoadClusterStateHandler`).

```csharp
// Remove this property:
public string? SchemaVersion { get; set; }
```

Update all callers that set `SchemaVersion =` in `ScenarioHeaderDto`. Key callers:
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`

### 4.4 Changes to ScenarioSerializer.cs (Fdp.Toolkits) — Serialize path

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`

**Phase 2 contract:** The serializer writes `$meta` as the first property of the root DOM.
`Header.SubsystemType` and `Header.SchemaVersion` are removed from the output. `Header.TkbName`
stays (authoring field). The caller (`ScenarioFileService`) no longer creates a
`HrotScenarioEnvelopeDto` wrapper — the serializer's root DOM IS the document.

In the `Serialize` method, change the assembled root DOM:
```csharp
// ── Assemble root DOM ─────────────────────────────────────────
// Before:
var headerNode = new JsonObject
{
    ["SubsystemType"]  = JsonValue.Create(header.SubsystemType),
    ["SchemaVersion"]  = JsonValue.Create(header.SchemaVersion),
};
if (header.TkbName != null)
    headerNode["TkbName"] = JsonValue.Create(header.TkbName);

return new JsonObject
{
    ["Header"]   = headerNode,
    ["Entities"] = entitiesNode,
};

// After:
var root = new JsonObject
{
    ["Entities"] = entitiesNode,
};

// Write the TkbName authoring field into a Header block (C-3: retains authoring fields).
if (header.TkbName != null)
{
    root["Header"] = new JsonObject
    {
        ["TkbName"] = JsonValue.Create(header.TkbName),
    };
}

// Stamp $meta envelope (docType from subsystem type, version 1).
JsonEnvelope.Write(root, new DocumentMeta(header.SubsystemType, 1));

return root;
```

**Important:** `Fdp.Core.Serialization.Migrations` namespace must be imported in this file.
Add `using Fdp.Core.Serialization.Migrations;` at the top.

### 4.5 Changes to ScenarioSerializer.cs — Deserialize path

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`

**Phase 2 contract:** `$meta` is stripped / validated by the migration adapter BEFORE
`Deserialize` is called. When `$meta` is present (new format), skip the
`Header.SubsystemType` check (adapter already validated the doc type). When `$meta` is
absent (legacy format during test cutover), keep old behavior.

In the `Deserialize(EntityRepository repo, JsonObject dom, ...)` method, update the
subsystem-type filter:

```csharp
// Phase 2: If $meta is present, doc-type was already validated by the migration adapter.
// Skip the Header.SubsystemType filter. If $meta is absent (legacy), retain old filter.
if (!JsonEnvelope.HasEnvelope(dom))
{
    // Legacy path: peek Header.SubsystemType for backward compatibility.
    var headerNode = (dom["Header"] ?? dom["header"]) as JsonObject;
    var savedType  = headerNode?["SubsystemType"]?.GetValue<string>()
                  ?? headerNode?["subsystemType"]?.GetValue<string>();
    if (!string.Equals(savedType, _subsystemType, StringComparison.Ordinal))
        return; // Graceful subsystem mismatch for legacy files.
}
// Envelope present: adapter already validated $meta.docType; proceed directly.
```

The rest of `Deserialize` (looking for `Entities` node, creating entities) stays unchanged.

### 4.6 Changes to ScenarioFileService.cs (Hrot.Presentation)

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`

**Phase 2 contract:** `ScenarioFileService` receives an optional `MigrationServices`
parameter. When non-null, `SaveScenario` and `LoadScenario` route through the adapters.
When null (existing callers not yet updated in JM-P2-009), the old direct-file path is used
as fallback. This avoids breaking all existing call sites until JM-P2-009 wires the bootstrap.

**a) Constructor change:**
```csharp
// Add optional MigrationServices parameter:
public ScenarioFileService(
    ScenarioSerializer serializer,
    FdpEventBus? bus = null,
    IZoneManagerService? zoneService = null,
    ITkbDatabase? tkbDb = null,
    MigrationServices? migrationServices = null)  // new
{
    _serializer         = serializer  ?? throw new ArgumentNullException(nameof(serializer));
    _bus                = bus;
    _zoneService        = zoneService;
    _tkbDb              = tkbDb;
    _migrationServices  = migrationServices;       // new
}
```
Add field: `private readonly MigrationServices? _migrationServices;`

**b) SaveScenario change:**
```csharp
public void SaveScenario(EntityRepository repo, string filePath)
{
    if (repo == null)     throw new ArgumentNullException(nameof(repo));
    if (filePath == null) throw new ArgumentNullException(nameof(filePath));

    var header  = new ScenarioHeader("Hrot.Scenario", TkbName: _tkbDb?.ActiveTkbName);
    var fdpDom  = _serializer.Serialize(repo, header);
    // _serializer.Serialize now stamps $meta on fdpDom.

    var activeZones = _zoneService?.GetActiveZones();
    if (activeZones != null && activeZones.Count > 0)
        fdpDom["Zones"] = System.Text.Json.Nodes.JsonSerializer
            .SerializeToNode(activeZones, HrotSerializerOptions.HrotJsonOptions)!;

    if (_migrationServices != null)
    {
        // Phase 2 path: write via persistent adapter (atomic, updates $meta.engineVersion).
        _migrationServices.Persistent.SaveAsync(fdpDom, filePath)
            .GetAwaiter().GetResult();
    }
    else
    {
        // Legacy fallback (until JM-P2-009 wires MigrationServices everywhere).
        var minifiedOptions = new System.Text.Json.JsonSerializerOptions(
            HrotSerializerOptions.HrotJsonOptions) { WriteIndented = false };
        var minifiedJson = System.Text.Json.JsonSerializer.Serialize(fdpDom, minifiedOptions);
        File.WriteAllText(filePath, JsonAestheticFormatter.FlattenNumericArrays(minifiedJson));
    }
}
```

**Note on Zones serialization:** The old code used `HrotScenarioEnvelopeDto` which had a
`Zones` property. After Phase 2, zones are added directly to the FDP DOM as a top-level
`"Zones"` key. Check how `IZoneManagerService.GetActiveZones()` returns zones and serialize
them into the DOM accordingly. Read `ScenarioFileServiceZoneTests.cs` before implementing
to understand the expected format.

**c) LoadScenario change:**
```csharp
public void LoadScenario(EntityRepository repo, string filePath)
{
    if (repo == null)     throw new ArgumentNullException(nameof(repo));
    if (filePath == null) throw new ArgumentNullException(nameof(filePath));

    string jsonText;
    System.Text.Json.Nodes.JsonObject? dom = null;

    if (_migrationServices != null)
    {
        // Phase 2 path: route through persistent adapter.
        var outcome = _migrationServices.Persistent.LoadAndMigrateAsync(filePath)
            .GetAwaiter().GetResult();
        // Validate doc type explicitly (adapter may accept multiple formats if registry is broad).
        dom = outcome.Content;
        jsonText = dom.ToJsonString();
    }
    else
    {
        jsonText = File.ReadAllText(filePath);
        ValidateSubsystemType(jsonText);  // legacy validation
    }

    _worldResetObservers?.Invoke();
    repo.SoftClear();
    if (repo.HasSingletonUnmanaged<GlobalTime>())
        repo.SetSingletonUnmanaged(default(GlobalTime));
    _bus?.Publish(new WorldResetEvent());

    if (_zoneService != null)
    {
        dom ??= System.Text.Json.Nodes.JsonNode.Parse(jsonText)?.AsObject();
        var envelope = dom?.Deserialize<HrotScenarioEnvelopeDto>(HrotSerializerOptions.HrotJsonOptions);
        if (envelope?.Zones != null)
            _zoneService.LoadZones(repo, envelope.Zones);

        if (dom != null && (dom["Entities"] != null || dom["entities"] != null))
            _serializer.Deserialize(repo, dom);
    }
    else
    {
        if (dom != null)
            _serializer.Deserialize(repo, dom);
        else
            _serializer.Deserialize(repo, jsonText);
    }
}
```

**Important:** `MigrationServices.Persistent.LoadAndMigrateAsync` returns a
`MigrationLoadResult` (not `ReadOnlyLoadOutcome`). Check the actual return type of
`PersistentMigrationAdapter.LoadAndMigrateAsync` in
`FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs`
before implementing. The result has a `Content` property of type `JsonObject`.

Also note: `_migrationServices.Persistent` is `PersistentMigrationAdapter`. Read its API
carefully before calling. It may have a `LoadAndMigrateAsync(string path)` overload.

**d) Remove SchemaVersion from the DTO construction:**
The old `SaveScenario` built:
```csharp
Header = new ScenarioHeaderDto { SchemaVersion = "1.0", ... }
```
This is no longer used once you move to the DOM-based save path above. If `HrotScenarioEnvelopeDto`
is still used for zone deserialization in `LoadScenario`, keep it but never set `SchemaVersion`.

### 4.7 Changes to HrotScenarioLoadHandler.cs (Hrot.SimHost)

**File:** `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs`

**Phase 2 contract:** `PrepareAsync` adds an optional `ReadOnlyMigrationAdapter?` parameter
(via constructor injection). When non-null, the staged scenario file is loaded through the
read-only adapter before handing to the serializer.

**a) Constructor change:**
```csharp
// Add optional ReadOnlyMigrationAdapter parameter:
public HrotScenarioLoadHandler(
    ScenarioSerializer serializer,
    IScenarioLoader scenarioLoader,
    IZoneManagerService zoneService,
    IScenarioEntityExtractor extractor,
    ScenarioEntityCreationRequestSource source,
    INetworkIdAllocator idAllocator,
    EntityRepository? world = null,
    IRecordReplayController? controller = null,
    string storageDirectory = @"C:\FDP_Temp",
    ReadOnlyMigrationAdapter? readOnlyMigrationAdapter = null)  // new
```
Add field: `private readonly ReadOnlyMigrationAdapter? _readOnlyAdapter;`

**b) In the PrepareAsync load section:**
Find where the scenario JSON is loaded (look for `_scenarioLoader.LoadAsync` and `_serializer.Deserialize`).
When `_readOnlyAdapter != null`, after loading the raw JSON string, run it through the adapter:

```csharp
// After loading jsonText from _scenarioLoader:
if (_readOnlyAdapter != null)
{
    var utf8 = System.Text.Encoding.UTF8.GetBytes(jsonText);
    var outcome = _readOnlyAdapter.LoadAndMigrateAsync(
        new System.IO.MemoryStream(utf8), "staged-scenario.json")
        .GetAwaiter().GetResult();
    // outcome.Content is the migrated JsonObject
    dom = outcome.Content;
}
```

Read the actual `PrepareAsync` code carefully before implementing — the JSON loading happens
inside the `PrepareLive` branch. Read the full method before changing anything.

### 4.8 Test updates

**Goal:** all existing tests that test ScenarioFileService or ScenarioSerializer must still
pass after these changes. New tests should verify the migration-adapter code paths.

**a) Update ScenarioFileServiceZoneTests.cs:**
These tests check the JSON output of `SaveScenario`. After the change:
- The output will have `$meta` as the first field (written by `ScenarioSerializer`)
- The output will NOT have `Header.SchemaVersion`
- The output will still have `Header.TkbName` (if TKB is set) or no `Header` (if not)
- `Zones` will be a top-level JSON field (same position as before)

Update assertions that previously checked `envelope.Header.SchemaVersion == "1.0"` to instead
check for the `$meta` field.

**b) Update ScenarioFileServiceTkbTests.cs:**
Same pattern — remove assertions about `SchemaVersion`; check that `$meta.docType` and
`$meta.schemaVersion` are present.

**c) Tests using ScenarioHeader constructor:**
Search for `new ScenarioHeader(` — remove any `SchemaVersion:` argument.

**d) Add new Phase 2 tests in `Hrot.Common.Tests/Migrations/ScenarioPhase2Tests.cs`** (or append to `ModuleRegistrationTests.cs`):

- **JM-P2-003-T01** `ScenarioSerializer_Serialize_ProducesMetaEnvelope`
  Build a `ScenarioSerializer` via `HrotScenarioSerializerFactory.Build(null)`, create an
  empty `EntityRepository`, call `Serialize(repo, new ScenarioHeader("Hrot.Scenario"))`.
  Assert: `JsonEnvelope.HasEnvelope(dom)` is true; `$meta.docType == "Hrot.Scenario"`;
  `$meta.schemaVersion == 1`; `Header.SchemaVersion` is absent from dom.

- **JM-P2-003-T02** `ScenarioSerializer_Serialize_TkbName_AppearsInHeader`
  Call `Serialize(repo, new ScenarioHeader("Hrot.Scenario", TkbName: "TestTkb"))`.
  Assert: `dom["Header"]["TkbName"].GetValue<string>() == "TestTkb"`.

- **JM-P2-003-T03** `ScenarioSerializer_Deserialize_EnvelopePresent_SkipsSubsystemFilter`
  Create a `JsonObject` DOM with `$meta.docType = "Hrot.Scenario"`, `$meta.schemaVersion = 1`,
  and a valid `Entities` object. Call `Deserialize(repo, dom)`. Assert no exception is thrown
  and repo is populated (or empty, depending on content).

- **JM-P2-003-T04** `ScenarioSerializer_Deserialize_LegacyNoEnvelope_UsesSubsystemFilter`
  Create a `JsonObject` DOM WITHOUT `$meta`, with `Header.SubsystemType = "Hrot.CGF"`
  (mismatched against a serializer built for "Hrot.Scenario"). Call `Deserialize(repo, dom)`.
  Assert no entities are created (graceful skip — old behavior preserved for legacy files).

---

## 5. Important Implementation Notes

1. **`PersistentMigrationAdapter.LoadAndMigrateAsync`** — Read its actual API in
   `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs`
   before calling. The return type is likely `MigrationLoadResult` (not `ReadOnlyLoadOutcome`).
   The `MigrationLoadResult.Content` property holds the `JsonObject`.

2. **`PersistentMigrationAdapter.SaveAsync`** — Read its actual signature. It likely takes
   `(JsonObject dom, string filePath)`. Pass the DOM directly (it already has `$meta` written
   by `ScenarioSerializer.Serialize`).

3. **Zones in SaveScenario** — The current code serializes zones via `HrotScenarioEnvelopeDto`.
   After Phase 2, add zones directly to the FDP DOM before passing to `SaveAsync`. The
   `PersistentMigrationAdapter.SaveAsync` writes the DOM as-is to disk. The zones go into
   `fdpDom["Zones"]`.

4. **Zones in LoadScenario** — `HrotScenarioEnvelopeDto` deserialization is used to extract
   `Zones` from the DOM. After Phase 2, the DOM may have `$meta` as the first property.
   `HrotScenarioEnvelopeDto.Deserialize` should still work (it's flexible about property order).
   But verify this in the tests.

5. **`JsonEnvelope.Write` must be called before `PersistentMigrationAdapter.SaveAsync`** —
   `ScenarioSerializer.Serialize` now stamps `$meta` on the DOM. Do NOT call `JsonEnvelope.Write`
   again in `ScenarioFileService.SaveScenario` (double-stamp).

6. **Sync wrappers** — `PersistentMigrationAdapter` is async. Use `.GetAwaiter().GetResult()`
   for the synchronous `SaveScenario/LoadScenario` methods. This is acceptable for Phase 2;
   Phase 4 can make these truly async if needed.

7. **Namespace imports** — Add `using Fdp.Core.Serialization.Migrations;` to
   `ScenarioSerializer.cs` and `ScenarioFileService.cs`.

---

## 6. Test-Driven Task Progression

**Mandatory workflow:**

```
1. Read existing tests first — understand current assertions
2. Write the test that covers the new behavior (red)
3. Implement the change (green)
4. Update any existing tests that break due to the SchemaVersion removal
5. Run: dotnet test "Hrot/Engine/Hrot.Presentation.Tests/..." -c Debug
   dotnet test "Hrot/Engine/Hrot.Common.Tests/..." -c Debug
   dotnet test "FDP/Engine/Fdp.Core.Tests/..." -c Debug --no-build
6. Only proceed when all tests pass
```

---

## 7. Build Verification

Before writing the report:

```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug 2>&1 | Select-Object -Last 5
dotnet test "Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj" -c Debug 2>&1 | Select-Object -Last 5
dotnet test "FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj" -c Debug --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 3
```

---

## 8. Report Format

Write your report to `.dev/json-migration/reports/BATCH-10-REPORT.md`.

Structure:
```markdown
# BATCH-10 Report
**Status:** Complete | Partial | Blocked
**Tests:** X new passing | Y total

## Tasks Completed
- [ ] D-017: Skeleton modules converted to static classes
- [ ] D-018: ReadOnlyLoadOutcome.Report null contract documented
- [ ] JM-P2-003: Scenario patches complete

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Beyond the Spec

## JM-P2-003 Summary
<ScenarioSerializer, ScenarioFileService, HrotScenarioLoadHandler — what changed and why>

## Build / Test Results
## Files Created / Modified
```

---

## 9. Autonomous Guidance

- Do not stop for questions unless there is a breaking design conflict.
- If `PersistentMigrationAdapter.SaveAsync` has a different signature than assumed, adapt.
- If `Zones` serialization into the DOM is complex, look at how `HrotScenarioEnvelopeDto` is
  serialized by `System.Text.Json` to understand the JSON shape, then replicate it in the DOM.
- Your role is described in `.github/skills/developer/SKILL.md`.
