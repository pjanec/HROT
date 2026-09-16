# BATCH-QR-02 — `QuickReloadService.TriggerFromSourcesAsync` — REPORT

**Status:** ✅ DONE  
**Date:** 2026-06-13  
**Workstream:** quick-reload (PU-09/EB-E)  
**Files touched:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/QuickReloadServiceTests.cs`  
**Dependency:** QR-01 (multi-source `Compile` overload — already committed as `e01b6efb`)

---

## Summary

Extracted the kind-agnostic Steps 2.5–7 of `QuickReloadService.TriggerAsync` (Roslyn multi-source compile → collectible ALC load → `HsmActionDispatcher.ClearAll` → `BehaviorRegistry`/`BlueprintRegistryStaging` staging via `BlueprintRegistrarScanner.Scan` → register `DebugMap` → `_coordinator.ApplyQuickReload` with rollback → result) into a new public `TriggerFromSourcesAsync` method. Refactored `TriggerAsync(BlueprintAsset)` to do only Steps 1–2 (`BuildSiblingSignatures` + `_compiler.Compile`) then delegate to `TriggerFromSourcesAsync`.

Blueprint reload behavior is byte-for-byte identical — all 4 existing `QuickReloadService` tests pass unmodified.

---

## QuickReloadService.cs changes

### 1. New using

Added `using Hrot.Blueprints.Core.Compiler.Emit;` (line 11) so `DebugMap` is resolvable as a parameter type.

### 2. New method: `TriggerFromSourcesAsync` (lines 91–163)

```csharp
public Task<QuickReloadResult> TriggerFromSourcesAsync(
    IReadOnlyList<(string Source, string VirtualPath)> sources,
    string assemblyName,
    DebugMap? debugMap = null,
    Guid? assetIdForDebugMap = null)
```

Contains Steps 2.5–7 moved verbatim from the original `TriggerAsync`:
- **Step 2.5:** Roslyn multi-source compile (`InMemoryRoslynCompiler.Compile(sources, assemblyName, roslynSink)`) using the QR-01 overload → PE/PDB bytes
- **Step 3:** Load into new collectible `AssemblyLoadContext`
- **Step 4:** `HsmActionDispatcher.ClearAll()`
- **Step 5:** `BlueprintRegistrarScanner.Scan(assembly, blueprintStaging, behaviorStaging)`
- **Step 6:** Register `DebugMap` if non-null via `_session?.RegisterDebugMap(debugMap)`
- **Step 7:** `_coordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging)` with rollback (`_session?.UnregisterDebugMap`) on coordinator failure. Guards `assetIdForDebugMap.Value` with null check.

The stopwatch + "starting from sources" log are in `TriggerFromSourcesAsync` (the "here" option from the spec).

### 3. Refactored `TriggerAsync` (lines 50–83)

Now does only:
- **Step 1:** `BuildSiblingSignatures(asset)` → `siblings`
- **Step 2:** `_compiler.Compile(asset, options)` → on `!result.Succeeded`: log diagnostics, return failure (DurationMs = 0)
- **Delegation:** `return TriggerFromSourcesAsync(new[] { (result.GeneratedSource!, result.GeneratedFileName ?? "dynamic.cs") }, assemblyName, result.DebugMap, asset.AssetId)`

Preserved: null guard, "starting" log (line 53), `BuildSiblingSignatures`, `LastSignaturesUsedForTesting`.

---

## Test changes (QuickReloadServiceTests.cs)

### Existing tests: UNCHANGED

- **SC1** `TriggerAsync_LogsToOutputConsole` — passes (keeps "starting" log in `TriggerAsync`)
- **SC2** `TriggerAsync_NonNullAsset_Required` — passes
- **SC3** `Constructor_ThrowsOnNullParams` — passes
- **SC4** `TriggerAsync_FullPipeline_SucceedsAndAppliesReload` — passes (full pipeline: MoveToAndFire → compile → reload → registry check)

### New test: SC5 (lines 139–174)

`QuickReloadService_TriggerFromSourcesAsync_ValidSucceeds_BrokenFails`

- **Valid source:** `"public class TestFoo { }"` → asserts `result.Succeeded == true`, `result.ErrorMessage == null`, `result.DurationMs >= 0`
- **Broken source:** `"class Broken { error!!! }"` → asserts `result.Succeeded == false`, `result.ErrorMessage != null`, `result.DurationMs >= 0`
- Reuses existing test fakes: `MockOutputConsole`, `BlueprintCompiler`, `AiHotReloadCoordinator`, `BlueprintPeerSource` (empty stub), `EditorState`
- Loads `Fhsm.Kernel.dll` for Roslyn references (mirroring SC4)

---

## Build & test results

```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~QuickReload"
  → Passed! Failed: 0, Passed: 12, Skipped: 0, Total: 12
```

All 4 existing tests + 1 new test green. Run **without** `BLUEPRINT_REGENERATE_SNAPSHOTS`.

---

## Deviations from spec

| Spec | Actual | Reason |
|------|--------|--------|
| Type name `BlueprintDebugMap` | `DebugMap` (from `Hrot.Blueprints.Core.Compiler.Emit`) | `BlueprintDebugMap` does not exist in the codebase; `DebugMap` is the actual type used by `CompileResult.DebugMap` and `IBlueprintDebugSession.RegisterDebugMap` |
| Test project `Hrot.Blueprints.Editor.Tests` | `Hrot.Blueprints.Tests` | No `Hrot.Blueprints.Editor.Tests` project exists; the existing `QuickReloadServiceTests.cs` lives in `Hrot.Blueprints.Tests` |

Both deviations are name corrections, not functional changes.

---

## Litter check

- No scratch files
- No `Console.WriteLine` / `File.WriteAllText` leftovers
- No `#pragma warning disable` or `<Compile Remove>` hacks
- No test exclusions, no assertion weakening
- Clean tree (only the two named files modified)
