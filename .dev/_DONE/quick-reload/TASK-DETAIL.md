# Quick-Reload — Task Detail

All tasks follow the [Working Agreement](./TASK-TRACKER.md#working-agreement--implementing-agent-zoo). Design context:
[DESIGN.md](./DESIGN.md). Run all builds/tests WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`.

---

## QR-01 — `InMemoryRoslynCompiler` multi-source overload
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs`
(+ its test project `Hrot.Blueprints.Compiler.Tests` if one exists, else the closest existing Roslyn-compiler test).
**Deliverable:** an overload that compiles **multiple** C# source files into ONE assembly.

Today `Compile(string source, string virtualSourcePath, string assemblyName, DiagnosticSink)` parses a single
`CSharpSyntaxTree`. Add:
```csharp
public (byte[] Pe, byte[] Pdb) Compile(
    IReadOnlyList<(string Source, string VirtualPath)> sources,
    string assemblyName,
    DiagnosticSink sink)
```
that parses each `(Source, VirtualPath)` into its own `CSharpSyntaxTree` (same parse options/encoding as the existing
single-source path — reuse them) and builds one `CSharpCompilation` over all trees with the same references, options,
PE/PDB emit, and diagnostic handling as the existing method. **Refactor the existing single-source `Compile` to
delegate to the new one** (wrap the single source in a one-element list) so behavior is shared and identical.
Keep `CompileAndLoad` working (single-source) — optionally add a multi-source `CompileAndLoad` if trivial, else leave.

**Tests:** add a test that compiles TWO trivial sources (e.g. two classes in two namespaces, where class B references
class A) into one assembly and asserts both types are present (`asm.GetType(...)` non-null) and `!sink.HasErrors`.
Assert a deliberately-broken second source produces errors. Keep existing single-source tests green.

**Build/test:**
```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj
dotnet test  <the Roslyn-compiler / Blueprints.Compiler test project>   # Failed: 0
```
**Done:** multi-source compile works + tested; single-source path unchanged; 0 warnings.

---

## QR-02 — `QuickReloadService.TriggerFromSourcesAsync` (generic source reload)
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs` (+ `Hrot.Blueprints.Editor.Tests`).
**Deliverable:** a kind-agnostic reload entry that takes already-emitted C# sources and runs the
compile→ALC→scan→coordinator pipeline; `TriggerAsync(BlueprintAsset)` becomes a thin caller.

Add:
```csharp
public Task<QuickReloadResult> TriggerFromSourcesAsync(
    IReadOnlyList<(string Source, string VirtualPath)> sources,
    string assemblyName,
    BlueprintDebugMap? debugMap = null,
    System.Guid? assetIdForDebugMap = null)
```
Move the existing `TriggerAsync` **Steps 2.5–7** into it verbatim (Roslyn compile via the QR-01 multi-source overload;
collectible `AssemblyLoadContext` load from PE/PDB; `HsmActionDispatcher.ClearAll()`; `BehaviorRegistry` +
`BlueprintRegistryStaging` staging; `BlueprintRegistrarScanner.Scan`; register `debugMap` if non-null;
`_coordinator.ApplyQuickReload(...)` with the same rollback try/catch; stopwatch/log/result). Then refactor
`TriggerAsync(BlueprintAsset)` to do its Steps 1–2 (`BuildSiblingSignatures` + `_compiler.Compile` → `GeneratedSource`
+ `DebugMap`) and then call `TriggerFromSourcesAsync([(GeneratedSource, GeneratedFileName)], assemblyName,
result.DebugMap, asset.AssetId)`.

**Hard requirement — blueprint reload unchanged:** the existing `Hrot.Blueprints.Editor.Tests` for `QuickReloadService`
(esp. `LastSignaturesUsedForTesting`, success/failure paths) must pass UNMODIFIED. Add a focused test for
`TriggerFromSourcesAsync` (e.g. a trivial `[BlueprintRegistrar]`-bearing source compiles, loads, and the coordinator
receives a non-empty staging — or, if coordinator interaction isn't unit-reachable, assert a successful
`QuickReloadResult` for valid sources and a failure result for broken sources).

**Build/test:**
```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor.Tests/...csproj   # Failed: 0 (existing QR tests UNCHANGED + pass)
```
**Done:** generic entry added; `TriggerAsync` delegates; blueprint reload behavior identical; 0 warnings.

---

## QR-03 — BTree quick-reload trigger (`_btreeQuickReloadTrigger`)  **[RUNTIME GATE]**
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (mirror the `_blueprintQuickReloadTrigger` wiring,
≈ line 2988). **Investigate + reuse — do not reinvent serialization.**
**Deliverable:** an `Action` field `_btreeQuickReloadTrigger` that hot-reloads the active BTree document.

Steps:
1. Resolve the active BTree asset: `_aiDocumentManager?.Active?.ViewState as AiCanvasContext`; `ctx?.AssetRef as
   Hrot.<…>.BehaviorTreeAsset` (confirm the type — `BTreeSelectionBridgeHelper.cs:91` casts `ctx.AssetRef as
   BehaviorTreeAsset`). If null → set a status string and return.
2. Convert the in-memory `BehaviorTreeAsset` → `BehaviorTreeAssetDto` using **the same conversion the BTree SAVE path
   uses** (find the BTree save delegate wired in `EditorSubsystem` / the mapper in `Hrot.AiEditor.Persistence/BTree`
   or `Hrot.BTree.Editor`; do NOT hand-roll a new mapping). 
3. `var topology = BTreeEmitCore.EmitTopologyCore(dto); var bridge = BTreeBridgeEmitCore.EmitBridge(dto);`
   (namespaces: `Hrot.AiEditor.Persistence.Emit`).
4. `quickReloadService.TriggerFromSourcesAsync(new[]{ (topology, dto.Name+".g.cs"), (bridge, dto.Name+".Registrar.g.cs") },
   assemblyName: $"BTreePatch_{<assetIdHash>}_{Guid.NewGuid():N}")` (mirror the blueprint assembly-name shape). Capture
   the result into a status string like the blueprint path (`_blueprintCompileStatus` analog or reuse it).
5. Wire it where `_blueprintQuickReloadTrigger` is wired (inside the same blueprint-editor-available block, where
   `quickReloadService` is in scope). Null-safe.

**Tests:** the conversion+emit step should be headless-testable — add a test (in the most appropriate existing editor
test project) that takes a known small `BehaviorTreeAssetDto`, runs `EmitTopologyCore`+`EmitBridge`, and feeds them to
`TriggerFromSourcesAsync` (or just asserts both sources are non-empty + contain the `[BlueprintRegistrar]` bridge for
the bridge source). The actual hot-swap is the **lead RUNTIME GATE** (REVIEW-QR) — note it in the report.

**Build/test:**
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj   # Failed: 0
```
**Done:** `_btreeQuickReloadTrigger` wired; builds 0 warnings; tests green. (Hot-swap confirmed at REVIEW-QR.)

---

## QR-04 — HSM quick-reload trigger (`_hsmQuickReloadTrigger`)  **[RUNTIME GATE]**
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`. **Symmetric to QR-03.**
Active HSM asset → `HsmAssetDto` (same save-path conversion) → `HsmEmitCore.Emit*(dto)` + `HsmBridgeEmitCore.EmitBridge(dto)`
→ `TriggerFromSourcesAsync(..., assemblyName: $"HsmPatch_…")`. Confirm the exact HSM emit-core method names
(`HsmEmitCore` may expose `Emit`/`EmitTopologyCore`) and that `HsmBridgeEmitCore.EmitBridge` emits a
`[BlueprintRegistrar]` bridge (mirror BTree). Wire `_hsmQuickReloadTrigger` alongside the others. Same test pattern +
RUNTIME GATE note.

**Build/test:** same as QR-03.
**Done:** `_hsmQuickReloadTrigger` wired; builds 0 warnings; tests green.

---

## QR-05 — Widen Compile/Reload toolbar dispatch to BTree/HSM
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (the `blueprint.compileReload` command from BATCH-56).
**Deliverable:** the toolbar Compile/Reload works in Blueprint, BTree, and HSM perspectives, dispatching to the right
trigger by active-doc kind.
- `IsEnabled`: `_aiDocumentManager?.Active?.Kind is AssetKind.Blueprint or AssetKind.BTree or AssetKind.Hsm`.
- Handler: switch on `_aiDocumentManager.Active.Kind` → Blueprint→`_blueprintCompileCallback`,
  BTree→`_btreeQuickReloadTrigger`, Hsm→`_hsmQuickReloadTrigger` (all `?.Invoke()`).
- Keep the command id (`blueprint.compileReload`) and icon as-is to avoid churn, OR rename to a generic id if trivial
  and update the BATCH-56 toolbar registration + any reference. (Prefer keeping the id; just generalize behavior.)
- Full Rebuild (`blueprint.fullRebuild`) unchanged (already global).

**Tests:** if `EditorSubsystem` test hooks expose the compile callback, add/extend a test that the dispatch selects the
correct trigger per active-doc kind (mock/fake the doc manager kind). Else headless build + the RUNTIME GATE.

**Build/test:** build `Hrot.Editor`; `Hrot.Editor.Tests` Failed: 0; 0 warnings.
**Done:** Compile/Reload enabled + correct dispatch for all three kinds.
