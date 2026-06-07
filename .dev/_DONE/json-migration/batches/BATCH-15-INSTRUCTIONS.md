# BATCH-15 — JM-P2-010: Committed Fixture Envelope Migration Script

## Overview

Implement the `Fdp.Tools.EnvelopeStamper` console tool and stamp all committed JSON fixture files
with a valid `$meta` envelope, per design doc *07 §5.1 step 4*.

Task reference: [JM-P2-010] in `.dev/json-migration/TASK-DETAILS.md` (lines 357–378).
Design references: *07 §5.1 step 4*, C-4.

---

## Context: What Fixtures Need Stamping

During codebase analysis, the following committed JSON fixture types were found **without** `$meta`:

### 1. Scenarios (docType = `"Hrot.Scenario"`, schemaVersion = 1)
These files have a `"header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }` block:
- `scenarios/hill-attack/scenario.json`
- `scenarios/test-fire/scenario.json`
- `scenarios/test-move/scenario.json`

### 2. Blueprints (docType = `"Hrot.Blueprints"`, schemaVersion = 1)
These files have a `"Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" }` block:
- All `*.json` files under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/` that are NOT:
  `*.deps.json`, `*.runtimeconfig.json`, or `xunit.runner.json`
- All `*.json` files under `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/`

### 3. Road Networks (docType = `"Fdp.RoadNetwork"`, schemaVersion = 1)
These files have `"nodes": [...]` and `"segments": [...]` at top-level (no `header`/`Header` block):
- `Hrot/Subsystems/Hrot.SimHost/Assets/sample_road.json`
- `Hrot/Engine/Hrot.Core.Tests/Assets/sample_road.json`
- `Hrot/Engine/Hrot.Map.Common.Tests/Assets/sample_road.json`
- `FDP/Examples/Fdp.Examples.CarKinem/Assets/sample_road.json`

### Files explicitly NOT in scope
- `config.json`, `xunit.runner.json`, `*.deps.json`, `*.runtimeconfig.json`, `launchSettings.json`
- Files under `ExtDeps/` (third-party code)
- Files under `.tmp/`, `.claude/`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/*.json` (deliberate test fixtures with bad `$meta`)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/data/*.json` (nav mesh data — not a migration-managed format)
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/e2e_*.json` (test scripts — not in scope per task)
- `FDP/Examples/Fdp.Examples.UrbanCombat/Assets/*.json` (BehaviorTree format — not in scope per task)
- `FDP/ExtDeps/FastBTree/**`, `FDP/ExtDeps/FastHSM/**` (third-party)

---

## Deliverables

### 1. New Project: `Fdp.Tools.EnvelopeStamper`

**Location:** `FDP/Tools/Fdp.Tools.EnvelopeStamper/`

**Project file:** `Fdp.Tools.EnvelopeStamper.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>fdp-envelope-stamper</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Fdp.Tools.EnvelopeStamper.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
  <ItemGroup>
    <!-- JsonEnvelope, DocumentMeta, FdpDocumentTypes -->
    <ProjectReference Include="..\..\Engine\Fdp.Core\Fdp.Core.csproj" />
    <!-- HrotDocumentTypes -->
    <ProjectReference Include="..\..\..\Hrot\Engine\Hrot.Common\Hrot.Common.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CommandLineParser" Version="2.9.1" />
  </ItemGroup>
</Project>
```

**Source files required:**

#### `StamperOptions.cs`
```csharp
using CommandLine;

namespace Fdp.Tools.EnvelopeStamper;

internal sealed class StamperOptions
{
    [Option('r', "root", Required = true,
        HelpText = "Workspace root directory to walk.")]
    public string Root { get; set; } = string.Empty;

    [Option("dry-run", Default = false,
        HelpText = "Print what would be stamped without writing files.")]
    public bool DryRun { get; set; }
}
```

#### `FixtureStamper.cs`

This is the core logic class (marked `internal static`).  Contains:

1. **`StampDirectory(string root, bool dryRun, TextWriter stdout, TextWriter stderr) : StampResult`**
   - Walks all `.json` files recursively under `root`, skipping paths that match any
     exclusion predicate (see below).
   - For each candidate file, calls `DetectDocType(JsonObject dom)` → `DocumentMeta?`.
   - If null → skip with a "skipped" log message.
   - If the file already has `$meta` → skip (idempotent: call `JsonEnvelope.HasMeta(dom)`).
   - Otherwise → call `JsonEnvelope.Write(dom, meta)` and write back to disk
     (or log if `--dry-run`).
   - Returns a `StampResult` with counts: `Stamped`, `AlreadyStamped`, `Skipped`, `Errors`.

2. **`DetectDocType(JsonObject dom) : DocumentMeta?`**
   ```
   Detection rules (in order):
   a. If dom has property "header" (case-insensitive lookup) that is a JsonObject,
      and that object has "subsystemType" property (case-insensitive) → use its value
      as docType, schemaVersion = 1.
      NOTE: For OrchestratorContext (docType=="Hrot.OrchestratorContext"), use schemaVersion=2 (C-4).
   b. If dom has property "Header" that is a JsonObject, and has "SubsystemType" property
      → use its value as docType, schemaVersion = 1.
   c. If dom has both a "nodes" property (JsonArray) and a "segments" property (JsonArray)
      at top-level → return DocumentMeta("Fdp.RoadNetwork", 1).
   d. Otherwise → return null (not a known fixture type, skip).
   ```

3. **Exclusion predicates** for `ShouldSkipPath(string path) : bool`:
   - Returns true if path contains any of: `\obj\`, `/obj/`, `\bin\`, `/bin/`
   - Returns true if filename matches: `*.deps.json`, `*.runtimeconfig.json`,
     `xunit.runner.json`, `launchSettings.json`, `settings.json`, `settings.local.json`
   - Returns true if path contains `\ExtDeps\` or `/ExtDeps/`
   - Returns true if path contains `\.tmp\` or `/.tmp/`
   - Returns true if path contains `\.claude\` or `/.claude/`
   - Returns true if path contains `Fdp.Core.Tests\Serialization\Migrations`
     or `Fdp.Core.Tests/Serialization/Migrations`
   - Returns true if path contains `Navigation\data` or `Navigation/data`

4. **`StampResult`** record:
   ```csharp
   internal record StampResult(int Stamped, int AlreadyStamped, int Skipped, int Errors);
   ```

#### `Program.cs`
```csharp
using CommandLine;
using Fdp.Tools.EnvelopeStamper;

internal static class Program
{
    public static int Main(string[] args)
        => RunMain(args, Console.Out, Console.Error);

    internal static int RunMain(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var result = Parser.Default.ParseArguments<StamperOptions>(args);
        return result.MapResult(
            opts => Execute(opts, stdout, stderr),
            _ => 1);
    }

    private static int Execute(StamperOptions opts, TextWriter stdout, TextWriter stderr)
    {
        if (!Directory.Exists(opts.Root))
        {
            stderr.WriteLine($"Error: directory not found: {opts.Root}");
            return 2;
        }

        var summary = FixtureStamper.StampDirectory(opts.Root, opts.DryRun, stdout, stderr);
        stdout.WriteLine();
        stdout.WriteLine($"Done. Stamped={summary.Stamped}, AlreadyStamped={summary.AlreadyStamped}, Skipped={summary.Skipped}, Errors={summary.Errors}");
        return summary.Errors > 0 ? 3 : 0;
    }
}
```

---

### 2. New Test Project: `Fdp.Tools.EnvelopeStamper.Tests`

**Location:** `FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/`

**Project file:** `Fdp.Tools.EnvelopeStamper.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Fdp.Tools.EnvelopeStamper\Fdp.Tools.EnvelopeStamper.csproj" />
  </ItemGroup>
</Project>
```

**Test file:** `FixtureStamperTests.cs`

Write the following test cases. All tests work against in-memory temporary directories.
Use `xunit` (no FluentAssertions).

```
T01 — Scenario file gets stamped with Hrot.Scenario v=1
  - Create a temp dir with a scenario.json containing:
    { "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0" }, "entities": {} }
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 1, result.Errors == 0
  - Read the file back, parse as JsonObject
  - Assert $meta.docType == "Hrot.Scenario", $meta.schemaVersion == 1
  - Assert $meta is the FIRST property in the object

T02 — Blueprint file gets stamped with Hrot.Blueprints v=1
  - Create a temp dir with a foo.bp.json containing:
    { "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" }, "AssetId": "..." }
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 1
  - Assert $meta.docType == "Hrot.Blueprints", $meta.schemaVersion == 1

T03 — Road network file gets stamped with Fdp.RoadNetwork v=1
  - Create a temp dir with a sample_road.json containing:
    { "nodes": [], "segments": [] }
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 1
  - Assert $meta.docType == "Fdp.RoadNetwork", $meta.schemaVersion == 1

T04 — Already-stamped file is skipped (idempotency)
  - Create a temp dir with a scenario.json that already has a valid $meta as first property
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 0, result.AlreadyStamped == 1

T05 — xunit.runner.json is excluded
  - Create a temp dir with an xunit.runner.json containing: { "methodDisplay": "method" }
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 0
  - Assert file content is unchanged

T06 — Files in ExtDeps subdirectory are excluded
  - Create a temp dir with structure: ExtDeps/some_lib/data.json (valid scenario format)
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 0

T07 — dry-run does not modify files
  - Create a temp dir with a scenario.json (no $meta)
  - Record original content
  - Call StampDirectory(tempDir, dryRun=true, ...)
  - Assert result.Stamped == 1 (counted as would-stamp)
  - Assert file content is UNCHANGED

T08 — OrchestratorContext fixture gets stamped with schemaVersion=2 (C-4)
  - Create a temp dir with a context.json containing:
    { "header": { "subsystemType": "Hrot.OrchestratorContext", "schemaVersion": "2.0" }, ... }
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert $meta.schemaVersion == 2

T09 — Unknown format file is skipped (returns skipped count)
  - Create a temp dir with a random.json containing: { "foo": "bar", "baz": 42 }
  - Call StampDirectory(tempDir, dryRun=false, ...)
  - Assert result.Stamped == 0, result.Skipped >= 1

T10 — $meta is the first property after stamping a scenario (order guarantee)
  - Stamp a scenario file
  - Parse the written file as JsonObject
  - Assert that the first key in the JsonObject is "$meta"
  - Assert that the "header" object is still present (old field preserved — we only ADD $meta)
```

> **Test quality note:** These tests must verify actual JSON content of written files (T01, T02, T03, T08, T10), not just return codes. Use `JsonNode.Parse(File.ReadAllText(...))` to re-read and assert.

---

### 3. Register Projects in Solution

Add both projects to `IOS-IG-SimHost.sln`:
```
dotnet sln "IOS-IG-SimHost.sln" add "FDP/Tools/Fdp.Tools.EnvelopeStamper/Fdp.Tools.EnvelopeStamper.csproj"
dotnet sln "IOS-IG-SimHost.sln" add "FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/Fdp.Tools.EnvelopeStamper.Tests.csproj"
```

---

### 4. Run the Tool Against Committed Fixtures

After building, run the tool to stamp the committed fixtures:
```
dotnet run --project "FDP/Tools/Fdp.Tools.EnvelopeStamper/Fdp.Tools.EnvelopeStamper.csproj" -- --root "d:\WORK\IOS-IG-SimHost-FDP"
```

Verify the following files now have `$meta` as their first property:
- `scenarios/hill-attack/scenario.json`
- `scenarios/test-fire/scenario.json`
- `scenarios/test-move/scenario.json`
- `Hrot/Subsystems/Hrot.SimHost/Assets/sample_road.json`
- `Hrot/Engine/Hrot.Core.Tests/Assets/sample_road.json`
- `Hrot/Engine/Hrot.Map.Common.Tests/Assets/sample_road.json`
- `FDP/Examples/Fdp.Examples.CarKinem/Assets/sample_road.json`
- At least one blueprint file in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- At least one blueprint file in `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/`

Verify the following files are NOT stamped (unchanged):
- `config.json` (root)
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/missing_meta.json`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/data/corridor.json` (if present)

---

### 5. Verify Existing Tests Still Pass

After stamping fixture files, run the relevant test suites to ensure no regressions:

```
dotnet test "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj" --no-build -c Debug
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build -c Debug
dotnet test "FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj" --no-build -c Debug
dotnet test "FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/Fdp.Tools.EnvelopeStamper.Tests.csproj" -c Debug
```

> **Note:** `Hrot.Blueprints.Tests` has pre-existing failures due to Stride editor dependency.
> Only assert that tests that previously passed continue to pass — the Stride failures are acceptable.

---

## Implementation Notes

### `JsonEnvelope.HasMeta` API
Check if `JsonEnvelope.HasMeta(JsonObject dom)` exists. If it does not, check for `$meta` by calling
`dom.TryGetPropertyValue("$meta", out _)` directly.

### Writing Files Back
After calling `JsonEnvelope.Write(dom, meta)`, serialize the DOM back to the file using:
```csharp
using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
dom.WriteTo(writer);
```

### Old Header Fields Are PRESERVED
Do NOT remove the existing `"header"` or `"Header"` block. The stamper **only adds** `$meta`
to the top. Existing deserialization code that reads `Header.SubsystemType` continues to work.
This satisfies the "round-trip except `$meta.engineVersion`" requirement — the non-`$meta`
content is byte-identical (modulo whitespace from re-serialization).

### Detection Case-Insensitivity
The scenario files use lowercase `"header"` key while blueprint files use uppercase `"Header"`.
When looking for these fields, check for both cases explicitly (not a general case-insensitive search,
since `JsonObject` by default is case-sensitive). Check for `"header"` first, then `"Header"`.

### Hrot.Common Reference from FDP/Tools
The `Fdp.Tools.EnvelopeStamper` project is in `FDP/Tools/` but needs to reference
`Hrot/Engine/Hrot.Common/Hrot.Common.csproj` for `HrotDocumentTypes`. The relative path from
`FDP/Tools/Fdp.Tools.EnvelopeStamper/` to `Hrot/Engine/Hrot.Common/` is:
`../../../../Hrot/Engine/Hrot.Common/Hrot.Common.csproj`

However, since `HrotDocumentTypes` is just string constants, you may alternatively define the
docType strings inline in the stamper rather than taking a reference to `Hrot.Common`. Use
whichever approach compiles cleanly. Avoid circular references.

Actually — since we only need string constants, **inline the docType strings** in `FixtureStamper.cs`
rather than referencing `Hrot.Common`. This keeps the tool self-contained:
```csharp
private const string DocTypeScenario = "Hrot.Scenario";
private const string DocTypeBlueprint = "Hrot.Blueprints";
private const string DocTypeRoadNetwork = "Fdp.RoadNetwork";
private const string DocTypeOrchestratorContext = "Hrot.OrchestratorContext";
// etc.
```

---

## Build + Test Commands

```powershell
# Build to catch compilation errors
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5

# Run stamper tests
dotnet test "FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/Fdp.Tools.EnvelopeStamper.Tests.csproj" -c Debug -v normal

# Run existing affected tests to verify no regressions
dotnet test "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj" -c Debug --filter "NOT Category=Stride" 2>&1 | Select-String "passed|failed|error" | Select-Object -Last 10
dotnet test "FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj" -c Debug 2>&1 | Select-String "passed|failed|error" | Select-Object -Last 5
```

---

## Batch Report Requirements

The batch report must include:
1. List of all files created/modified.
2. Test results for `Fdp.Tools.EnvelopeStamper.Tests` — all 10 tests must pass.
3. Confirmation that existing tests (Fdp.Core.Tests, Hrot.Blueprints.Tests non-Stride) still pass.
4. A sample of 3 fixture files showing their content **before** and **after** stamping
   (just the first few lines of each to confirm `$meta` is first property).
5. The full output of running the stamper tool against the workspace.
6. List of files the stamper actually modified.
