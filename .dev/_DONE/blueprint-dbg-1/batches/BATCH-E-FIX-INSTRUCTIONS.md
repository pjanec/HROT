# BATCH-E-FIX: Full Rebuild debug-map loading (missing pause/overlay)

**Depends on:** BATCH-E + BATCH-E-CLEANUP  
**Estimated Effort:** 1 hour  
**Priority:** HIGH

---

## 🚨 EXECUTION DIRECTIVE

**Do NOT ask questions or permission. Edit files directly, build, run tests, write report.**

---

## Context

Breakpoint markers show on nodes but the sim doesn't pause and no gold execution outline appears. Root cause: `BlueprintEditorModule.OnReloadCompleted` doesn't load `.dbgmap.json` files after Full Rebuilds. Without debug maps, the session can't fully resolve breakpoint structure hashes (though currently hash check still passes) — but more importantly, this was the architect-confirmed gap.

The `OnExternalHit` → `OnHit` → `RequestPause()` chain in `DataBreakpointManager.cs` is already implemented (verified). The missing link is the debug map registration.

---

## Task: Fix Full Rebuild debug-map loading

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorModule.cs`

Locate the `OnReloadCompleted` method. The `FullRebuildViaFileWatcher` branch currently only logs the DLL path:

```csharp
else if (info.Source == ReloadSource.FullRebuildViaFileWatcher)
{
    // Read debug maps from DLL output directory (DllPath is set for full rebuilds).
    if (info.DllPath != null)
        _outputConsole.LogInfo($"Full rebuild completed: {info.DllPath}");
}
```

Replace it with code that scans for and loads all `.dbgmap.json` files from the DLL's output directory and registers them with the debug session:

```csharp
else if (info.Source == ReloadSource.FullRebuildViaFileWatcher)
{
    if (info.DllPath != null)
    {
        _outputConsole.LogInfo($"Full rebuild completed: {info.DllPath}");

        // Load and register debug maps for all assets in the build output.
        var dir = System.IO.Path.GetDirectoryName(info.DllPath);
        if (dir != null)
        {
            foreach (var mapFile in System.IO.Directory.EnumerateFiles(dir, "*.dbgmap.json"))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(mapFile);
                    var map = System.Text.Json.JsonSerializer.Deserialize<Hrot.Blueprints.Core.Compiler.Emit.DebugMap>(json);
                    if (map != null && _session != null)
                        _session.RegisterDebugMap(map);
                }
                catch (Exception ex)
                {
                    _outputConsole.LogError($"Failed to load debug map {mapFile}: {ex.Message}");
                }
            }
        }
    }
}
```

**Important details:**
- The file already imports `System` and `System.IO` — verify, add if missing.
- `System.Text.Json` may need to be added.
- `_session` field — check if it exists (it should be an `IBlueprintDebugSession?`). If the field name is different, use the correct name.
- `DebugMap` is in namespace `Hrot.Blueprints.Core.Compiler.Emit` — verify the using is present.
- `RegisterDebugMap` is a method on `IBlueprintDebugSession` — verify the call signature matches.

---

## Build and test

```
dotnet build IOS-IG-SimHost.sln -c Debug
```
Must be 0 errors.

```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug --no-build
```
Must have 0 new failures. Pre-existing (2): `AllocationFreeTests`, `WhenNodePerfTests`.

---

## Write report

Write to `.dev/_DONE/blueprint-dbg-1/reports/BATCH-E-FIX-REPORT.md`:
- What was changed
- Build result
- Test result (pass/fail/skip counts)
- Any issues
