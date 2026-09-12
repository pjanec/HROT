# BATCH-QR-05 REPORT — Toolbar Compile/Reload dispatches by active-doc kind

**Date:** 2026-06-13
**Status:** ✅ DONE — build 0/0, tests 185/0

## Change summary

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (lines 3252–3270)

Three edits to the `blueprint.compileReload` `EditorCommandDescriptor` registration:

1. **Description** (line 3255): widened from `"Compile & hot-reload the active blueprint"` to `"Compile & hot-reload the active blueprint / BTree / HSM"`.

2. **IsEnabled** (lines 3258–3261): changed from `== AssetKind.Blueprint` to a pattern match enabling the command when `Active.Kind` is `Blueprint`, `BTree`, or `Hsm`:
   ```csharp
   IsEnabled: () => _aiDocumentManager?.Active?.Kind
       is Hrot.Editor.AiShared.AssetKind.Blueprint
       or Hrot.Editor.AiShared.AssetKind.BTree
       or Hrot.Editor.AiShared.AssetKind.Hsm
   ```

3. **Handler** (lines 3262–3270): replaced `_ => _blueprintCompileCallback?.Invoke()` with a `switch` dispatch on `_aiDocumentManager?.Active?.Kind`:
   ```csharp
   _ =>
   {
       switch (_aiDocumentManager?.Active?.Kind)
       {
           case Hrot.Editor.AiShared.AssetKind.Blueprint: _blueprintCompileCallback?.Invoke(); break;
           case Hrot.Editor.AiShared.AssetKind.BTree:     _btreeQuickReloadTrigger?.Invoke();  break;
           case Hrot.Editor.AiShared.AssetKind.Hsm:       _hsmQuickReloadTrigger?.Invoke();    break;
       }
   }
   ```

## Unchanged

- Command **Id:** `blueprint.compileReload`
- **IconKey:** `build/compile`
- **DisplayName:** `"Compile / Reload"`
- **ToolbarCommandAdapter** registration (`sortOrder: 50`)
- `blueprint.fullRebuild` command (unchanged, already global)

## Verification

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj → 0 Warning(s), 0 Error(s)
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj → Passed: 185, Failed: 0, Skipped: 0
```

No existing EditorSubsystem test hook exposes the compile command callback as a per-kind dispatch seam, so no new unit test was added. The dispatch correctness is verified by build-time type checking (the switch arm types are real `AssetKind` enum values) and the **REVIEW-QR** runtime gate.

## Remaining

**REVIEW-QR** *(lead runtime gate)* — edit a BTree and an HSM, hit Compile/Reload, confirm the change hot-swaps in the running sim; blueprint reload still works.
