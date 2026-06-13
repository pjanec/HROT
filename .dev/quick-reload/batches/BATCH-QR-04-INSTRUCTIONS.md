# BATCH-QR-04 — HSM quick-reload trigger (`_hsmQuickReloadTrigger`)  [RUNTIME GATE]

**Workstream:** quick-reload. **Model: pro (Zoo).** **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
**Restate & obey the Working Agreement** in `.dev/quick-reload/TASK-TRACKER.md`. Touch ONLY `EditorSubsystem.cs`.
Depends on QR-02 (committed). **Exactly symmetric to QR-03** (BTree), already committed — mirror it for HSM.

## Objective
Wire `Action _hsmQuickReloadTrigger` in `EditorSubsystem` that hot-reloads the **active HSM document** in process,
using the verified HSM pieces (confirmed present):
`active HsmAsset → HsmAssetMapper.ToDto → HsmEmitCore.EmitTopologyCore + HsmBridgeEmitCore.EmitBridge → quickReloadService.TriggerFromSourcesAsync`.

## Where + how (mirror the QR-03 `_btreeQuickReloadTrigger` block)
1. Add a field near `_btreeQuickReloadTrigger`: `private Action? _hsmQuickReloadTrigger;`
2. Add the needed usings if not already present: `Hrot.Hsm.Editor.Model`, `Hrot.Hsm.Editor.Persistence`,
   `Hrot.AiEditor.Persistence.Hsm` (the `Hrot.AiEditor.Persistence.Emit` using is already added by QR-03).
3. Wire it in the **same block** as `_btreeQuickReloadTrigger` (where `quickReloadService` is in scope):
   ```csharp
   _hsmQuickReloadTrigger = () =>
   {
       var ctx      = _aiDocumentManager?.Active?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;
       var hsmAsset = ctx?.AssetRef as Hrot.Hsm.Editor.Model.HsmAsset;   // cf. HsmSelectionBridgeHelper.cs:142
       if (hsmAsset == null) { _blueprintCompileStatus = "No active HSM document."; return; }

       var dto      = Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsmAsset);
       var topology = Hrot.AiEditor.Persistence.Emit.HsmEmitCore.EmitTopologyCore(dto);
       var bridge   = Hrot.AiEditor.Persistence.Emit.HsmBridgeEmitCore.EmitBridge(dto);

       var asmName = $"HsmPatch_{dto.AssetId:N}_{Guid.NewGuid():N}";
       var result = quickReloadService.TriggerFromSourcesAsync(
           new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
           asmName).GetAwaiter().GetResult();

       _blueprintCompileStatus = result.Succeeded
           ? $"Compiled HSM '{dto.Name}' in {result.DurationMs}ms"
           : $"HSM compile failed: {result.ErrorMessage}";
   };
   ```
   Notes:
   - Use **`HsmEmitCore.EmitTopologyCore`** + **`HsmBridgeEmitCore.EmitBridge`** (NOT `HsmEmitCore.Emit`) — the bridge
     is the `[BlueprintRegistrar]` self-registration that lets the coordinator pick it up; topology-core matches the
     generator's runtime `.g.cs` (no editor layout).
   - Confirm `HsmAssetDto`'s `AssetId`/`Name` member names by reading `HsmAssetDto.cs`; adjust to compile. Null-safe.
   - Reuse `_blueprintCompileStatus` for the status line. Do NOT change QR-03's BTree trigger or anything else.

## Verification (composition-root wiring + RUNTIME GATE)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
Both `Failed: 0`; build 0 warnings. (Actual HSM hot-swap = REVIEW-QR lead runtime gate.)

## Definition of done
- `_hsmQuickReloadTrigger` wired symmetric to BTree; build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/quick-reload/reports/BATCH-QR-04-REPORT.md` (the wiring, verified HSM member names, build/test output;
  note the REVIEW-QR runtime gate). Toolbar dispatch is QR-05.

If anything can't be done as specified, STOP and write the blocker in the report.
