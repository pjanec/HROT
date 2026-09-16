# BATCH-E-CLEANUP: Remove redundant debug renderers

**Depends on:** BATCH-E  
**Estimated Effort:** 30 min  
**Priority:** HIGH

---

## 🚨 EXECUTION DIRECTIVE

**You are a coding agent. Do NOT ask questions or permission. Edit files directly, build, run tests, write report. No interactive prompts.**

---

## Context

BATCH-E bridged `IBlueprintDebugSession` → NodeEdit `IDebugSession`. NodeEdit's native `NodeRenderer` now draws breakpoint markers (16px red circle on node header) and execution overlays (gold pulse, orange afterglow). But the old custom renderers are still registered in `BuildRenderers()`, causing **double rendering** — two overlapping red circles and two overlapping execution outlines.

The architect confirmed: remove the redundant custom renderers completely.

---

## Task 1: Clean up BuildRenderers in BlueprintDocumentFactory.cs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`

Locate the `BuildRenderers` method (~line 419). Replace the entire method body with this streamlined version:

```csharp
    private static IReadOnlyList<ICustomCanvasRenderer> BuildRenderers(
        BlueprintAsset                              bpAsset,
        IReadOnlyList<ICustomCanvasRenderer>?       extra)
    {
        var list = new List<ICustomCanvasRenderer>
        {
            // Blueprint custom renderer: pulsing overlay when a WhenNode fires at runtime.
            // In debug mode it is active; in release mode IsActive == false (no per-frame cost).
            new WhenFiringPulseRenderer(),
        };

        if (extra != null)
            list.AddRange(extra);

        return list;
    }
```

Key changes:
- Remove the `IBlueprintDebugSession? debugSession = null` parameter
- Remove `BlueprintBreakpointGutterRenderer` and `BlueprintRuntimeOverlayRenderer` from the list
- Remove the `if (debugSession != null) { gutterRenderer.SetSession...; runtimeOverlay.SetSession...; }` block
- Keep `WhenFiringPulseRenderer` and `extra` renderers

Then update the **call site** of `BuildRenderers` in the same file (~line 183). Currently:
```csharp
var renderers = BuildRenderers(bpAsset, extraRenderers, debugSession);
```
Change to:
```csharp
var renderers = BuildRenderers(bpAsset, extraRenderers);
```

---

## Task 2: Delete the 3 redundant files

Delete these files entirely (they are now dead code):

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/BlueprintBreakpointGutterRenderer.cs`
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/BlueprintRuntimeOverlayRenderer.cs`
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintBreakpointContextMenuProvider.cs`

Use `git rm` or just delete them — they're superseded by the native NodeEdit rendering path.

---

## Task 3: Remove any remaining references

Search the entire codebase for references to the deleted types:

- `BlueprintBreakpointGutterRenderer`
- `BlueprintRuntimeOverlayRenderer`  
- `BlueprintBreakpointContextMenuProvider`

If any references remain (tests, imports, etc.), update or remove them. For tests that test these deleted types, delete the test methods or the entire test file if it only contains tests for these types.

Also check for any remaining `using` statements that referenced these types' namespaces and are no longer needed.

---

## Task 4: Build and test

```
dotnet build IOS-IG-SimHost.sln -c Debug
```
Must be 0 errors. If build fails, fix all errors before proceeding.

```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug --no-build
```
Must have 0 new failures. Pre-existing failures (2): `AllocationFreeTests`, `WhenNodePerfTests`.

---

## Task 5: Write report

Write a brief report to `.dev/_DONE/blueprint-dbg-1/reports/BATCH-E-CLEANUP-REPORT.md` containing:
- What was deleted/changed
- Build result (errors/warnings count)
- Test result (passed/failed/skipped counts, list of failures)
- Any issues encountered
