# BATCH-QR-03 — BTree quick-reload trigger (`_btreeQuickReloadTrigger`)  [RUNTIME GATE]

**Workstream:** quick-reload. **Model: pro (Zoo).** **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
**Restate & obey the Working Agreement** in `.dev/quick-reload/TASK-TRACKER.md`. Touch ONLY `EditorSubsystem.cs`
(+ an editor test file only if a tractable headless assertion is added). Depends on QR-02 (committed).

## Objective
Wire an `Action _btreeQuickReloadTrigger` in `EditorSubsystem` that hot-reloads the **active BTree document** in
process, mirroring `_blueprintQuickReloadTrigger`, using the verified pieces:
`active BehaviorTreeAsset → BehaviorTreeAssetMapper.ToDto → BTreeEmitCore.EmitTopologyCore + BTreeBridgeEmitCore.EmitBridge
→ quickReloadService.TriggerFromSourcesAsync(...)`.

## Where + how
1. Add a field near `_blueprintQuickReloadTrigger` (≈ line 293):
   `private Action? _btreeQuickReloadTrigger;`
2. Wire it **inside the same block where `_blueprintQuickReloadTrigger` is assigned** (≈ line 2988 — where the local
   `quickReloadService` is in scope; that block is guarded by blueprint-editor availability, which is correct: the
   quick-reload infra lives in Blueprints). Body:
   ```csharp
   _btreeQuickReloadTrigger = () =>
   {
       var ctx = _aiDocumentManager?.Active?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;
       var btAsset = ctx?.AssetRef as Hrot.BTree.Editor.Model.BehaviorTreeAsset;   // confirm namespace via BTreeSelectionBridgeHelper.cs:91
       if (btAsset == null) { _blueprintCompileStatus = "No active BTree document."; return; }

       var dto      = Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(btAsset);
       var topology = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitTopologyCore(dto);
       var bridge   = Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.EmitBridge(dto);

       var asmName = $"BTreePatch_{dto.AssetId:N}_{Guid.NewGuid():N}";
       var result = quickReloadService.TriggerFromSourcesAsync(
           new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
           asmName).GetAwaiter().GetResult();   // TriggerFromSourcesAsync is synchronous (Task.FromResult), mirror blueprint's .GetResult()

       _blueprintCompileStatus = result.Succeeded
           ? $"Compiled BTree '{dto.Name}' in {result.DurationMs}ms"
           : $"BTree compile failed: {result.ErrorMessage}";
   };
   ```
   Notes:
   - Use **`EmitTopologyCore`** (not `Emit`) + **`EmitBridge`** — this mirrors what the build-time `BTreeJsonGenerator`
     emits (the `.g.cs` topology + the `.Registrar.g.cs` `[BlueprintRegistrar]` bridge), which is the proven runnable
     output. (The full `BTreeEmitCore.Emit` includes editor layout + omits the bridge → would NOT self-register.)
   - Reuse the existing `_blueprintCompileStatus` field for the toolbar status line (shared compile status). Do NOT
     add a new status field/UI.
   - Confirm the exact `BehaviorTreeAsset` namespace and `dto.AssetId`/`dto.Name` member names by reading
     `BehaviorTreeAssetMapper.cs` + `BehaviorTreeAssetDto.cs`; adjust fully-qualified names to compile. Keep null-safe.
   - Do NOT change `_blueprintQuickReloadTrigger`, `TriggerFromSourcesAsync`, or any emit/mapper code.

## Verification
This is composition-root wiring (no unit-testable seam) + a **[RUNTIME GATE]** (the actual hot-swap is confirmed by
the lead at REVIEW-QR). So:
- Build must be 0 warnings and `Hrot.Editor.Tests` must stay `Failed: 0` (proves it compiles + doesn't regress).
- If a tractable headless assertion exists (e.g. an existing test that can call the emit pieces), you MAY add one that
  asserts `BTreeBridgeEmitCore.EmitBridge(dto)` for a minimal DTO contains `[BlueprintRegistrar]` and `EmitTopologyCore`
  is non-empty — but do NOT invent a heavy fake just to test the composition-root lambda.
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
Both `Failed: 0`; build 0 warnings.

## Definition of done
- `_btreeQuickReloadTrigger` wired (active BTree → ToDto → EmitTopologyCore+EmitBridge → TriggerFromSourcesAsync),
  status surfaced via `_blueprintCompileStatus`. Build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/quick-reload/reports/BATCH-QR-03-REPORT.md` (the wiring, the emit calls, the verified namespaces, build/
  test output; note the hot-swap is the REVIEW-QR runtime gate). The toolbar dispatch to this trigger is QR-05.

If anything can't be done as specified, STOP and write the blocker in the report.
