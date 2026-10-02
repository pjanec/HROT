# BATCH-05 Report
**Tasks:** PU-301, PU-302, PU-303  **Branch:** `blueprint-integ-1`  **Date:** 2026-06-05

---

## Implementation Summary

### PU-301 — JSON dual-load contributors + ownership + SourceFilePath

**Mapper `ToModel` entry (the branch point)**  
Added `BehaviorTreeAssetMapper.ToModel(dto, sourceFilePath, isEditorOwned)` and `HsmAssetMapper.ToModel(dto, sourceFilePath, isEditorOwned)` alongside the existing `FromDto` (which now delegates to `ToModel(dto, string.Empty, true)`).  These are the canonical model-construction entries that set both fields explicitly.  `FromDto` is unchanged for all existing callers.

**New contributors**
- `Hrot.BTree.Editor/Catalog/BTreeJsonAssetContributor.cs` — implements `IAssetCatalogContributor`, `Kind=BTree`.  `Discover(jsonPaths, rootDirectory)` reads file headers via `BTreeJsonServices.ReadHeader` (skips malformed, never throws).  `LoadFull(filePath)` → `BTreeJsonServices.Deserialize` → `BehaviorTreeAssetMapper.ToModel(dto, filePath, isEditorOwned:true)` → stores `_debugSession` reference on asset (via `asset.SetDebugSession`) so `StitchKernelIndices` can re-wire symbolication.  `Refresh(...)` → `Discover` + `LoadAll` + `ContributorChanged.Invoke()`.
- `Hrot.Hsm.Editor/Catalog/HsmJsonAssetContributor.cs` — symmetric, using `HsmJsonServices` and `HsmAssetMapper.ToModel`.

**Ownership under JSON SoT:** presence of a `.json` file (no marker read).  `IsEditorOwned=true`, `SourceFilePath=<absolute .json path>`, `IsDirty=false` (both mappers call `ClearDirty()` before returning).

**JSON wins AssetId collision:** `AiAssetCatalogBuilder` now accepts optional `bTreeJsonContributor` / `hsmJsonContributor` params.  JSON contributors are added to `AssetCatalog` **after** the assembly contributors; `AssetCatalog.Rebuild()` iterates contributors in add-order and the final `_byId[assetId] = asset` write wins — JSON contributor (last) supersedes assembly contributor on collision.

**EditorSubsystem wiring (dormant):** `EditorSubsystem.Initialize` creates `BTreeJsonAssetContributor(_btreeDebugSession)` and `HsmJsonAssetContributor`, calls `Discover(rootDirectory: <Trees dir>)` / `Discover(rootDirectory: <Machines dir>)`, and passes them as the optional params to `AiAssetCatalogBuilder`.  No `.btree.json`/`.hsm.json` files exist under `Hrot.AI.Behaviors` yet (PU-401 migration) → zero assets discovered in the live editor; mechanism dormant-but-correct.

---

### PU-301 acceptance — reopen-when-C#-won't-compile (the crux)

Test `PU301_Acceptance_BTree_ReopenWhenCSharpBroken_DocumentStaysOpenTopologyIntact` in `ReconcileStitchTests.cs`:
1. Writes a valid `.btree.json` to a temp dir.
2. Loads it via `BTreeJsonAssetContributor.Refresh` — NO assembly contribution for the same AssetId.
3. `AiDocumentManager.Open(jsonAsset)` succeeds.
4. `ReconcileFromCatalog(empty)` — no fresh asset in catalog (assembly unavailable).
5. Asserts: document still open; `Nodes.Count == 2` (topology intact); both `FindBlobIndex(rootVid) == -1` and `FindBlobIndex(seqVid) == -1` (stitch inert); `IsDirty == false`.

HSM symmetric (`PU301_Acceptance_Hsm_...`).

---

### PU-302 — post-reload stitching + Kind-guard

**New `IStitchableAsset` interface** (`Hrot.Editor.AiShared/Identity/IStitchableAsset.cs`):
```csharp
public interface IStitchableAsset : IEditableAsset
{
    void StitchRuntimeIndices(IEditableAsset? fresh);
}
```
Keeps `AiDocument`/`AiDocumentManager` dependency-free of concrete BTree/HSM types (avoiding circular references).

**`AiDocument.StitchRuntimeIndices(IEditableAsset? fresh)`:**
```csharp
public void StitchRuntimeIndices(IEditableAsset? fresh)
{
    if (Asset is IStitchableAsset stitchable)
        stitchable.StitchRuntimeIndices(fresh);
}
```

