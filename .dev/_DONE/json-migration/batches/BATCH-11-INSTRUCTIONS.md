# BATCH-11 — Blueprint and TKB Envelope Rollout (JM-P2-004 + JM-P2-005)

**Batch Number:** BATCH-11
**Tasks:** JM-P2-004, JM-P2-005
**Phase:** Phase 2 — Envelope rollout
**Estimated Effort:** 6-8 hours
**Workspace root:** `d:\Work\IOS-IG-SimHost-FDP`

---

## 1. Onboarding

**Read before starting:**

- `.dev/json-migration/TASK-DETAILS.md` — sections for JM-P2-004 and JM-P2-005
- `.dev/json-migration/05-integration-patches.md` — sections for `BlueprintJsonServices` and `TkbLoadClusterStateHandler`
- `.github/skills/developer/SKILL.md` — your role and quality standards

**Build command (run before and after changes):**
```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 8
```

**Known pre-existing build failure:** `Hrot.Blueprints.Tests` has compile errors
(`Hrot.Editor` namespace missing, `IAnimationTkbQueries`) — pre-existing, unrelated to
this batch. Do not attempt to fix.

**Key Phase 2 constraint:**
`JsonEnvelope.Peek/Read` THROWS `MigrationException` when `$meta` is absent.
Unit tests that call adapted code paths MUST use JSON with `$meta`.

---

## 2. Task JM-P2-004 — Patch Blueprint Read/Write Paths

**Full spec:** `.dev/json-migration/TASK-DETAILS.md#jm-p2-004--patch-blueprint-readwrite-paths`
**Integration-patches section:** `.dev/json-migration/05-integration-patches.md` — section "BlueprintJsonServices"

### 2.1 Read these files first

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs` — full file
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/BlueprintAsset.cs` — understand the type
3. `Hrot/Engine/Hrot.Common/Scenario/Migrations/BlueprintMigrationModule.cs` — for doc type constant
4. `FDP/Engine/Fdp.Core/Serialization/Migrations/JsonEnvelope.cs` — `Write`, `HasEnvelope`, `Read` on `JsonObject`
5. `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs` — `Build` method for test setup
6. Any existing Blueprint tests in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler.Tests/` (if they exist)

### 2.2 Changes to BlueprintJsonServices.cs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs`

**Phase 2 contract:**
- `Serialize` produces JSON with `$meta` as the FIRST property.
- `Deserialize` accepts JSON with `$meta` (strips it before handing to `System.Text.Json`
  deserializer) AND accepts legacy JSON without `$meta` (backward compat).
- No injection of `MigrationServices` at the static-class level. The adapter logic is
  applied inside `Serialize/Deserialize` directly using `JsonEnvelope` utilities.
- `BlueprintAsset` has NO version field and NO `Header` object (cold-start: there is nothing
  to remove from the old format).

**a) `Serialize` change:**

```csharp
// Before:
public static string Serialize(BlueprintAsset asset)
    => JsonSerializer.Serialize(asset, _options);

// After:
public static string Serialize(BlueprintAsset asset)
{
    // Serialize the asset to a DOM, then stamp $meta before returning.
    var dom = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();
    JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, 1));
    return dom.ToJsonString();
}
```

Add required `using` directives:
```csharp
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
```

**b) `Deserialize` change:**

```csharp
// Before:
public static BlueprintAsset? Deserialize(string json)
    => JsonSerializer.Deserialize<BlueprintAsset>(json, _options);

// After:
public static BlueprintAsset? Deserialize(string json)
{
    // Strip $meta if present (Phase 2 format). Legacy format has no $meta.
    if (JsonEnvelope.HasEnvelope(json))
    {
        // Parse the outer envelope but pass the body JSON to the deserializer.
        // Re-serialize without $meta for the underlying JsonSerializer.
        var dom = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        dom.Remove("$meta");
        return JsonSerializer.Deserialize<BlueprintAsset>(dom.ToJsonString(), _options);
    }
    return JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
}
```

