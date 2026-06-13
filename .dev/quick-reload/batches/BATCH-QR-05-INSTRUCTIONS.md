# BATCH-QR-05 — Toolbar Compile/Reload dispatches by active-doc kind (Blueprint/BTree/HSM)

**Workstream:** quick-reload. **Model: pro (Zoo).** **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
**Restate & obey the Working Agreement** in `.dev/quick-reload/TASK-TRACKER.md`. Touch ONLY `EditorSubsystem.cs`.
Depends on QR-03 (`_btreeQuickReloadTrigger`) + QR-04 (`_hsmQuickReloadTrigger`) — both committed.

## Objective
The `blueprint.compileReload` toolbar command (added in BATCH-56) is enabled only for Blueprint and invokes
`_blueprintCompileCallback`. Widen it to **all three** in-memory-reloadable kinds and dispatch to the right trigger by
the active document's kind, so Compile/Reload works in the Blueprint, BTree, and HSM perspectives.

## Change (in the `blueprint.compileReload` registration — search `"blueprint.compileReload"` in EditorSubsystem.cs)
1. **`IsEnabled`** — change from `== AssetKind.Blueprint` to enable for all three kinds:
   ```csharp
   IsEnabled: () => _aiDocumentManager?.Active?.Kind
       is Hrot.Editor.AiShared.AssetKind.Blueprint
       or Hrot.Editor.AiShared.AssetKind.BTree
       or Hrot.Editor.AiShared.AssetKind.Hsm
   ```
2. **Handler** — replace `_ => _blueprintCompileCallback?.Invoke()` with a kind dispatch:
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
3. Leave the command **Id** (`blueprint.compileReload`), **IconKey** (`build/compile`), and the
   `ToolbarCommandAdapter.Register(... sortOrder: 50)` entry UNCHANGED (avoid churn). The `DisplayName` is already the
   generic "Compile / Reload" — leave it. `blueprint.fullRebuild` is unchanged (already global).
   (Optional, only if it reads cleanly: update the command `Description` to "Compile & hot-reload the active blueprint
   / BTree / HSM" — cosmetic; skip if it risks touching unrelated lines.)

Do NOT change the triggers themselves, the Full Rebuild command, or the icon registration.

## Verification
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
Both `Failed: 0`; build 0 warnings. If an `EditorSubsystem` test hook exposes the compile command/callback and a
per-kind dispatch is unit-reachable, add a small test (active-doc kind → correct trigger selected via a fake/seam);
otherwise headless build + the REVIEW-QR runtime gate.

## Definition of done
- Compile/Reload is enabled when the active doc is Blueprint, BTree, or HSM, and dispatches to the matching trigger.
  Build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/quick-reload/reports/BATCH-QR-05-REPORT.md`. After this, the only remaining item is **REVIEW-QR**
  (lead runtime gate: edit a BTree + an HSM, Compile/Reload, confirm in-process hot-swap; blueprint reload still works).

If anything can't be done as specified, STOP and write the blocker in the report.