**`ReconcileFromCatalog` — EXACT Kind-guarded branch (design §3 D13):**
```csharp
if (doc.Asset.Kind is AssetKind.BTree or AssetKind.Hsm && doc.Asset.IsEditorOwned)
    doc.StitchRuntimeIndices(fresh);
else
    doc.ReconcileAsset(fresh);
```
Blueprint assets are `AssetKind.Blueprint` with `IsEditorOwned=true` → go to `ReconcileAsset` (full replace).  Hand-authored BTree/HSM (`IsEditorOwned=false`) → `ReconcileAsset`.  Only editor-owned BTree/HSM → stitch.

**`BehaviorTreeAsset.StitchKernelIndices(BehaviorTreeAsset? fresh, BTreeDebugSession? debugSession)`:**
- Builds `visualIdStr → blobIndex` map from `fresh.Blob.DebugMetadata[i].VisualId`.
- For each node in `_nodes`: if VisualId found → assign `KernelBlobIndex` + update `_visualIdToBlobIndex`; else → `KernelBlobIndex = -1`, remove from lookup (sentinel).
- Updates `Blob = freshBlob` (the recompiled blob reference).
- If unmatched nodes → `SetLoadDiagnostic(BlackboardLoadState.StructParseFailed, ...)`.
- Re-wires: `debugSession?.SetDebugMetadata(freshBlob.DebugMetadata, AssetId)`.
- **Does NOT call `MarkDirty`.**

Explicit interface impl:
```csharp
void IStitchableAsset.StitchRuntimeIndices(IEditableAsset? fresh)
    => StitchKernelIndices(fresh as BehaviorTreeAsset, _debugSession);
```

