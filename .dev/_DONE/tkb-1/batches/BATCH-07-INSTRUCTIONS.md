# BATCH-07 Instructions — TKB Phase 8: ScenarioHeaderDto, Consensus Check, Save Pipeline

**Tasks in this batch:** TKB-016, TKB-018, TKB-021  
**Design reference:** `.dev/tkb-1/DESIGN.md` §8.1–8.3  
**Task specs:** `.dev/tkb-1/TASK-DETAIL.md` §TKB-016, §TKB-018, §TKB-021

---

## Context

This is the final batch. All prior TKB tasks (001–015, 019, 020, 022) are committed.

The codebase now has:
- `ITkbDatabase` with `ActiveTkbName` property (`TkbDatabase` concrete class)
- `TkbLoadClusterStateHandler` sets `ActiveTkbName` after loading
- `ITkbEntityTranslator` wired in SimHost composition root
- `ScenarioHeaderDto` currently has `SubsystemType` and `SchemaVersion` only

This batch adds the scenario persistence and orchestrator sanity gate.

---

## Task TKB-016 — Extend `ScenarioHeaderDto` with `TkbName`

**File:** `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs`

### Current code

```csharp
public sealed class ScenarioHeaderDto
{
    public string? SubsystemType { get; set; }
    public string? SchemaVersion { get; set; }
}
```

### Change required

Add a nullable `TkbName` property immediately after `SchemaVersion`. Do NOT use `[JsonPropertyName]` — the existing serializer options handle camelCase/PascalCase already.

```csharp
public sealed class ScenarioHeaderDto
{
    public string? SubsystemType { get; set; }
    public string? SchemaVersion { get; set; }
    public string? TkbName { get; set; }    // null = no opinion
}
```

Add a `<summary>` doc comment: `/// Identifies the TKB required by this scenario. Null means "no opinion" — the node uses the fallback catalog.`

### Tests for TKB-016

**File to create:** `Hrot/Engine/Hrot.Core.Tests/ScenarioHeaderDtoTests.cs`  
**Namespace:** `Hrot.Core.Tests`

Use `System.Text.Json.JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>` with `HrotSerializerOptions.HrotJsonOptions` (already used in `HrotScenarioDtoTests.cs` in the same test project).

Add to `Hrot.Core.Tests` project (check that `Hrot.Core.Tests.csproj` references `Hrot.Core`).

Tests to write:

1. `ScenarioHeaderDto_WithTkbName_Deserializes`  
   JSON: `{"Header":{"SubsystemType":"Hrot.SimHost","TkbName":"Sample_v1"},"Entities":{}}`  
   Assert: `envelope.Header.TkbName == "Sample_v1"`.

2. `ScenarioHeaderDto_WithoutTkbName_IsNull`  
   JSON: `{"Header":{"SubsystemType":"Hrot.SimHost"},"Entities":{}}`  
   Assert: `envelope.Header.TkbName == null`.

3. `ScenarioHeaderDto_TkbNameNull_InJson_IsNull`  
   JSON: `{"Header":{"SubsystemType":"Hrot.SimHost","TkbName":null},"Entities":{}}`  
   Assert: `envelope.Header.TkbName == null`.

---

## Task TKB-021 — Wire `ActiveTkbName` into scenario save pipeline

This task has two sub-tasks:
1. Extend the FDP `ScenarioHeader` record (FDP project)
2. Add `ITkbDatabase?` to `ScenarioFileService` and stamp `TkbName` when saving (HROT project)

### Sub-task A: Extend `ScenarioHeader` FDP record

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioHeader.cs`

Current:
```csharp
public record ScenarioHeader(string SubsystemType, int SchemaVersion = 1);
```

New (add `TkbName` as optional parameter AFTER `SchemaVersion`):
```csharp
public record ScenarioHeader(string SubsystemType, int SchemaVersion = 1, string? TkbName = null);
```

Update doc comment to mention `TkbName`.

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`

In the `Serialize` method, find the `headerNode` assembly block:

