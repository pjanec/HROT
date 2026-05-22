# BATCH-24: TASK-ED-006 -- Editor Preferences, Configuration, and Remaining Test Coverage

**Batch Number:** BATCH-24
**Tasks:** TASK-ED-006
**Phase:** 6 -- Editor
**Estimated Effort:** 2-3 days
**Priority:** HIGH
**Dependencies:** BATCH-23

---

## 0. Onboarding

### Required Reading

1. `.dev/blueprints-1/batches/BATCH-24-INSTRUCTIONS.md` (this file)
2. `.dev/blueprints-1/TASK-DETAIL.md` §ED-006
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj`
4. Scan all existing files under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` -- understand what's already there
5. Scan all existing test files under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/` -- avoid duplicate tests

### Report Submission

`.dev/blueprints-1/reports/BATCH-24-REPORT.md`

---

## 1. BlueprintEditorPreferences

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorPreferences.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Editor;

public sealed class BlueprintEditorPreferences
{
    public bool AutoReloadOnSave { get; set; } = false;
    public bool WatchPanelVisible { get; set; } = true;
    public float GraphEditorGridSnap { get; set; } = 8.0f;
    public int NodeHistorySize { get; set; } = 64;
    public int HotReloadLogMaxEntries { get; set; } = 1000;

    public static BlueprintEditorPreferences Defaults => new();

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Saves preferences to <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, s_jsonOpts);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads preferences from <paramref name="path"/>.
    /// Returns defaults if the file does not exist or cannot be parsed.
    /// </summary>
    public static BlueprintEditorPreferences Load(string path)
    {
        if (!File.Exists(path)) return Defaults;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BlueprintEditorPreferences>(json, s_jsonOpts)
                   ?? Defaults;
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }
}
```

---

## 2. BlueprintEditorConfiguration

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorConfiguration.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

/// <summary>Compile-time configuration for the Blueprint editor integration.</summary>
public sealed record BlueprintEditorConfiguration(
    string DebugMapsOutputDirectory,
    string BehaviorsDllDirectory,
    string BehaviorsBuildTarget = "");

```

---

## 3. PreferencesWindow (skeleton)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/PreferencesWindow.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public sealed class PreferencesWindow : BlueprintEditorWindowBase
{
    private readonly BlueprintEditorPreferences _prefs;
    private readonly string _savePath;

    public override string Title => "Blueprint Preferences";

    public PreferencesWindow(BlueprintEditorPreferences prefs, string savePath)
    {
        _prefs    = prefs    ?? throw new ArgumentNullException(nameof(prefs));
        _savePath = savePath ?? throw new ArgumentNullException(nameof(savePath));
    }

    public override void DrawUI()
    {
        // ImGui form: AutoReloadOnSave checkbox, GraphEditorGridSnap slider, etc.
        // "Save" button: _prefs.Save(_savePath).
        // "Reset to Defaults" button: copy defaults into _prefs fields.
        // Requires ImGui runtime. Stub for Slice 1.
    }
}
```

---

## 4. MockOutputConsole test helper

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/MockOutputConsole.cs`:

```csharp
using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

