# BATCH-23: Retire Blueprints AssetBrowserWindow + FileSystemAssetCatalog
**Tasks:** MTB-P7-T5   **Phase:** 7 — final task   **Est:** ~7h
**Dependencies:** BATCH-10 (`BlueprintAssetContributor.BaseFolder`), BATCH-22 (docked host registered).

> AUTHORIZED deletion (ORCH §2.5). This is the riskiest retirement — `FileSystemAssetCatalog` also
> backs the peer-blueprint-signature lookup (NOT just the browser). Read DEC-13 first.

## ⚠ CORRECTIVE — round 2 (dev-lead review found TWO real regressions; the "all pre-existing" claim was WRONG)
A Stability-filtered `Hrot.Blueprints.Tests` run shows **20 failures, not the required 9**. Beyond the
9 PRE-1, the dev-lead verified against pre-retirement baseline `f24659de`:
- **(A) QuickReload — REGRESSION introduced by THIS batch's catalog swap.** These 3 tests PASSED at
  `f24659de` and now **FAIL DETERMINISTICALLY IN ISOLATION** (not flaky — verified, each ~16s):
  `QuickReloadServiceTests.QuickReloadService_TriggerAsync_FullPipeline_SucceedsAndAppliesReload`,
  `BlueprintCompileOnDemandMveTests.QuickReload_FullPipeline_CompiledBlueprint_AttachesAndRunsOnEntity`,
  `BlueprintCompileOnDemandMveTests.QuickReload_InstanceBlueprint_RegistersIntoSharedRegistry`.
  → Your `FileSystemAssetCatalog`→`BlueprintPeerSource` swap (and the `qrsCatalog` repoint at
  EditorSubsystem ~L2691 + `QuickReloadService.BuildSiblingSignatures`) broke quick-reload. **FIX the
  salvage** so quick-reload resolves sibling signatures exactly as before. Verify all 3 pass IN
  ISOLATION and in the full suite. Do NOT mislabel as flaky; they fail in isolation.
- **(B) `EditorSubsystemBlueprintWindowsTests` (8 tests) — pre-existing relative to THIS batch (fail at
  `f24659de` too) but introduced earlier in the project and MUST be fixed now for the gate.** Root
  cause: the test does `new EditorSubsystem(); subsystem.RegisterWindows(wm);` (no DI/services) and
  `RegisterWindows` now **throws** (the scenario-menu/docked-host wiring added in BATCH-21/22
  dereferences services that are null in this bare path — icon provider / `_aiDocumentManager` /
  `INewAssetService` registry). Make the new wiring **null-safe** so `RegisterWindows` completes and
  still registers the 18 per-perspective windows + the browser/docked host (register what it can; skip
  optional callbacks when their services are null). Do NOT weaken these tests; they assert core window
  registration. The 8 failing tests:
  `EditorSubsystem_RegisterWindows_RegistersThreePerspectives_AndGlobalBrowser`,
  `…_RegistersVariablesWindow_ForBlueprint`, `…_RegistersDetailsWindow_ForBlueprint`,
  `…_RegistersMyBlueprintWindow_ForBlueprint`, `…_RegistersCanvasWindows_ForBTreeAndHsm`,
  `…_BlueprintWindows_HaveOwningPerspective_Blueprint`, `…_BTreeWindows_HaveOwningPerspective_BTree`,
  `…_HsmWindows_HaveOwningPerspective_HSM`. (Note: the "GlobalBrowser" the test expects is now the
  docked host `AssetBrowserDockedWindow` id `"AssetBrowser"` — if a test asserts a specific browser id,
  update it to the docked-host id; that is tracking a renamed window, NOT weakening.)

