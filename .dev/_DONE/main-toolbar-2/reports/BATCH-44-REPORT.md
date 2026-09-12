# BATCH-44 Report — BUG-A6: New Asset Write/Scan Reconciliation (Blueprint / BTree / HSM)

**Date:** 2026-06-12  
**Branch:** blueprint-integ-1  
**Status:** DONE — build 0 warnings, all tests pass at baseline

---

## Root Cause Summary

All three kinds share the same structural bug: the new-asset **write location** was the bin/output dir (`AssetRoots.AssetsFor(kind)` = `AppContext.BaseDirectory/Assets/<Kind>`), while each contributor **scans** the SOURCE project dir (`aiRootDir/Assets/<Kind>`). The mismatch meant `FindByAssetId(minted.AssetId)` always returned null → `Open` was never called → no canvas, no perspective switch.

---

## Per-Kind Write-vs-Scan Reconciliation

### Blueprint

| | Before | After |
|---|---|---|
| **Contributor scans** | `bpRootDir` = `aiRootDir/Assets/Blueprints` (source, resolved from `.csproj`) | unchanged |
| **New-asset write** | `AssetSavePath.Compose(Blueprint, dest, name)` → `AssetRoots.AssetsFor(Blueprint)` = **bin dir** | `AssetSavePath.Compose(Blueprint, dest, name, assetRootOverride: _bpRootDir)` → **source dir** |

`AssetSavePath.Compose` already has an `assetRootOverride` parameter for exactly this case. Passing `_bpRootDir` routes the new `.bp.json` to the same directory that `bpContrib.Refresh()` scans.

**End-to-end path (Blueprint):**
1. `ShowNewAssetDialog` → `CreateNew` mints `BlueprintAsset` with fresh `AssetId` (in memory only)
2. `AssetSavePath.Compose(..., assetRootOverride: _bpRootDir)` → path = `aiRootDir/Assets/Blueprints/<dest>/<name>.bp.json`
3. `saveAsBlueprintToFile(minted, bpPath)` → `SaveActiveBlueprintCommand.Save` writes the file
4. `RefreshFromAssembly(aiAsm)` → `_blueprintRefresh()` → `bpContrib.Refresh()` rescans `bpRootDir`; header read finds the new `.bp.json` by its `AssetId`
5. `catalog.FindByAssetId(minted.AssetId)` returns the catalogued `BlueprintFileAsset`
6. `_aiDocumentManager.Open(catalogued)` → `_perspectiveSwitcher.Activate("Blueprint")` → canvas shown

### BTree

| | Before | After |
|---|---|---|
| **Contributor scans** | `btreeJsonContrib.Refresh(rootDirectory: btreeJsonRootDir)` where `btreeJsonRootDir = aiRootDir/Assets/BTrees` | unchanged |
| **Service root** | `BTreeNewAssetService()` → `AssetRoots.AssetsFor(BTree)` = **bin dir** | `BTreeNewAssetService(_btreeJsonRootDir)` → **source dir** |
| **Post-write refresh** | `RefreshFromAssembly` only (does NOT call `btreeJsonContrib.Refresh`) | + `_btreeJsonContrib.Refresh(rootDirectory: _btreeJsonRootDir)` |

`BTreeNewAssetService` accepts an optional `assetRootPath` ctor param; passing `_btreeJsonRootDir` directs `AssetSavePath.Compose` to write `<name>.btree.json` into the source tree.

`RefreshFromAssembly` only calls `_bTreeLoadFrom` (assembly-based contributor), NOT the JSON contributor — so an explicit `_btreeJsonContrib.Refresh(...)` is required after write.

