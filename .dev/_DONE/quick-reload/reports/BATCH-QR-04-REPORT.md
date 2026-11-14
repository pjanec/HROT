# BATCH-QR-04-REPORT — HSM quick-reload trigger (`_hsmQuickReloadTrigger`)

**Date:** 2026-06-13 | **Branch:** `blueprint-integ-1` | **Files touched:** `EditorSubsystem.cs` only.

## Summary

Wired `Action _hsmQuickReloadTrigger` in `EditorSubsystem`, symmetric to the QR-03 `_btreeQuickReloadTrigger`, using the verified HSM emit pipeline.

## Changes (EditorSubsystem.cs only)

### 1. Usings added (3 new, after line 132)

```csharp
using Hrot.Hsm.Editor.Model;          // HsmAsset
using Hrot.Hsm.Editor.Persistence;    // HsmAssetMapper
using Hrot.AiEditor.Persistence.Hsm;  // HsmAssetDto
```

`Hrot.AiEditor.Persistence.Emit` was already present (QR-03) — covers `HsmEmitCore` and `HsmBridgeEmitCore`.

### 2. Field added (line 305)

```csharp
private Action? _hsmQuickReloadTrigger;
```

Placed directly after `_btreeQuickReloadTrigger`, with a comment referencing QR-04.

### 3. Trigger wiring (after BTree trigger block, line 3044)

```csharp
_hsmQuickReloadTrigger = () =>
{
    var ctx      = _aiDocumentManager?.Active?.ViewState as AiCanvasContext;
    var hsmAsset = ctx?.AssetRef as HsmAsset;
    if (hsmAsset == null) { _blueprintCompileStatus = "No active HSM document."; return; }

    var dto      = HsmAssetMapper.ToDto(hsmAsset);
    var topology = HsmEmitCore.EmitTopologyCore(dto);
    var bridge   = HsmBridgeEmitCore.EmitBridge(dto);

    var asmName = $"HsmPatch_{dto.AssetId:N}_{Guid.NewGuid():N}";
    var result = quickReloadService.TriggerFromSourcesAsync(
        new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
        asmName).GetAwaiter().GetResult();

    _blueprintCompileStatus = result.Succeeded
        ? $"Compiled HSM '{dto.Name}' in {result.DurationMs}ms"
        : $"HSM compile failed: {result.ErrorMessage}";
};
```

- `ctx.AssetRef as HsmAsset` — mirrors `HsmSelectionBridgeHelper.cs:142` pattern.
- Uses `EmitTopologyCore` + `EmitBridge` (NOT `HsmEmitCore.Emit`), matching the generator's `.g.cs` + `[BlueprintRegistrar]` bridge.
- Reuses `_blueprintCompileStatus` for status-line feedback.
- BTree trigger and all other code unchanged.

## Verified HSM member names

- **HsmAssetDto.AssetId** (`Guid`) — confirmed at `HsmAssetDto.cs:215`
- **HsmAssetDto.Name** (`string`) — confirmed at `HsmAssetDto.cs:216`
- **HsmAssetMapper.ToDto(HsmAsset)** → `HsmAssetDto` — confirmed at `HsmAssetMapper.cs:23`
- **HsmEmitCore.EmitTopologyCore(HsmAssetDto)** → `string` — confirmed at `HsmEmitCore.cs:40`
- **HsmBridgeEmitCore.EmitBridge(HsmAssetDto)** → `string` — confirmed at `HsmBridgeEmitCore.cs:35`
- **QuickReloadResult** record (`Succeeded`, `ErrorMessage`, `DurationMs`) — confirmed at `QuickReloadResult.cs:3-6`

## Build / Test

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj  →  0 Warning(s), 0 Error(s)
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj  →  Passed: 185, Failed: 0, Skipped: 0
```

Baseline preserved: 185/0.

## Definition of done

- [x] `_hsmQuickReloadTrigger` wired symmetric to BTree trigger
- [x] Build 0 warnings
- [x] `Hrot.Editor.Tests` 185 passed, 0 failed
- [x] Only `EditorSubsystem.cs` touched
- [x] BTree trigger unchanged

## REVIEW-QR runtime gate

**Not tested headlessly** — the actual HSM hot-swap in the running editor (open an HSM document, trigger compile/reload, confirm the change hot-swaps in the running sim) must be verified by the lead via REVIEW-QR. Toolbar dispatch wiring is QR-05.
