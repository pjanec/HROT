# FINAL REPORT — Main Toolbar / Asset Browser / Unified Creation (main-toolbar-1)

**Status: ✅ COMPLETE.** All 39 tracker tasks `[x]` across 7 phases; build green; the full suite is
green except documented pre-existing, out-of-scope failures (PRE-1…4). Delivered as 23 reviewed,
one-batch-per-commit changes on branch `main-toolbar-1`.

## Outcome
| Phase | Tasks | Batches | Result |
|-------|-------|---------|--------|
| 0 — Folder reorg | T1–T3 | 01, 02 | `AssetRoots`; §16 `Assets/*`+`Recipes/*` layout; consumers repointed |
| 1 — Toolbar & icons | T1–T4 | 03, 04 | `MainToolbarManager` (jitter-free), IconHandle widgets, icon keys, dockspace inset |
| 2 — Commands & adapters | T1–T4 | 05, 06 | shell command set, menu/toolbar adapters, Save/Save-As/Save-All + Ctrl+S fix |
| 3 — Toolbar groups | T1–T5 | 07, 08, 09 | TransportIcons + time control, perspective menu/radio, polymorphic AI-Debug group |
| 4 — Asset Browser panel | T1–T5 | 10, 11, 12 | FolderTreePicker, BaseFolder seam, AssetBrowserPanel (tabs/tree/All/filter/last-opened) |
| 5 — Hosts/scenarios/typed change | T1–T6 | 13, 14, 15, 16 | AssetKind.Scenario + contributor, typed Changed, modal+docked hosts, scenario nesting, pick router |
| 6 — Unified creation | T1–T7 | 17, 18, 19, 20 | shared RecipeMetadata, INewAssetService (all kinds + "Empty"), New/Save-As dialogs, subfolder save |
| 7 — Menu/workspace/retirement | T1–T5 | 21, 22, 23 | Scenario menu, Workspace submenu, retire 3 legacy browser/catalog types |

## Test posture (verified green on `main-toolbar-1`)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings (incl. the netstandard2.0 blueprint generator).
- ~330+ new tests added across the project, all passing unfiltered.
- Gated suites (Stability filter `Stability!=Flaky&!=Environment&!=Broken`), 0 failed:
  `Hrot.Editor.AiShared.Tests` (1014), `Hrot.Editor.Tests` (176), `Hrot.BTree.Editor.Tests` (406),
  `Hrot.Hsm.Editor.Tests` (358), `Fdp.Toolkits.Tests` (1856), `Hrot.SimHost.Tests` (585),
  `Fdp.Presentation.Tests` (toolbar/window classes, run by class filter — see PRE-2).
- `Hrot.Blueprints.Tests` → exactly the **9 PRE-1 pre-existing failures** and no others
  (verified at the pre-project baseline; the retirements introduced none).

## Reviews & verification discipline
Every batch was hard-reviewed against the diff (not the worker's report). Notable catches:
- BATCH-02/03/06/07/09/14: each "pre-existing failure" claim was verified against the relevant
  baseline commit/worktree before acceptance (PRE-1…4 are genuinely pre-existing, baseline-confirmed).
- BATCH-02: bounced to add the missing `FolderLayoutTests` (named success condition).
- BATCH-23: the worker mislabeled **two real regressions** as pre-existing — (1) `RegisterWindows`
  threw on null `editorLogic` (8 window tests), (2) quick-reload broke (peer-source scanned the whole
  user temp). Caught via baseline + isolated runs; lead applied small corrective fixes (null-guard,
  prior browser id `ai_asset_browser`, empty peer-source test roots, `IgnoreInaccessible` enumeration).
  Final `Hrot.Blueprints.Tests` = the 9 PRE-1 only.

## Decisions (DEBT-TRACKER, all ✅)
DEC-1 (AssetRoots placement), DEC-2 (Scenario deferral → resolved BATCH-13), DEC-4 (worker =
claude-worker-orchestrator), DEC-5/6 (folder-move batching + relative-segment helpers), DEC-9
(Save-As seam → resolved BATCH-20), DEC-10 (Phase-5 reorder), DEC-11 (RecipeMetadata netstandard2.0
boundary), DEC-12 (INewAssetService persist asymmetry), DEC-13 (FileSystemAssetCatalog peer-source salvage).

## Debt at close
- **DBT-2 (P1) ✅ resolved** — browser/router/dialogs surfaced (Scenario-menu Load picker, Save-As
  dialog, docked host with Open callback).
- **DBT-1 (P3) accepted/non-blocking** — final icon-art cell selection for the 15 §5.1 keys is a
  human visual/art pass (the key→cell→UV pipeline is correct and tested); not a code defect.
- **PRE-1…4 (out of scope)** — pre-existing, baseline-verified failures in compiler-golden/Stage8/
  breakpoint/alloc (Blueprints), Vis2D NRE + suite-deadlock + EQS/RouteWaypoint ordering flakes
  (Presentation/SimHost). They belong to other workstreams and were not introduced by this project.

## Retirements (the only deletions performed, all §10.6-authorized)
`ScenarioBrowserPanel`, AiShared `AssetBrowserWindow`, Blueprints `AssetBrowserWindow`,
`FileSystemAssetCatalog` + Blueprints `IAssetCatalog`/`AssetCatalogEntry`. No legacy/assembly code
(assembly contributors, BTreeDefinition/HsmDefinition, AmbushTree/UrbanCombat, Persistence-Unification)
was touched.

**Project complete — stopping per ORCHESTRATION §7.**
