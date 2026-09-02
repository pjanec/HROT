# Quick-Reload (PU-09 / EB-E) — Design of record

**Goal:** in-process **quick reload** for **BTree** and **HSM** assets (≤100 ms target), mirroring the existing
Blueprint `QuickReloadService`, so editing a BTree/HSM and hitting **Compile / Reload** hot-swaps the running
behavior without an MSBuild full rebuild.

**Origin:** deferred substrate item PU-09 (JSON DD §6.5) / EB-E (Forward-Plan §5). The sibling visual-editing thread
(`ai-hsm-btree-vis-edit-2`) explicitly deferred it (BT-07 "large/risky, lead-handled"); we own it here.

## Why it's feasible (de-risked)
The hot-reload machinery is **already general** and the emit path is **runtime-callable**:
- **Emit (runtime-callable):** `Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitTopologyCore(dto)` emits the
  `CreateBuilder()`/`[BTreeDefinition]` thunk; `BTreeBridgeEmitCore.EmitBridge(dto)` emits a **`[BlueprintRegistrar]`
  bridge class** (the "masquerade"). HSM has the symmetric `HsmEmitCore` / `HsmBridgeEmitCore`. The build-time Roslyn
  generators only *wrap* these static methods.
- **Reload machinery (general):** `QuickReloadService.TriggerAsync(BlueprintAsset)` already does
  `GeneratedSource → InMemoryRoslynCompiler → collectible ALC → BlueprintRegistrarScanner.Scan → AiHotReloadCoordinator.ApplyQuickReload`.
  The scanner finds `[BlueprintRegistrar]` classes and the coordinator commits behavior + blueprint staging and swaps
  the ALC atomically — it already clears `HsmActionDispatcher` and stages a `BehaviorRegistry`. A BTree/HSM-emitted
  assembly registers through the **same `[BlueprintRegistrar]` path** → no new registrar plumbing.

So BTree/HSM quick reload = the blueprint flow **minus** the AST-compile (`_compiler.Compile`) and the
sibling-signature steps (the EmitCore output *is* the C# source).

## Target flow (BTree; HSM symmetric)
```
active doc's in-memory BehaviorTreeAsset
  → (existing save-path mapper) BehaviorTreeAssetDto
  → BTreeEmitCore.EmitTopologyCore(dto)  +  BTreeBridgeEmitCore.EmitBridge(dto)   // two C# sources
  → QuickReloadService.TriggerFromSourcesAsync(sources, assetIdHash)             // NEW generic entry
       → InMemoryRoslynCompiler.Compile(multi-source)                            // NEW multi-source overload
       → collectible AssemblyLoadContext.LoadFromStream
       → BlueprintRegistrarScanner.Scan(asm, blueprintStaging, behaviorStaging)  // finds the [BlueprintRegistrar] bridge
       → AiHotReloadCoordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging)
```

## Key existing APIs (verified)
- `QuickReloadService.TriggerAsync(BlueprintAsset)` — `Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`.
  Steps 2.5–7 (Roslyn → ALC → scan → coordinator) are kind-agnostic and will be extracted into `TriggerFromSourcesAsync`.
- `InMemoryRoslynCompiler.Compile(string source, string virtualPath, string assemblyName, DiagnosticSink)` —
  `Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs`. **Single-source today** → needs a
  multi-source overload (BTree emits topology + bridge as two files).
- `AiHotReloadCoordinator.ApplyQuickReload(AssemblyLoadContext, BehaviorRegistry, BlueprintRegistryStaging)` —
  `internal` to `Hrot.Blueprints.Editor` (so the new generic entry must live IN `Hrot.Blueprints.Editor`, alongside
  `QuickReloadService` — no extraction needed).
- `BlueprintRegistrarScanner.Scan(assembly, blueprintStaging, behaviorStaging)`.
- Active doc asset: `_aiDocumentManager.Active.ViewState as AiCanvasContext`; `ctx.AssetRef` is the runtime asset
  (`BehaviorTreeAsset` for BTree — see `BTreeDocumentFactory.cs:157` / `BTreeSelectionBridgeHelper.cs:91`; the HSM
  equivalent is the HSM asset). The save path's **asset→DTO** conversion is reused to get the DTO for emit.
- Emit: `Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs` (`EmitTopologyCore`), `BTreeBridgeEmitCore.cs`
  (`EmitBridge`); HSM `HsmEmitCore` / `HsmBridgeEmitCore`.
- Toolbar command: `blueprint.compileReload` (BATCH-56, `EditorSubsystem`) — currently dispatches
  `_blueprintCompileCallback`, enabled only for Blueprint; widen to BTree/HSM.

## Constraints / risks
- The new generic reload entry MUST live in `Hrot.Blueprints.Editor` (the coordinator is `internal` there). BTree/HSM
  triggers are wired in `EditorSubsystem` (which references `Hrot.Blueprints.Editor`, `*.Persistence.Emit`, and the
  BTree/HSM editor assemblies) — same place `_blueprintQuickReloadTrigger` lives.
- Hot-reload correctness (ALC swap, registrar scan, dispatcher clears) is **runtime-only** → each batch is
  headless-built + unit-tested where possible; the actual hot-swap is a **lead runtime gate** (REVIEW-QR).
- Do NOT regress blueprint quick reload (TriggerAsync must behave identically after the refactor).
- No new auto-recompile-on-edit; reload stays user-triggered via the toolbar (mirrors blueprint).
