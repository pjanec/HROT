# BATCH-10 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P4-T1/T2: pure `FolderTreePicker.Build` read-mode tree-builder + `AssetRelPath.RelPath` helper,
and a backward-compatible `IAssetCatalogContributor.BaseFolder` seam (default `null`; file
contributors override to `AssetRoots.AssetsFor(Kind)`).

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: `FolderTreePickerTests` + `AssetRelPathTests` → **14 passed, 0 failed**.
- `AssetRelPath`: file asset → `Path.GetRelativePath(base, source)` normalized to `/` + trimmed;
  non-file (empty source or null base) → `Name`. Matches §10.2.
- `FolderTreePicker`: `FolderTreeNode` (Name/FullPath/IsLeaf/Children); `Build` splits on `/`,
  handles root-level leaves + null/empty, folders-first-then-leaves stable alphabetical sort. Pure
  (no ImGui/IO). Pick mode correctly NOT implemented (deferred to MTB-P6-T4).
- `BaseFolder` added as a **default interface member** (`=> null;`) — backward-compatible (all
  existing implementors compile unchanged; suites green). Overridden on the JSON file-backed
  contributors (`BlueprintAssetContributor`, `BTreeJsonAssetContributor`, `HsmJsonAssetContributor`)
  → `AssetRoots.AssetsFor(Kind)`.
- Suites green: AiShared 904/0, BTree.Editor 399/0, Hsm.Editor 352/0, Fdp.Toolkits 1856/0,
  SimHost 585/0. Scope clean, no legacy deletions.

## Test Quality
Good. Tree tests assert hierarchy/FullPath/IsLeaf at each level, root-level + empty/null handling,
and order-independence (determinism). RelPath tests assert exact `combat/Guard.bp.json` (Path-normalized
on Windows), scenario→Name, and that file contributors' `BaseFolder` equals the asset root while
default contributors return null. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P4-T1, MTB-P4-T2 → `[x]`. Phase 4 continues (T3/T4/T5 remain).

## Commit Message
```
feat(main-toolbar): FolderTreePicker (read) + BaseFolder seam + relpath helper (MTB-P4-T1, T2)

Pure FolderTreePicker.Build read-mode tree-builder (FolderTreeNode hierarchy, stable sort,
root-level/empty handling) and AssetRelPath.RelPath (SourceFilePath-minus-base for files,
Name for non-files). Add backward-compatible IAssetCatalogContributor.BaseFolder default member
(null), overridden to AssetRoots.AssetsFor(Kind) on the JSON file-backed Blueprint/BTree/Hsm
contributors. Tests: 14 new, all pass.
```
