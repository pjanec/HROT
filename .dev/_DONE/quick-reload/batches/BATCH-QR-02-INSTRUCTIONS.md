# BATCH-QR-02 — `QuickReloadService.TriggerFromSourcesAsync` (generic source reload)

**Workstream:** quick-reload (PU-09/EB-E). **Model: pro (Zoo).** **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
**Restate & obey the Working Agreement** in `.dev/_DONE/quick-reload/TASK-TRACKER.md` (one task; no cheating; finish until
build 0 warnings + tests `Failed:0`; headless; tests assert real values; litter-free; report=truth; **no
codebase-memory tooling**). Touch ONLY the two files named below. Depends on QR-01 (multi-source `Compile` overload —
already committed).

## Objective
Extract the kind-agnostic part of `QuickReloadService.TriggerAsync(BlueprintAsset)` into a new public
`TriggerFromSourcesAsync(...)` that BTree/HSM (later batches) can call with already-emitted C# sources, then make
`TriggerAsync` a thin caller. **Blueprint reload behavior must be byte-for-byte identical.**

## File 1 — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`
Today `TriggerAsync(BlueprintAsset asset)` does: **Step 1** `BuildSiblingSignatures`, **Step 2** `_compiler.Compile(asset,
options)` → `result` (with `GeneratedSource`, `GeneratedFileName`, `BlueprintId`, `DebugMap`), then **Steps 2.5–7**:
Roslyn compile → collectible `AssemblyLoadContext` load → `HsmActionDispatcher.ClearAll()` → `BehaviorRegistry` +
`BlueprintRegistryStaging` staging via `BlueprintRegistrarScanner.Scan` → register `DebugMap` → `_coordinator.ApplyQuickReload`
(with rollback try/catch) → stopwatch/log → `QuickReloadResult`.

Add a new public method containing **exactly Steps 2.5–7** (moved verbatim, parameterized):
```csharp
public Task<QuickReloadResult> TriggerFromSourcesAsync(
    IReadOnlyList<(string Source, string VirtualPath)> sources,
    string assemblyName,
    BlueprintDebugMap? debugMap = null,
    System.Guid? assetIdForDebugMap = null)
```
- Use the QR-01 multi-source overload: `var roslynCompiler = new InMemoryRoslynCompiler(references); var (peBytes,
  pdbBytes) = roslynCompiler.Compile(sources, assemblyName, roslynSink);` (build `references` +
  `roslynSink = new DiagnosticSink()` the same way `TriggerAsync` does today). Keep the `roslynSink.HasErrors` →
  log + failure-`QuickReloadResult` branch.
- ALC: `new AssemblyLoadContext(assemblyName, isCollectible: true)`; `LoadFromStream(peStream, pdbStream)` — same as now.
- `HsmActionDispatcher.ClearAll();` then `BehaviorRegistry`/`BlueprintRegistryStaging` staging +
  `BlueprintRegistrarScanner.Scan(assembly, blueprintStaging, behaviorStaging)` — verbatim.
- If `debugMap != null`: `_session?.RegisterDebugMap(debugMap);` and on coordinator failure roll back with
  `assetIdForDebugMap` → `_session?.UnregisterDebugMap(assetIdForDebugMap.Value)` (mirror today's
  `_session?.UnregisterDebugMap(asset.AssetId)`; guard the `.Value`).
- `_coordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging);` inside the same try/catch.
- Stopwatch + `_outputConsole` logging + return `QuickReloadResult` — same shape as today.
- Keep the top-of-method `Stopwatch.StartNew()` + the "starting…" log either in the caller or here — your choice, but
  the **success/failure result semantics must match today**.

Then refactor `TriggerAsync(BlueprintAsset asset)` to: do Step 1 (`BuildSiblingSignatures`) + Step 2
(`_compiler.Compile(asset, options)`; on `!result.Succeeded` keep today's diagnostics-log + failure result), then
`return TriggerFromSourcesAsync(new[] { (result.GeneratedSource!, result.GeneratedFileName ?? "dynamic.cs") },
$"BlueprintPatch_{result.BlueprintId:X8}_{Guid.NewGuid():N}", result.DebugMap, asset.AssetId);`.
Keep `BuildSiblingSignatures` + `LastSignaturesUsedForTesting` UNCHANGED. Keep all `using`s; add any newly needed.

## File 2 — tests: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor.Tests` (the existing `QuickReloadService` test file)
- ALL existing `QuickReloadService` tests must pass **UNMODIFIED** (esp. those asserting `LastSignaturesUsedForTesting`
  and success/failure). This proves blueprint reload is unchanged.
- ADD one focused test for `TriggerFromSourcesAsync`: compile a trivial valid source (a minimal class; a
  `[BlueprintRegistrar]`-bearing source if a registrar type is reachable in the test refs, else any compiling source)
  → assert a SUCCESS `QuickReloadResult`; and a broken source → assert a FAILURE result (not a throw, unless the
  existing tests show throws). Mirror the existing tests' construction of `QuickReloadService` (catalog, editorState,
  outputConsole, compiler, coordinator, session) — reuse their fakes/fixtures; do not invent new infra.
  If `TriggerFromSourcesAsync` can't be meaningfully unit-tested with the existing fakes (e.g. coordinator needs a
  live registry), assert what IS reachable (success vs failure result) and note the runtime gate in the report — do
  NOT weaken or fake-pass.

## Build & test (no `BLUEPRINT_REGENERATE_SNAPSHOTS`)
```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor.Tests/Hrot.Blueprints.Editor.Tests.csproj --filter "FullyQualifiedName~QuickReload"
```
Both `Failed: 0`; build 0 warnings.

## Definition of done
- `TriggerFromSourcesAsync` added (Steps 2.5–7, parameterized, multi-source); `TriggerAsync` delegates to it after
  Steps 1–2; blueprint reload behavior identical (existing QuickReload tests unchanged + green); new source-reload
  test green. Build 0 warnings. Write `.dev/_DONE/quick-reload/reports/BATCH-QR-02-REPORT.md`.

If anything can't be done as specified, STOP and write the blocker in the report.