**End-to-end path (BTree):**
1. `ShowNewAssetDialog` → `CreateNew` mints DTO, writes `.btree.json` to `aiRootDir/Assets/BTrees/<dest>/<name>.btree.json`
2. `RefreshFromAssembly(aiAsm)` (for assembly contributor)
3. `_btreeJsonContrib.Refresh(rootDirectory: _btreeJsonRootDir)` → re-discovers, reads header, loads full asset
4. `catalog.FindByAssetId(minted.AssetId)` returns the `BehaviorTreeAsset`
5. `_aiDocumentManager.Open(catalogued)` → `_perspectiveSwitcher.Activate("BTree")` → canvas shown

### HSM

Identical structural fix to BTree.

| | Before | After |
|---|---|---|
| **Contributor scans** | `hsmJsonContrib.Refresh(rootDirectory: hsmJsonRootDir)` where `hsmJsonRootDir = aiRootDir/Assets/HSMs` | unchanged |
| **Service root** | `HsmNewAssetService()` → `AssetRoots.AssetsFor(Hsm)` = **bin dir** | `HsmNewAssetService(_hsmJsonRootDir)` → **source dir** |
| **Post-write refresh** | `RefreshFromAssembly` only | + `_hsmJsonContrib.Refresh(rootDirectory: _hsmJsonRootDir)` |

**End-to-end path (HSM):** same as BTree with `.hsm.json` / `HsmAsset`.

---

## BUG-A9 (Save-As disabled)

`shell.saveAs` `IsEnabled` is `() => docManager.Active != null`. Before A6 fix, `Open` was never called so `docManager.Active` stayed null → Save-As was greyed out. After A6 fix, `Open(catalogued)` sets the active document → Save-As auto-enables. No separate code change required.

---

## Files Changed

**`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`** (single file):

1. **New instance fields** (after `_aiCatalogBuilder` block, ~L283):
   - `string? _bpRootDir` — Blueprint source scan root
   - `string? _btreeJsonRootDir` — BTree JSON source scan root
   - `string? _hsmJsonRootDir` — HSM JSON source scan root
   - `BTreeJsonAssetContributor? _btreeJsonContrib` — captured from Initialize
   - `HsmJsonAssetContributor? _hsmJsonContrib` — captured from Initialize

2. **`Initialize` (~L684):** changed local `var bpRootDir/btreeJsonContrib/hsmJsonContrib` + new `btreeJsonRootDir`/`hsmJsonRootDir` to assign to the instance fields above; kept local aliases so the rest of Initialize compiles unchanged.

3. **`RegisterWindows` / `_newAssetServices` (~L2268):** changed `BTreeNewAssetService()` → `BTreeNewAssetService(_btreeJsonRootDir)` and `HsmNewAssetService()` → `HsmNewAssetService(_hsmJsonRootDir)`.

4. **`RegisterWindows` / `ShowNewAssetDialog` closure (~L2584):**
   - Blueprint: added `assetRootOverride: _bpRootDir` to `AssetSavePath.Compose`.
   - BTree: added `_btreeJsonContrib?.Refresh(rootDirectory: _btreeJsonRootDir)` after assembly refresh.
   - HSM: added `_hsmJsonContrib?.Refresh(rootDirectory: _hsmJsonRootDir)` after assembly refresh.

All null-checks use `?.` — safe when source dir is unavailable.

---

## Test Results

| Suite | Command | Result |
|---|---|---|
| `Hrot.Editor.Tests` | `dotnet test Hrot.Editor.Tests.csproj` | **Failed: 0**, Passed: 186 |
| `Hrot.Blueprints.Tests` filtered `EditorSubsystemBlueprintWindows` | `--filter EditorSubsystemBlueprintWindows` | **Failed: 0**, Passed: 15 |
| `Hrot.Blueprints.Tests` full suite | `dotnet test Hrot.Blueprints.Tests.csproj` | **Failed: 8** (all pre-existing: golden/Roslyn/perf benchmarks — same as PRE-1 baseline ~9) |

No new failures introduced. Build: 0 warnings, 0 errors.

---

## Live Confirmation Note

The GUI confirmation (canvas appears, perspective switches) is the user's — this fix cannot be validated headlessly. The source-path trace above shows each link in the chain is provably correct by inspection.
