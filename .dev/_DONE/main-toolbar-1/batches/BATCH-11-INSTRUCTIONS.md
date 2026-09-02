# BATCH-11: AssetBrowserPanel — tabs + per-kind tree + row icons
**Tasks:** MTB-P4-T3   **Phase:** 4 — Generic Asset Browser Panel   **Est:** ~10h
**Dependencies:** BATCH-10 (`FolderTreePicker`, `AssetRelPath`, `BaseFolder`), BATCH-03 (`AssetKindIcons`, `IIconProvider`, `IconWidgets`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §10.1 (AssetBrowserPanel), §10.2 (base-folder seam), §5.2 (kind icons).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P4-T3.
4. Existing code (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` — `All` (flat `IEditableAsset` list),
     `FindByAssetId/Name`, `Changed`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` — `Name`, `Kind`, `SourceFilePath`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/FolderTreePicker.cs` (Build → FolderTreeNode) and
     `AssetRelPath.cs` (RelPath) from BATCH-10.
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKindIcons.cs` (GetIconKey) + `Identity/AssetRoots.cs`
     (`AssetsFor`).
   - `NodeEditor.Core` `IIconProvider`/`IconHandle`; `Fdp.Presentation` `IconWidgets`.

## Scope — do ONLY MTB-P4-T3 (the panel model + per-kind tabs/tree/rows). NOT the "All" tab,
## chips, or incremental filter (MTB-P4-T4). NOT auto-expand/last-opened (MTB-P4-T5).
**File (NEW):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs`

### Types
```csharp
[Flags] public enum AssetKindFilter { None=0, Scenario=1, Blueprint=2, BTree=4, Hsm=8,
                                      Blackboard=16, Utility=32, All=~0 }

public sealed class AssetBrowserPanelOptions {
    public AssetKindFilter Kinds { get; init; } = AssetKindFilter.All;
    public bool   ShowAllTab { get; init; } = true;      // behavior implemented in MTB-P4-T4
    public AssetKind? InitialKind { get; init; }          // behavior implemented in MTB-P4-T5
    public string?    InitialFullPath { get; init; }      // behavior implemented in MTB-P4-T5
}
```
Define the full options type now (so later batches don't change its shape), but in THIS batch only
the `Kinds` filter (tab set) is wired; `ShowAllTab`/`InitialKind`/`InitialFullPath` are accepted and
stored, behaviors land in T4/T5. Map `AssetKind`↔`AssetKindFilter` with a small helper (note:
`AssetKind.Scenario` does not exist yet — handle the existing 5 kinds; the Scenario filter bit is
reserved for MTB-P5-T2).

### Panel — logic separated from ImGui draw (the success conditions are logic-level)
Constructor: `AssetBrowserPanel(IAssetCatalog catalog, IIconProvider icons, AssetBrowserPanelOptions options)`.
Expose a **testable model** (no ImGui) plus `DrawContent()` (ImGui) that renders it:
- **Tabs:** `IReadOnlyList<AssetKind> Tabs` = the permitted kinds (from `options.Kinds`) that the
  catalog actually has assets for (or all permitted kinds — pick one and document; `Tabs_ReflectKindFilter`
  must pass either way — assert the filter is honored). One tab per permitted kind.
- **Per-kind tree:** for a kind, gather `catalog.All.Where(a => a.Kind == kind)`, compute each asset's
  relpath via `AssetRelPath.RelPath(asset, BaseFolderFor(kind))` where
  `BaseFolderFor(kind)` = `AssetRoots.AssetsFor(kind)` for the file kinds (Blueprint/BTree/Hsm) and
  `null` otherwise (wrap the AOORE for rootless kinds → null), then `FolderTreePicker.Build(relpaths)`.
  Provide `FolderTreeNode TreeFor(AssetKind kind)` and a way to map a leaf node → its `IEditableAsset`
  (e.g. a dictionary relpath→asset per kind).
- **Row icon:** each asset row carries `AssetKindIcons.GetIconKey(kind)` resolved via the provider;
  expose `string RowIconKey(IEditableAsset asset)` (or include it in a row model) so it's assertable.
- **Selection + activation:** `IEditableAsset? Selection { get; }` (single-click highlight via a
  `SelectAsset(asset)` method), and `event Action<IEditableAsset>? AssetActivated` raised by an
  `ActivateAsset(asset)` method (wired to double-click in `DrawContent`). **The panel performs NO side
  effects** — it never opens documents or loads anything; it only raises the event / sets Selection.
- Re-read from the catalog on `Changed` (rebuild the per-kind trees); immediate-mode draw.

### Tests required (`AssetBrowserPanelTests` in `Hrot.Editor.AiShared.Tests`, fake `IAssetCatalog`
+ fake `IIconProvider` + fake `IEditableAsset`s)
- `Tabs_ReflectKindFilter` — with `Kinds = Blueprint | Hsm`, `Tabs` contains exactly those kinds
  (not BTree/Blackboard/Utility).
- `PerKindTree_GroupsAssetsByRelPath` — assets with SourceFilePath under the kind's Assets root
  (e.g. `Assets/Blueprints/combat/Guard.bp.json`, `Assets/Blueprints/Patrol.bp.json`) produce a tree
  with folder `combat` (leaf Guard) and root leaf Patrol; assert structure + leaf→asset mapping.
- `Row_CarriesKindIconKey` — `RowIconKey(blueprintAsset) == AssetKindIcons.GetIconKey(AssetKind.Blueprint)`
  (and one more kind).
- `DoubleClick_RaisesAssetActivated_WithAsset` — `ActivateAsset(asset)` raises `AssetActivated` with
  that exact asset; `SelectAsset(asset)` sets `Selection`; neither performs any catalog/document side
  effect (assert via a recording fake that no load/open method was called).

## Hard constraints
- Do NOT implement the "All" tab / chips / incremental filter (MTB-P4-T4) or auto-expand /
  last-opened (MTB-P4-T5). Do NOT add `AssetKind.Scenario` (MTB-P5-T2). No scope creep.
- The panel must perform NO side effects (no document open, no scenario load) — host decides.
- Do NOT delete/modify legacy/assembly-loading code. Keep existing public APIs intact.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests` + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`
  (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-11-REPORT.md`: files changed, the logic/draw split + model
  seams, how Tabs are derived, each new test + assertions, paste actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