**Note on `JsonEnvelope.HasEnvelope(string json)`:** Check if `JsonEnvelope` has a `HasEnvelope`
overload that takes a `string`. If it does NOT exist, check via string contains or parse:
```csharp
// Lightweight check without full parse:
if (json.Contains("\"$meta\""))
{
    var dom = JsonNode.Parse(json)!.AsObject();
    dom.Remove("$meta");
    return JsonSerializer.Deserialize<BlueprintAsset>(dom.ToJsonString(), _options);
}
return JsonSerializer.Deserialize<BlueprintAsset>(json, _options);
```

The `dom.Remove("$meta")` approach is appropriate here because `BlueprintAsset` would fail
to deserialize if it encounters unknown `$meta` property AND its `JsonSerializerOptions`
don't use `[JsonExtensionData]` or ignore unknown properties. Check `_options` first — if
`PropertyNameCaseInsensitive = true` and there's NO `JsonUnknownTypeHandling.JsonNode`, then
unknown properties are silently ignored. If that's the case, you can skip the remove and
just let `JsonSerializer.Deserialize<BlueprintAsset>` handle `$meta` as an ignored property.

**Investigate this before implementing:** Run a quick test to see if `JsonSerializer.Deserialize<BlueprintAsset>` silently ignores `$meta` with the existing `_options`. If it does, `Deserialize` may need no change at all (legacy + Phase 2 both work without explicit stripping).

### 2.3 Csproj project reference

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj`

Add project reference to `Hrot.Common` to get `HrotDocumentTypes`:
```xml
<ProjectReference Include="$(SolutionDir)Hrot\Engine\Hrot.Common\Hrot.Common.csproj" />
```

Check if `Hrot.Common` is already referenced (directly or transitively) before adding.

### 2.4 New tests for JM-P2-004

**Location:** Create or extend
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler.Tests/BlueprintJsonServicesTests.cs`

If no test project exists for `Hrot.Blueprints.Compiler`, add tests to
`Hrot.Blueprints.Core.Tests` or a suitable existing test project. Read the project structure
first to find the right home.