```csharp
var headerNode = new JsonObject
{
    ["SubsystemType"]  = JsonValue.Create(header.SubsystemType),
    ["SchemaVersion"]  = JsonValue.Create(header.SchemaVersion),
};
```

Change to conditionally include `TkbName` when non-null:

```csharp
var headerNode = new JsonObject
{
    ["SubsystemType"]  = JsonValue.Create(header.SubsystemType),
    ["SchemaVersion"]  = JsonValue.Create(header.SchemaVersion),
};
if (header.TkbName != null)
    headerNode["TkbName"] = JsonValue.Create(header.TkbName);
```

Do NOT break existing callers — `TkbName` defaults to null so existing `new ScenarioHeader("Hrot.SimHost")` calls continue to work without change.

**Tests for Sub-task A:**

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioSerializerTkbNameTests.cs`  
Namespace: `Fdp.Toolkit.Scenario.Tests`

The FDP test project already tests `ScenarioSerializer`. Check which test project covers it. The filter `FullyQualifiedName~Tkb` already covers `Fdp.Toolkits.Tests`.

Tests to write:

1. `Serialize_WithTkbName_IncludesTkbNameInHeader`  
   Create an empty repo + `ScenarioHeader("Hrot.SimHost", TkbName: "Alpha_v1")`.  
   Call `serializer.Serialize(repo, header)`.  
   Assert: `dom["Header"]["TkbName"].GetValue<string>() == "Alpha_v1"`.

2. `Serialize_WithoutTkbName_OmitsTkbNameFromHeader`  
   Create an empty repo + `ScenarioHeader("Hrot.SimHost")` (no TkbName).  
   Call `serializer.Serialize(repo, header)`.  
   Assert: `dom["Header"]["TkbName"] == null` (property absent).

### Sub-task B: Wire `ITkbDatabase?` into `ScenarioFileService`

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`

Add `ITkbDatabase? tkbDb = null` optional parameter to the constructor, AFTER all existing parameters:

```csharp
public ScenarioFileService(
    ScenarioSerializer serializer,
    FdpEventBus? bus = null,
    IZoneManagerService? zoneService = null,
    ITkbDatabase? tkbDb = null)
{
    _serializer  = serializer  ?? throw new ArgumentNullException(nameof(serializer));
    _bus         = bus;
    _zoneService = zoneService;
    _tkbDb       = tkbDb;
}
```

Add `private readonly ITkbDatabase? _tkbDb;` field.

Add the required `using Fdp.Toolkit.Tkb;` using if not already present.

In `SaveScenario`, update the envelope `Header` construction:

BEFORE:
```csharp
var envelope = new HrotScenarioEnvelopeDto
{
    Header   = new ScenarioHeaderDto { SubsystemType = "Hrot.Scenario", SchemaVersion = "1.0" },
    Zones    = (activeZones != null && activeZones.Count > 0) ? activeZones : null,
    Entities = fdpDom["Entities"]?.AsObject() ?? fdpDom["entities"]?.AsObject(),
};
```

AFTER:
```csharp
var envelope = new HrotScenarioEnvelopeDto
{
    Header   = new ScenarioHeaderDto
    {
        SubsystemType = "Hrot.Scenario",
        SchemaVersion = "1.0",
        TkbName       = _tkbDb?.ActiveTkbName,
    },
    Zones    = (activeZones != null && activeZones.Count > 0) ? activeZones : null,
    Entities = fdpDom["Entities"]?.AsObject() ?? fdpDom["entities"]?.AsObject(),
};
```

Also update the `var header = new ScenarioHeader("Hrot.Scenario")` line to pass `TkbName`:

```csharp
var header  = new ScenarioHeader("Hrot.Scenario", TkbName: _tkbDb?.ActiveTkbName);
```

**Tests for Sub-task B:**

File: `Hrot/Engine/Hrot.Presentation.Tests/ScenarioFileServiceTkbTests.cs`  
Namespace: `Hrot.ScenarioEditor.Tests`

The test project is `Hrot.Presentation.Tests`. It already references `Hrot.Presentation` and `Hrot.Core`. It also needs `Fdp.Toolkit.Tkb` types (via transitive reference through `Hrot.Presentation`).