internal sealed class MockOutputConsole : IOutputConsole
{
    public List<string> InfoMessages    { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<string> ErrorMessages   { get; } = new();
    public List<string> DebugMessages   { get; } = new();
    public List<string> DiagMessages    { get; } = new();

    public void LogInfo(string message)       => InfoMessages.Add(message);
    public void LogWarning(string message)    => WarningMessages.Add(message);
    public void LogError(string message)      => ErrorMessages.Add(message);
    public void LogDebug(string message)      => DebugMessages.Add(message);
    public void LogDiagnostic(string message) => DiagMessages.Add(message);
}
```

**IMPORTANT:** Read `IOutputConsole.cs` first to get the exact method signatures. The mock must implement all methods.

---

## 5. Tests Required

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/PreferencesTests.cs`:

**SC1: `Preferences_Defaults_AreCorrect`**
- `BlueprintEditorPreferences.Defaults.NodeHistorySize == 64`.
- `Defaults.AutoReloadOnSave == false`.
- `Defaults.GraphEditorGridSnap == 8.0f`.

**SC2: `Preferences_SaveAndLoad_RoundTrip`**
- Create preferences with non-default values. `Save(tempPath)`. `Load(tempPath)`.
- Assert all field values match.
- Delete temp file after test.

**SC3: `Preferences_Load_NonExistentFile_ReturnsDefaults`**
- `Load("/nonexistent/path/prefs.json")` returns object equal to Defaults (NodeHistorySize == 64).

**SC4: `Preferences_Load_InvalidJson_ReturnsDefaults`**
- Write `"not valid json"` to a temp file. `Load(tempPath)` returns Defaults. No exception thrown.

**SC5: `PreferencesWindow_Title_IsCorrect`**
- Create `PreferencesWindow`. Assert `window.Title == "Blueprint Preferences"`.

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/QuickReloadServiceTests.cs`:

**SC1: `QuickReloadService_TriggerAsync_LogsToOutputConsole`**
- Create `MockOutputConsole`. Create `QuickReloadService(catalog, editorState, console)`.
- Call `TriggerAsync(asset)`. Assert `console.InfoMessages.Count > 0`.

**SC2: `QuickReloadService_TriggerAsync_NonNullAsset_Required`**
- `await service.TriggerAsync(null!)` throws `ArgumentNullException`.

**IMPORTANT:** Find `BlueprintAsset` type before writing these tests. You need a real or minimal `BlueprintAsset` instance. Check `Fdp.Toolkit.Blueprints` namespace. If it requires complex construction, stub with whatever minimal constructor is available.

---

## 6. Build + Verify

```powershell
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor -v quiet
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Editor" -v minimal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 errors, 0 failures. Total count >= 463 (456 + 7 new tests).

---

## 7. Order of Operations

1. Read BATCH-24-INSTRUCTIONS.md.
2. Read `IOutputConsole.cs` for MockOutputConsole.
3. Find `BlueprintAsset` in `Fdp.Toolkit.Blueprints` namespace.
4. Read existing editor files to check what's already there (avoid duplicates).
5. Create `BlueprintEditorPreferences.cs`.
6. Create `BlueprintEditorConfiguration.cs`.
7. Create `PreferencesWindow.cs`.
8. Build Editor. Fix errors.
9. Create `Editor/MockOutputConsole.cs`.
10. Create `Editor/PreferencesTests.cs` (SC1-SC5).
11. Create `Editor/QuickReloadServiceTests.cs` (SC1-SC2).
12. Build Tests. Fix errors.
13. Run Editor filter tests. Fix failures.
14. Run full suite. Fix any failures.
15. Commit.
16. Write report.

---

## 8. Commit

```
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/
git commit -m "feat(blueprints): BATCH-24 ED-006 editor preferences config and test coverage

- BlueprintEditorPreferences: JSON save/load, graceful fallback on invalid JSON
- BlueprintEditorConfiguration: DebugMapsOutputDirectory, BehaviorsDllDirectory
- PreferencesWindow: skeleton with IBlueprintEditorWindow compliance
- MockOutputConsole: test helper for IOutputConsole
- PreferencesTests: SC1-SC5 defaults, round-trip, missing/invalid file
- QuickReloadServiceTests: SC1-SC2 logging and null guard

Baseline: 456 -> X pass / 5 skip / 0 fail"
```

---

## Success Criteria

| SC | Check |
|----|-------|
| SC1 | Preferences defaults correct |
| SC2 | Preferences save+load round-trip |
| SC3 | Load missing file returns defaults |
| SC4 | Load invalid JSON returns defaults (no exception) |
| SC5 | PreferencesWindow title correct |
| SC6 | QuickReloadService logs to output console |
| SC7 | QuickReloadService null asset guard |
| Build | dotnet build Hrot.Blueprints.Editor zero errors |
| Tests | 0 failures full suite |