**JM-P2-004-T01** `BlueprintJsonServices_Serialize_ProducesMetaEnvelope`
Create a minimal `BlueprintAsset` instance (verify the type's required fields). Call
`BlueprintJsonServices.Serialize(asset)`. Parse the result as `JsonObject`. Assert:
- `JsonEnvelope.HasEnvelope(dom)` is true
- `JsonEnvelope.Read(dom).DocType == HrotDocumentTypes.Blueprint` ("Hrot.Blueprints")
- `JsonEnvelope.Read(dom).SchemaVersion == 1`

**JM-P2-004-T02** `BlueprintJsonServices_Deserialize_Phase2_RoundTrips`
Serialize an asset, then deserialize the result. Assert the round-trip preserves
key fields (e.g., `Id`, `Name`, or any fields on `BlueprintAsset`).

**JM-P2-004-T03** `BlueprintJsonServices_Deserialize_LegacyJson_Works`
Create legacy JSON without `$meta` (directly construct a minimal JSON string that matches
the old format: `{"Id":"...","Name":"...","Nodes":[]}`). Call `Deserialize`. Assert no
exception is thrown and the asset fields are populated correctly.

---

## 3. Task JM-P2-005 — Patch TKB Read/Write Paths

**Full spec:** `.dev/json-migration/TASK-DETAILS.md#jm-p2-005--patch-tkb-readwrite-paths`
**Integration-patches section:** `.dev/json-migration/05-integration-patches.md` — section "TkbLoadClusterStateHandler"

### 3.1 Read these files first

1. `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs` — full
2. `Hrot/Subsystems/Hrot.SimHost.Tests/TkbLoadClusterStateHandlerTests.cs` — all existing tests

### 3.2 Key discovery about TkbLoadClusterStateHandler

`TkbLoadClusterStateHandler.ExtractTkbNameFromLocalScenario` reads a SEPARATE lightweight
file `{localStagingRoot}/ScenarioHeader.json` (NOT the full scenario file). It uses a
`Utf8JsonReader` that scans all tokens and returns the value of the FIRST property named
`"TkbName"` found anywhere in the document.

This means: adding `$meta` to `ScenarioHeader.json` does NOT break the existing scanner,
because the reader will skip `$meta` (it's not `TkbName`) and continue until it finds
`TkbName`. **No change to the production C# code is needed.**

### 3.3 Source changes for JM-P2-005

**`TkbLoadClusterStateHandler.cs` — NO CHANGE to production C# code.** The forward scanner
already handles `$meta` transparently.

**However, the test helper must be updated** to write Phase 2 format, and a new test must
verify Phase 2 compatibility:

### 3.4 Test updates for JM-P2-005

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/TkbLoadClusterStateHandlerTests.cs`

**a) Update `WriteScenarioHeader`:** The helper currently writes either `{"TkbName":"..."}` or
`{"SubsystemType":"SimHost"}`. Update to support Phase 2 format:

```csharp
private void WriteScenarioHeader(string? tkbName, bool phase2Format = false)
{
    string content;
    if (phase2Format && tkbName != null)
    {
        // Phase 2 format: $meta first, then TkbName at root level.
        content = $"{{\"$meta\":{{\"docType\":\"Hrot.Scenario\",\"schemaVersion\":1}},\"TkbName\":\"{tkbName}\"}}";
    }
    else if (tkbName != null)
    {
        // Legacy format.
        content = $"{{\"TkbName\":\"{tkbName}\"}}";
    }
    else
    {
        content = "{\"SubsystemType\":\"SimHost\"}";
    }
    File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), content, new UTF8Encoding(false));
}
```

**b) Add new test JM-P2-005-T01** `ExtractTkbName_Phase2Format_ReturnsCorrectName`

```
Given: A ScenarioHeader.json with Phase 2 format ($meta first, then TkbName)
When:  PrepareAsync is called (via the TkbLoadClusterStateHandler)
Then:  The handler correctly extracts the TkbName and loads the right TKB artifact
```

The test mirrors the existing `PrepareAsync_WithTkbName_LoadsTkbFromZip` test but uses
`phase2Format: true` when calling `WriteScenarioHeader`. You can either:
- Add `phase2Format` parameter to the `WriteScenarioHeader` helper (preferred)
- Or write the Phase 2 JSON inline in the new test

**c) Verify existing tests still pass** — all existing `TkbLoadClusterStateHandlerTests` must
continue passing after the helper update.

---

## 4. Test Discipline

**Mandatory workflow:**
1. Run existing tests before touching any file
2. Make changes
3. Run tests again to verify no regressions
4. Add new tests (red → green)

**Test run commands:**
```powershell
dotnet test "Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 8
```

Find the Blueprint test project (if it exists):
```powershell
Get-ChildItem "Hrot/Subsystems/Blueprints" -Recurse -Filter "*.Tests.csproj" | Select-Object -ExpandProperty FullName
```

---

## 5. Build Verification

Before writing the report:

```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 8
dotnet test "Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 5
```

Also run `Hrot.Common.Tests` to ensure no regressions:
```powershell
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 5
```

---

## 6. Report Format

Write your report to `.dev/json-migration/reports/BATCH-11-REPORT.md`.

Structure:
```markdown
# BATCH-11 Report
**Status:** Complete | Partial | Blocked
**Tests:** X new passing | Y total per project

## Tasks Completed
- [ ] JM-P2-004: Blueprint JSON envelope (BlueprintJsonServices)
- [ ] JM-P2-005: TKB envelope compatibility (TkbLoadClusterStateHandler tests)

## Developer Insights
### Issues Encountered
### Design Decisions Beyond the Spec
### BlueprintAsset unknown-properties behavior (investigation result)

## Build / Test Results
## Files Created / Modified
```

---

## 7. Autonomous Guidance

- If `Hrot.Blueprints.Compiler.Tests` does not exist, create it with proper `.csproj` and
  add it to the `IOS-IG-SimHost.sln` solution.
- If `BlueprintAsset`'s JSON deserializer silently ignores `$meta` (no `[JsonExtensionData]`,
  default behavior in System.Text.Json), then `Deserialize` needs no change. Document this
  finding in the report's "Developer Insights" section.
- `HrotDocumentTypes.Blueprint = "Hrot.Blueprints"` — this is in `Hrot.Common`.
- `TkbLoadClusterStateHandler` requires NO production C# change for JM-P2-005. Only tests change.
- Do not stop for questions. Adapt based on what you find in the code.
- Your role is described in `.github/skills/developer/SKILL.md`.