Tests to write:

1. `SaveScenario_WithActiveTkbName_StampsTkbNameInHeader`  
   - Create a `TkbDatabase` and set `ActiveTkbName = "Sample_v1"`.
   - Create a `ScenarioFileService` with `tkbDb: db`.
   - Save to temp file.
   - Read the saved file, deserialize as `HrotScenarioEnvelopeDto` using `HrotSerializerOptions.HrotJsonOptions`.
   - Assert: `envelope.Header.TkbName == "Sample_v1"`.

2. `SaveScenario_WithNullActiveTkbName_OmitsOrNullsTkbName`  
   - Create a `TkbDatabase` (ActiveTkbName is null by default).
   - Create a `ScenarioFileService` with `tkbDb: db`.
   - Save to temp file.
   - Read the saved file, deserialize as `HrotScenarioEnvelopeDto`.
   - Assert: `envelope.Header.TkbName == null`.

3. `SaveScenario_WithoutTkbDatabase_OmitsOrNullsTkbName`  
   - Create a `ScenarioFileService` WITHOUT `tkbDb` parameter (null).
   - Save to temp file.
   - Deserialize and assert `envelope.Header.TkbName == null`.

Use `new UTF8Encoding(false)` if needed for encoding, and `Path.GetTempFileName()` for temp file isolation.

---

## Task TKB-018 — Orchestrator TkbName consensus check

**File:** `Hrot/Subsystems/Hrot.Orchestrator/StorageGatewayModule.cs`

Add a private static helper method `CheckTkbNameConsensus(string[] files)` and call it in `PrefetchScenarioAsync` right after the empty-directory guard and BEFORE the parallel copy loop.

### Algorithm

```csharp
/// <summary>
/// Reads the <c>TkbName</c> field from the <c>Header</c> section of each JSON file
/// using a forward-only <see cref="System.Text.Json.Utf8JsonReader"/> (no DOM allocation).
/// Throws <see cref="InvalidOperationException"/> if any two non-empty TkbName values disagree.
/// </summary>
private static void CheckTkbNameConsensus(string[] files)
{
    string? agreedTkbName = null;
    string? agreedSourceFile = null;

    foreach (var file in files)
    {
        if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            continue;

        string? tkbName = PeekTkbNameFromFile(file);
        if (string.IsNullOrEmpty(tkbName))
            continue;

        if (agreedTkbName == null)
        {
            agreedTkbName   = tkbName;
            agreedSourceFile = file;
        }
        else if (!string.Equals(agreedTkbName, tkbName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"[Gateway] TkbName consensus check failed: " +
                $"'{agreedTkbName}' (from '{Path.GetFileName(agreedSourceFile)}') " +
                $"conflicts with '{tkbName}' (from '{Path.GetFileName(file)}').");
        }
    }
}
```

### PeekTkbNameFromFile helper

Reads `Header.TkbName` using forward-only `Utf8JsonReader`. No `JsonDocument` allocation.

```csharp
private static string? PeekTkbNameFromFile(string filePath)
{
    try
    {
        var bytes = File.ReadAllBytes(filePath);
        var reader = new System.Text.Json.Utf8JsonReader(bytes,
            new System.Text.Json.JsonReaderOptions { AllowTrailingCommas = true });

        bool inHeader = false;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case System.Text.Json.JsonTokenType.PropertyName:
                    var propName = reader.GetString();
                    if (!inHeader && (propName == "Header" || propName == "header"))
                    {
                        inHeader = true;
                    }
                    else if (inHeader && (propName == "TkbName" || propName == "tkbName"))
                    {
                        reader.Read();
                        return reader.TokenType == System.Text.Json.JsonTokenType.String
                            ? reader.GetString() : null;
                    }
                    break;
                case System.Text.Json.JsonTokenType.StartObject:
                case System.Text.Json.JsonTokenType.EndObject:
                    // Only enter header; once we exit header object, stop.
                    if (inHeader && reader.TokenType == System.Text.Json.JsonTokenType.EndObject)
                        return null;
                    break;
            }
        }
        return null;
    }
    catch
    {
        return null;
    }
}
```

