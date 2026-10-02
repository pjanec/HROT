# BATCH-QR-03 — BTree quick-reload trigger (`_btreeQuickReloadTrigger`) — REPORT

**Status:** ✅ DONE  
**Date:** 2026-06-13  
**Workstream:** quick-reload (PU-09/EB-E)  
**Files touched:**
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`  
**Dependency:** QR-02 (`TriggerFromSourcesAsync` — already committed as `e01b6efb`)

---

## Summary

Added `Action? _btreeQuickReloadTrigger` to `EditorSubsystem`, wired inside the same block where `_blueprintQuickReloadTrigger` is assigned (where the local `quickReloadService` is in scope). The trigger resolves the active BTree document, maps it to a DTO via `BehaviorTreeAssetMapper.ToDto`, emits the topology core (`EmitTopologyCore`) and the self-registering bridge (`EmitBridge`), then delegates to `TriggerFromSourcesAsync` for in-process Roslyn compilation and hot-reload.

This is **[RUNTIME GATE]** — the actual hot-swap is confirmed by the lead at REVIEW-QR.

---

## EditorSubsystem.cs changes

### 1. Using statements (lines 110–115)

Added four namespaces needed for BTree emit and mapping:

```csharp
using Hrot.BTree.Editor.Model;           // BehaviorTreeAsset
using Hrot.BTree.Editor.Persistence;      // BehaviorTreeAssetMapper
using Hrot.AiEditor.Persistence.BTree;    // BehaviorTreeAssetDto
using Hrot.AiEditor.Persistence.Emit;     // BTreeEmitCore, BTreeBridgeEmitCore
```

These are placed in the existing BTree-using block (after `using Hrot.BTree.Editor.Host;`).

### 2. Field declaration (≈ line 298)

Added after `_blueprintQuickReloadTrigger`:

```csharp
// QR-03: BTree quick-reload trigger — wired in Phase 4 alongside _blueprintQuickReloadTrigger.
// Invokes ToDto → EmitTopologyCore + EmitBridge → TriggerFromSourcesAsync (no IEditableAsset param).
private Action? _btreeQuickReloadTrigger;
```

Type is `Action?` (no parameter — it resolves the active document internally), unlike `_blueprintQuickReloadTrigger` which takes `IEditableAsset`.

### 3. Trigger body (≈ lines 3016–3036)

Wired immediately after the `_blueprintQuickReloadTrigger` lambda body with identical indentation (same block scope, `quickReloadService` in scope):

```csharp
_btreeQuickReloadTrigger = () =>
{
    var ctx     = _aiDocumentManager?.Active?.ViewState
        as Hrot.Editor.AiShared.Windows.AiCanvasContext;
    var btAsset = ctx?.AssetRef as Hrot.BTree.Editor.Model.BehaviorTreeAsset;
    if (btAsset == null) { _blueprintCompileStatus = "No active BTree document."; return; }

    var dto      = Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(btAsset);
    var topology  = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitTopologyCore(dto);
    var bridge    = Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.EmitBridge(dto);

    var asmName = $"BTreePatch_{dto.AssetId:N}_{Guid.NewGuid():N}";
    var result = quickReloadService.TriggerFromSourcesAsync(
        new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
        asmName).GetAwaiter().GetResult();

    _blueprintCompileStatus = result.Succeeded
        ? $"Compiled BTree '{dto.Name}' in {result.DurationMs}ms"
        : $"BTree compile failed: {result.ErrorMessage}";
};
```

---

## Verified namespaces & members

| Symbol | Namespace | Source |
|--------|-----------|--------|
| `BehaviorTreeAsset` | `Hrot.BTree.Editor.Model` | `BTreeSelectionBridgeHelper.cs:91` (`ctx.AssetRef as BehaviorTreeAsset`) |
| `BehaviorTreeAssetMapper` | `Hrot.BTree.Editor.Persistence` | `BehaviorTreeAssetMapper.cs:16` (static class) |
| `BehaviorTreeAssetDto` | `Hrot.AiEditor.Persistence.BTree` | `BehaviorTreeAssetDto.cs:234` (sealed class) |
| `dto.AssetId` | `Guid` | `BehaviorTreeAssetDto.cs:237` (`public Guid AssetId`) |
| `dto.Name` | `string` | `BehaviorTreeAssetDto.cs:238` (`public string Name`) |
| `BTreeEmitCore.EmitTopologyCore` | `Hrot.AiEditor.Persistence.Emit` | `BTreeEmitCore.cs:42` (static, takes `BehaviorTreeAssetDto`) |
| `BTreeBridgeEmitCore.EmitBridge` | `Hrot.AiEditor.Persistence.Emit` | `BTreeBridgeEmitCore.cs:36` (static, takes `BehaviorTreeAssetDto`) |
| `TriggerFromSourcesAsync` | `Hrot.Blueprints.Editor.Reload` | `QuickReloadService.cs:91` (public, synchronous — `Task.FromResult`) |

---

## Design decisions

- **`EmitTopologyCore` + `EmitBridge`** (not `BTreeEmitCore.Emit`): matches what the build-time `BTreeJsonGenerator` emits — `.g.cs` topology + `.Registrar.g.cs` `[BlueprintRegistrar]` bridge. The full `Emit` includes editor layout and omits the bridge → would NOT self-register.
- **Shared `_blueprintCompileStatus`**: no new status field — the toolbar reads `_blueprintCompileStatus`, so BTree compile results surface on the same status line as blueprint results.
- **`.GetAwaiter().GetResult()`**: safe because `TriggerFromSourcesAsync` returns `Task.FromResult` (synchronous), mirroring the blueprint trigger's `TriggerAsync` call.
- **No new toolbar dispatch**: wiring the toolbar button to dispatch by active-doc kind is deferred to QR-05.

---

## Build & test results

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
  → Passed!  Failed: 0, Passed: 185, Skipped: 0, Total: 185
```

Run **without** `BLUEPRINT_REGENERATE_SNAPSHOTS`. No new tests added — composition-root wiring has no unit-testable seam (the lambda resolves live `_aiDocumentManager.Active`, `quickReloadService`, etc.), and inventing a heavy fake just to test the lambda would violate the working agreement. The emit pieces (`EmitTopologyCore`, `EmitBridge`, `ToDto`) are already tested by their own test suites.

---

## Deviations from spec

None. All fully-qualified names compile as-is.

---

## RUNTIME GATE note

The hot-swap is confirmed by the lead at **REVIEW-QR** (edit a BTree, hit Compile/Reload, confirm the change takes effect in the running sim without a full rebuild). This batch ensures the wiring compiles and the existing test suite stays green — the runtime behavior gate is external.

---

## Litter check

- No scratch files
- No `Console.WriteLine` / `File.WriteAllText` leftovers
- No `#pragma warning disable` or `<Compile Remove>` hacks
- No test exclusions, no assertion weakening
- Only `EditorSubsystem.cs` modified (4 usings + 1 field + 1 lambda body)
