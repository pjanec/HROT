# BATCH-10: FolderTreePicker (read) + BaseFolder seam + relpath helper
**Tasks:** MTB-P4-T1, MTB-P4-T2   **Phase:** 4 — Generic Asset Browser Panel   **Est:** ~8h
**Dependencies:** Phase 0 (`AssetRoots`). Pure/foundational; no UI side effects.

> Do T1 then T2 in sequence; do NOT advance until the current task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §18.1 (FolderTreePicker), §10.2 (base-folder seam), §10.1 (tree
   construction context).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P4-T1, MTB-P4-T2.
4. Existing code (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalogContributor.cs` — `Kind`, `Enumerate`,
     `ContributorChanged`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` — `Name`, `Kind`, `SourceFilePath`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs` — `AssetsFor(AssetKind)` (absolute),
     `AssetsRelative(AssetKind)`.
   - File contributors to update in T2:
     `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Catalog/BlueprintAssetContributor.cs`,
     `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs` +
     `BTreeJsonAssetContributor.cs`,
     `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Catalog/HsmAssetContributor.cs` + `HsmJsonAssetContributor.cs`.

---

## Task 1 — `FolderTreePicker` read-mode tree-builder (MTB-P4-T1) — §18.1/§18.3
**File (NEW):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/FolderTreePicker.cs` (shared widget home;
this batch implements ONLY the **read-mode pure tree-builder** — pick mode is MTB-P6-T4, do NOT
implement it here).
- A pure, ImGui-free **tree builder**: given a list of **relative paths** (using `/` separators;
  each path is a leaf, e.g. `"combat/patrol/Guard.bp.json"` or a bare `"Scout"`), build a folder
  hierarchy of nodes. Suggested shape:
  ```csharp
  public sealed class FolderTreeNode {
      public string Name { get; }          // segment name
      public string FullPath { get; }      // accumulated relative path to this node
      public bool   IsLeaf { get; }        // true for terminal asset paths
      public IReadOnlyList<FolderTreeNode> Children { get; }  // sorted, folders-first or name-sorted (be stable)
  }
  public static FolderTreeNode Build(IEnumerable<string> relativePaths);
  ```
- Split each path on `/`; intermediate segments are folders, the final segment is a leaf. Handle
  root-level leaves (no `/`) and ignore empty/`null` paths. **Stable ordering** (deterministic sort,
  e.g. folders then leaves, each alphabetical — document the rule). No ImGui, no I/O.

**Tests required (`FolderTreePickerTests` in `Hrot.Editor.AiShared.Tests`):**
- `Build_NestedPaths_ProducesCorrectHierarchy` — e.g. `["a/b/x", "a/b/y", "a/z"]` → folder `a` with
  child folder `b` (leaves x,y) and leaf `z`; assert the FullPath + IsLeaf at each level.
- `Build_EmptyAndRootLevelLeaves_Handled` — empty input → empty/root node; root-level leaf `"x"`
  (no slash) is a leaf child of root; null/empty entries skipped without throw.
- `Build_IsStable_Sorted` — same input in different input order produces identical structure
  (deterministic), and children are in the documented sorted order.

## Task 2 — `BaseFolder` seam + relative-path helper (MTB-P4-T2) — §10.2
- Add `string? BaseFolder { get; }` to `IAssetCatalogContributor`. To avoid breaking every
  implementor (incl. test fakes / non-file contributors), give it a **default interface member**
  `string? BaseFolder => null;`. **Override** it on the FILE-backed contributors to return the
  kind's Assets root: `AssetRoots.AssetsFor(Kind)` —
  - Blueprint → `BlueprintAssetContributor`
  - BTree → `BTreeAssetContributor` and/or `BTreeJsonAssetContributor` (whichever produces assets
    whose `SourceFilePath` lives under `Assets/BTrees` — set it on the file-backed one(s); document
    which and why)
  - Hsm → `HsmAssetContributor` and/or `HsmJsonAssetContributor` (same reasoning)
  Non-file contributors (and Scenario, which doesn't exist yet) keep the default `null`.
- Add a **relative-path helper** (static, in `Hrot.Editor.AiShared`, e.g.
  `Browser/AssetRelPath.cs`): `string RelPath(IEditableAsset asset, string? baseFolder)`:
  - **File asset** (non-empty `SourceFilePath` AND non-null `baseFolder`): the `SourceFilePath`
    made relative to `baseFolder`, normalized to `/` separators (use `Path.GetRelativePath` then
    replace `\`→`/`; trim leading `/`). 
  - **Non-file asset** (empty `SourceFilePath` or null base): the asset's `Name` (already a relpath
    for scenarios, §19).

**Tests required (`AssetRelPathTests` in `Hrot.Editor.AiShared.Tests`, fake `IEditableAsset` +
fake contributor):**
- `FileAsset_RelPath_IsSourceMinusBase` — base `…/Assets/Blueprints`, source
  `…/Assets/Blueprints/combat/Guard.bp.json` → `"combat/Guard.bp.json"` (forward slashes,
  Path-normalized; verify on Windows `\`).
- `ScenarioAsset_RelPath_IsName` — empty `SourceFilePath` (or null base) → returns `Name` verbatim.
- `Contributor_BaseFolder_MatchesAssetRoot` — a file contributor's `BaseFolder` equals
  `AssetRoots.AssetsFor(its Kind)`; a default/non-file contributor's `BaseFolder` is null.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. Do NOT implement FolderTreePicker pick-mode
  (MTB-P6-T4) or the AssetBrowserPanel (MTB-P4-T3). No scope creep beyond the two tasks' files +
  the contributor `BaseFolder` overrides.
- The `BaseFolder` interface addition MUST be backward-compatible (default member) — confirm all
  existing `IAssetCatalogContributor` implementors still compile without edits except the file
  contributors you intentionally override.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for: `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, and
  the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run if it appears).
  For `Hrot.Blueprints.Tests` (PRE-1 pre-existing failures) run your new tests by class filter; do
  NOT touch the pre-existing failures.
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-10-REPORT.md`: files changed, which contributors got a
  non-null `BaseFolder` (and why those), the tree-builder sort rule, each new test + assertions,
  paste actual test-run summaries, and the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