**`HsmAsset.UpdateBlob(blob, metadata)`** (added — mirrors `BehaviorTreeAsset.ReplaceAll`'s blob update):
```csharp
internal void UpdateBlob(HsmDefinitionBlob blob, MachineMetadata metadata)
{
    _blob = blob;
    _metadata = metadata;
}
```
Required because `Blob`/`Metadata` were getter-only auto-properties; converted to backing fields `_blob`/`_metadata`.

**`HsmAsset.StitchKernelIndices(HsmAsset? fresh)`:**
- Builds `StableId → FlatIndex` map from `fresh.Metadata.StateStableIds`.
- Builds `VisualId → FlatIndex` map from `fresh.Metadata.TransitionVisualIds`.
- Assigns `state.FlatIndex` for all states, `transition.FlatIndex` for all transitions.
- Calls `UpdateBlob(fresh.Blob, fresh.Metadata)`.
- Diagnostic on unmatched; `MarkDirty` never called.

---

**Debug overlay re-wire location:** `BehaviorTreeAsset.StitchKernelIndices` — the final line before return:
```csharp
debugSession?.SetDebugMetadata(freshBlob.DebugMetadata, AssetId);
```
The `debugSession` is set on the asset at load time (`BTreeJsonAssetContributor.LoadFull` calls `asset.SetDebugSession(_debugSession)`) and stored in `_debugSession`. HSM has no equivalent debug session in the current code.

---

**Proof Blueprint reconcile is untouched:**  
Test `PU302_KindGuard_Blueprint_UsesFullReplace_NotStitch`: Blueprint doc → `ReconcileFromCatalog` → `ReconcileAsset(fresh)` → `doc.Asset` is the `fresh` instance with the updated Name. The stitch branch never executes. Also test `PU302_KindGuard_HandAuthored_BTree_UsesFullReplace_NotStitch`: same full-replace for `IsEditorOwned=false`.

---

**Confirmation nothing calls MarkDirty on load/stitch:**
- `BehaviorTreeAssetMapper.ToModel` calls `asset.ClearDirty()` before returning.
- `HsmAssetMapper.ToModel` calls `asset.ClearDirty()` before returning.
- `BehaviorTreeAsset.StitchKernelIndices`: no `MarkDirty` call anywhere in the method.
- `HsmAsset.StitchKernelIndices`: no `MarkDirty` call anywhere in the method.
- `AiDocument.StitchRuntimeIndices`: no `_isDirty = true`.
- Verified by test `NoDirty_LoadAndStitch_LeaveDirtyFalse`.

---

## Design Decisions

1. **`IStitchableAsset` interface** instead of direct type casts in `AiDocumentManager`: avoids circular project references (AiShared ← BTree/Hsm, not the other way). The interface lives in `AiShared` alongside `IEditableAsset`.

2. **`_debugSession` stored on `BehaviorTreeAsset`**: the debug session is set by `BTreeJsonAssetContributor` at load time so `StitchRuntimeIndices` (called from `AiDocumentManager`, which has no debug-session access) can re-wire symbolication without external injection.

3. **`BlackboardLoadState.StructParseFailed` for unmatched nodes**: used as the "partial stitch" diagnostic (no blob match). `AssemblyFailed` used for null-blob (assembly unavailable) case. These are the nearest existing enum values; design says to call `SetLoadDiagnostic(...)` — the exact value is an implementation detail.

4. **`AiAssetCatalogBuilder` optional params** for JSON contributors: backward-compatible addition — all existing callers continue to compile with the two optional params defaulting to `null`.

5. **Pre-existing test update** (`ReloadReconciliationTests.Reload_ReconcileFromCatalog_UpdatesMatchingOpenDoc`): this test verified the old behavior where `ReconcileFromCatalog` always did full-replace for BTree. PU-302 intentionally changes this. Updated the test to use `Kind=Blueprint` (which still uses full-replace) — the documented change.

---

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| `IStitchableAsset` interface in AiShared instead of direct dispatch | Avoids circular project reference (AiDocumentManager is in AiShared, BTree/Hsm editors reference AiShared) | Clean dependency graph | None — adds a small interface |
| `_debugSession` field on `BehaviorTreeAsset` | Stitch call site has no debug session available | Allows re-wiring symbolication from within the stitch call | Slight coupling between model + debug session; negligible |
| `BlackboardLoadState.StructParseFailed` for partial stitch, `AssemblyFailed` for null blob | No `Warning`/`Error` values exist in the enum | Uses nearest semantically-correct values | Could be a separate enum value (PU-debt candidate) |
| Pre-existing test updated to use `Blueprint` instead of `BTree` | PU-302 intentionally routes editor-owned BTree through stitch, not full-replace | Test correctly reflects new contract | None |

---

## Test Results

### New tests (all green)

**`Hrot.BTree.Editor.Tests.Catalog.BTreeJsonAssetContributorTests`** (7 tests):
- `Discover_ValidFile_HeaderContainsAssetIdAndName` — header AssetId+Name correct
- `LoadFull_ValidFile_ModelHasCorrectTopologyAndOwnership` — Nodes.Count=2, IsEditorOwned=true, SourceFilePath=path, IsDirty=false
- `Discover_MalformedFile_IsSkipped_SiblingStillDiscovered` — no throw, sibling loads
- `LoadFull_IsEditorOwned_True_And_SourceFilePath_EqualsJsonPath`
- `LoadFull_DoesNotMarkDirty`
- `Refresh_FiresContributorChanged`
- `Catalog_JsonWins_OnAssetIdCollision_WithAssemblyContributor` — JSON IsEditorOwned=true, SourceFilePath ends with `.btree.json`

**`Hrot.Hsm.Editor.Tests.Catalog.HsmJsonAssetContributorTests`** (4 tests):
- `Discover_ValidFile_HeaderContainsAssetIdAndName`
- `LoadFull_ValidFile_ModelHasCorrectTopologyAndOwnership` — AllStates.Count=1, IsEditorOwned=true, IsDirty=false
- `Discover_MalformedFile_IsSkipped_SiblingStillDiscovered`
- `LoadFull_DoesNotMarkDirty`

**`Hrot.Editor.AiShared.Tests.Documents.ReconcileStitchTests`** (8 tests):
- `PU301_Acceptance_BTree_ReopenWhenCSharpBroken_DocumentStaysOpenTopologyIntact` — THE CRUX
- `PU301_Acceptance_Hsm_ReopenWhenCSharpBroken_DocumentStaysOpenTopologyIntact`
- `PU302_Stitch_BTree_CorrectKernelBlobIndex_ByVisualId` — root→0, seq→1, Blob updated, IsDirty=false
- `PU302_Stitch_BTree_UnmatchedNode_Getssentinel_AndDiagnostic` — root→0, seq→-1, LoadState≠Clean, IsDirty=false
- `PU302_Stitch_Hsm_CorrectFlatIndex_ByStableId` — Idle state FlatIndex=1, IsDirty=false
- `PU302_KindGuard_Blueprint_UsesFullReplace_NotStitch` — doc.Asset IS fresh, Name="BpFresh"
- `PU302_KindGuard_HandAuthored_BTree_UsesFullReplace_NotStitch` — doc.Asset IS fresh
- `NoDirty_LoadAndStitch_LeaveDirtyFalse`

### Full gate results

| Suite | Pass | Fail | Notes |
|-------|------|------|-------|
| `Hrot.BTree.Editor.Tests` | 392 | 0 | +7 new (BTreeJsonContributor) |
| `Hrot.Hsm.Editor.Tests` | 341 | 0 | +4 new (HsmJsonContributor) |
| `Hrot.Editor.AiShared.Tests` | 769 | 0 | +8 new (ReconcileStitch); 1 pre-existing updated |
| `Hrot.AiEditor.Persistence.Tests` | 88 | 0 | persistence gate — unchanged |
| `Hrot.AiEditor.Generators.Tests` | 37 | 0 | generators gate — unchanged |
| `Hrot.ClusterRunner.Integration.Tests` (EditorSubsystem) | 13 | 0 | boot 10/10 + BP 3/3 |
| `Hrot.Blueprints.Tests` | 1357 | 7 | **7 = baseline DEBT-006** — 0 new |
| `dotnet build IOS-IG-SimHost.sln` | 0 errors | 0 warnings | |

**Blueprint regression proof:** 7 failures in `Hrot.Blueprints.Tests` are the exact pre-existing DEBT-006 failures (golden snapshots + allocation + `IBlueprintTimeController` obsolete). Zero new Blueprint failures introduced.

---

## Developer Insights

1. **Circular dependency constraint is the main architectural challenge.** `AiDocumentManager` lives in `AiShared` but needs to call stitch on BTree/HSM models. The `IStitchableAsset` interface solved this cleanly without inverting the dependency graph.

2. **`BehaviorTreeAsset.Blob` was already a settable private field** (set via `ReplaceAll`). For `HsmAsset.Blob`/`Metadata`, the properties were getter-only auto-props — converting them to backing fields is the minimal change. No other callers modified them.

3. **`BlackboardLoadState` enum has no `Warning` variant.** Design §6.6 says "surface a diagnostic" but the existing enum values are blackboard-specific. Using `StructParseFailed` for partial stitch is a slight semantic stretch. A future cleanup could add `StitchPartial`/`BlobUnavailable` values — tracked as PU-D09.

4. **EditorSubsystem wiring is path-relative** (`../../Hrot/AI.Behaviors/Trees`). In production the paths may diverge from the dev layout. This is consistent with how `bpRootDir` is set for blueprints. The contributors are harmless if the directory doesn't exist (they just discover zero files).

5. **`BTreeDebugSession.SetDebugMetadata` re-wiring in stitch:** confirmed working in the test by verifying `FindBlobIndex` returns the correct index (which implicitly requires the blob to be updated). The debug session re-wire itself isn't tested in headless because debug sessions require ImGui infrastructure — but the call is correct and mirrors `BTreeAssetContributor.RegisterBlobCore`.

---

## Known Issues

1. **PU-D09 (P3):** `BlackboardLoadState` used as stitch diagnostic is semantically imprecise. A dedicated enum value would be cleaner. Low risk.

2. **EditorSubsystem path discovery** for JSON roots uses relative paths from `BaseDirectory`. Works in the standard layout; may need a configurable root path for non-standard deployments (PU-501 addresses this properly with path-at-creation).

3. **No file-watcher integration** in `BTreeJsonAssetContributor`/`HsmJsonAssetContributor` yet. The batch spec says "Discover on startup + on the asset-source file-watcher" — startup wiring is done; file-watcher trigger is deferred to PU-401/PU-501 (which sets up the full save/watch pipeline).

4. **`HsmAsset` `Blob`/`Metadata` backing-field change:** all 341 HSM editor tests pass, confirming no callers were broken. No serialization impact (these are not serialized fields).

---

## Suggested Commit Message

```
feat(persistence): JSON dual-load contributors + post-reload stitching (BATCH-05)

PU-301: BTreeJsonAssetContributor + HsmJsonAssetContributor — file-based
IAssetCatalogContributor using ReadHeader (header-lazy, malformed-skip) +
BehaviorTreeAssetMapper.ToModel/HsmAssetMapper.ToModel (IsEditorOwned=true,
SourceFilePath=<.json path>); JSON wins AssetId collision (added last in
AiAssetCatalogBuilder); wired into EditorSubsystem (dormant — no .json yet).
PU-301 acceptance: valid .btree.json + no assembly → Open succeeds,
ReconcileFromCatalog(empty) leaves document open, topology intact, no throw.
PU-302: IStitchableAsset interface; AiDocument.StitchRuntimeIndices;
BehaviorTreeAsset.StitchKernelIndices (VisualId→KernelBlobIndex, Blob updated,
debugSession.SetDebugMetadata re-wired, unmatched→sentinel+diagnostic);
HsmAsset.StitchKernelIndices (StableId→FlatIndex, TransitionVisualId→FlatIndex,
UpdateBlob); ReconcileFromCatalog kind-guard (BTree/Hsm+IsEditorOwned→stitch,
Blueprint/hand-authored→full-replace unchanged). MUST NOT MarkDirty anywhere
(PU-602 constraint). 19 new headless tests green; 0 new Blueprint failures
(DEBT-006 baseline 7 confirmed unchanged).
```