**Corrective DoD:** Stability-filtered `Hrot.Blueprints.Tests` must show **EXACTLY the 9 PRE-1
failures** — the 8 window tests AND the 3 quick-reload tests must all be GREEN. Run the 3 quick-reload
tests in isolation to prove they pass. List the final filtered failing set (must == 9 PRE-1).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §10.6 (retirement).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P7-T5. **DEC-13 in DEBT-TRACKER.md** (peer-catalog salvage).
4. Targets + consumers (read):
   - **Delete:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/AssetBrowserWindow.cs`;
     `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/FileSystemAssetCatalog.cs`;
     `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IAssetCatalog.cs` (the Blueprints `IAssetCatalog`
     + `AssetCatalogEntry`).
   - Consumers of the Blueprints `IAssetCatalog`/`FileSystemAssetCatalog`:
     - **Browser (delete):** `BlueprintWindowRegistrar.cs:50` (creates `AssetBrowserWindow`),
       `BlueprintEditorModule.cs` (holds `IAssetCatalog`), DI
       `BlueprintEditorServiceCollectionExtensions.cs:15/29`.
     - **Peer-signature (MUST keep working):** `Host/BlueprintDocumentFactory.cs:112` (param
       `peerAssetCatalog`) + `:419-438` `BuildPeerSignatureLookup` — uses `catalog.EnumerateAll()` →
       `(AssetId, Path)` → parse signature. EditorSubsystem `:2569` (`blueprintPeerCatalog`) + `:2693`
       (`qrsCatalog` quick-reload) construct `new FileSystemAssetCatalog(dir)`.
     - Tests: `Hrot.Blueprints.Tests/Editor/AssetBrowserWindowTests.cs` (delete — type gone),
       `Editor/EditorInfrastructureTests.cs` (uses `FileSystemAssetCatalog`),
       `Debug/ProbeIntegrationTests.cs` (uses `FileSystemAssetCatalog`).
   - Salvage source: `Catalog/BlueprintAssetContributor.cs` (yields `IEditableAsset` with `AssetId` +
     `SourceFilePath` = the `Path` the peer lookup needs) + `AssetRoots.AssetsFor(Blueprint)`.

## Scope — MTB-P7-T5 (DEC-13)
1. **Delete the browser** `Hrot.Blueprints.Editor/AssetBrowserWindow.cs` and remove its
   registration/host wiring (`BlueprintWindowRegistrar` creation; the window parts of
   `BlueprintEditorModule`; the DI `AddSingleton` that fed the window). The open-docs/browser surface
   is now the docked host (BATCH-22) + Workspace submenu (BATCH-21).
2. **Salvage the peer-signature scan, then delete** `FileSystemAssetCatalog` + the Blueprints
   `IAssetCatalog`/`AssetCatalogEntry`:
   - Provide a **contributor-backed** `(Guid AssetId, string Path)` enumeration over blueprint files
     under `Assets/Blueprints` (reuse `BlueprintAssetContributor` / a directory scan equivalent to the
     old `FileSystemAssetCatalog.EnumerateAll`). Repoint `BlueprintDocumentFactory.BuildPeerSignatureLookup`
     to take this source (e.g. change the param to a `Func<IEnumerable<(Guid,string)>>` or a small
     retained helper, OR a thin `ContributorAssetCatalog` if you prefer keeping a narrow type — your
     choice; document it). The signature-parse logic (`BlueprintSignatureParser.Parse`) is unchanged.
   - Repoint EditorSubsystem `:2569`/`:2693` (peer + quick-reload) to construct the contributor-backed
     source over the SAME directory the old catalog scanned. Preserve quick-reload + CallPeer behavior.
   - Then remove `FileSystemAssetCatalog.cs` + `IAssetCatalog.cs`.
3. Update the affected tests (`EditorInfrastructureTests`, `ProbeIntegrationTests`) to the replacement
   source; delete `AssetBrowserWindowTests.cs`. Keep their behavioral assertions (peer lookup still
   resolves a peer by AssetId → Path → signature).

## CRITICAL guardrail (peer-signature must not regress)
The peer-blueprint-signature lookup powers `CallPeerBlueprintNodes` — a core compiler feature. After
the swap, **`Hrot.Blueprints.Tests` must show EXACTLY the 9 established PRE-1 pre-existing failures**
(AiPrimitive×2, Stage8×2, AllocFree, MoveToAndFire snapshot, CF2, CF7rev, WhenNodePerf) — NO new
failures. Any CallPeer / peer-signature / quick-reload test must stay green. If the swap breaks one,
fix the salvage (do not weaken the test).

## Hard constraints
- Delete ONLY the three named Blueprints types (+ their now-dead window refs/tests). Do NOT delete any
  other legacy/assembly code. Do NOT touch the AiShared `IAssetCatalog` (different interface).
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings) — no dangling refs to the deleted types.
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. Updated tests pass UNFILTERED. With the Stability
  filter: `Hrot.Blueprints.Tests` shows **exactly the 9 PRE-1 failures and no others** (run it and list
  the failing set to prove no new breakage); `Hrot.Editor.AiShared.Tests`, `Hrot.Editor.Tests`,
  `Fdp.Toolkits.Tests`, `Hrot.SimHost.Tests` → 0 failed (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-23-REPORT.md`: types deleted + every reference removed/
  repointed, the contributor-backed peer-signature salvage (and how CallPeer/quick-reload still work),
  the exact `Hrot.Blueprints.Tests` failing set (prove = 9 PRE-1), tests updated, paste actual test-run
  summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