### Call site in `PrefetchScenarioAsync`

Insert AFTER the empty-files guard and BEFORE the `Parallel.ForEach` pairs loop:

```csharp
if (files.Length == 0)
    throw new InvalidOperationException(...);   // existing guard

// NEW: sanity gate — TkbName must agree across all scenario files.
CheckTkbNameConsensus(files);

int success = 0, failure = 0;
var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies };
```

### Tests for TKB-018

**File to extend:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/StorageGatewayTests.cs`

Add a new `[Collection("OrchestratorTests")]` test class `StorageGatewayTkbConsensusTests` (or add methods to the existing `StorageGatewayTests` class — prefer a separate class at the bottom of the same file).

Tests to write:

1. `PrefetchScenario_SameTkbName_AllFiles_Succeeds`  
   - Create a NAS source dir with two JSON files, both containing `{"Header":{"TkbName":"Alpha_v1",...},...}`.
   - Create a destination dir.
   - Call `PrefetchScenarioAsync`.
   - Assert: completes without exception, `result.IsFullSuccess == true`.

2. `PrefetchScenario_ConflictingTkbNames_ThrowsInvalidOperationException`  
   - Create a NAS source dir with two JSON files:
     - File 1: `{"Header":{"TkbName":"Alpha_v1"}}`
     - File 2: `{"Header":{"TkbName":"Beta_v1"}}`
   - Assert: `await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.PrefetchScenarioAsync(...))`.

3. `PrefetchScenario_NullTkbNames_AllFiles_Succeeds`  
   - Two JSON files with `{"Header":{"SubsystemType":"Hrot.SimHost"}}` (no TkbName).
   - Assert: completes without exception.

4. `PrefetchScenario_MixedNullAndNonNull_SameName_Succeeds`  
   - File 1: `{"Header":{"TkbName":"Alpha_v1"}}`
   - File 2: `{"Header":{"SubsystemType":"Orchestrator"}}` (no TkbName)
   - Assert: completes without exception.

5. `PrefetchScenario_NonJsonFiles_AreIgnoredByConsensusCheck`  
   - Create source dir with `Hrot.SimHost.json` (TkbName: "Alpha_v1") + `some.bin` file.
   - Assert: completes without exception (non-.json files skipped).

For each test that needs a destination, create a temp dir and create at least one `NodeDistributionTarget` pointing to it. At test teardown, delete the temp dirs.

### Important note on test setup

For `PrefetchScenarioAsync`, targets must be provided. Use a single `NodeDistributionTarget`:
```csharp
var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
Directory.CreateDirectory(destDir);
var targets = new List<NodeDistributionTarget>
{
    new NodeDistributionTarget { NodeId = 1, DestinationPath = destDir }
};
```

The JSON test files must be full minimal valid JSON that passes `File.ReadAllBytes` + `Utf8JsonReader`. Use `System.IO.File.WriteAllText(path, json, new UTF8Encoding(false))`.

---

## Build Verification

After implementation, run:

```powershell
# FDP build
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP ; dotnet build FDP.sln -v m 2>&1 | Select-String "error|Build succeeded|Build FAILED" | Select-Object -Last 5

# FDP TKB tests
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Tkb" --no-build -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 5

# Hrot.Core.Tests
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test Hrot\Engine\Hrot.Core.Tests\Hrot.Core.Tests.csproj -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 5

# Hrot.Orchestrator.Tests
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test Hrot\Subsystems\Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 5

# Hrot.Presentation.Tests
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test Hrot\Engine\Hrot.Presentation.Tests\Hrot.Presentation.Tests.csproj -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 5

# SimHost Tests (Tkb filter)
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --filter "FullyQualifiedName~Tkb" -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 5
```

No new errors are expected. Pre-existing 22 errors in `Hrot.SimHost.Integration.Tests` are unrelated.

---

## Report

Submit a `BATCH-07-REPORT.md` in `.dev/tkb-1/reports/` with:
- Summary of changes per task
- Test counts per project
- Any deviations or issues
