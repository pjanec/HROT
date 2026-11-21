# BATCH-28: `AssetPickerSource` + per-kind/folder icon registration (resolves DBT-1)

**Batch Number:** BATCH-28
**Tasks:** MTB-P8-T2 (`AssetPickerSource` + recognizable per-kind & folder icons)
**Phase:** Phase 8 — Asset Picker UX via NodeEdit's picker (Tree layout)
**Estimated Effort:** ~7h
**Priority:** HIGH (T3 wiring depends on this)
**Dependencies:** BATCH-27 (MTB-P8-T1 — `PickerEntry.IconKey`, Tree icon/highlight rendering) — already merged.

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Engineering rules (MUST follow):** `.dev/.guides/DEV-GUIDE.md`
2. **Design doc:** `.dev/_DONE/main-toolbar-1/ASSET-PICKER-UX-DESIGN.md` — "Editor integration (assets as a NodeEdit
   picker source)" and **Decision D3**.
3. **Task definition:** `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → **`MTB-P8-T2`** (the acceptance bar).
4. **Decision context (read these rows):** `.dev/_DONE/main-toolbar-1/DEBT-TRACKER.md` → **DEC-15** (why the
   source is opened via the entry-driven `OpenPicker` path, not `registry.Open`) and **DBT-1** (icon debt
   this batch resolves).

### Source Code Location (all paths relative to repo root)
- **NEW source:** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerSource.cs`
- **PickerEntry (now has `IconKey`):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerEntry.cs`
- **IPickerSource<T> interface:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IPickerRegistry.cs`
- **Reference IPickerSource impls:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs`
- **Catalog + asset model:** `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs`,
  `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs`,
  `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs`
- **Per-kind icon keys:** `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKindIcons.cs` (`GetIconKey(kind)`)
- **Kind filter + base-folder helper:** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs`
  (`AssetKindFilter`, `AssetKindFilterMapping.PermittedKinds`, `internal static BaseFolderFor(kind)`)
- **Relpath helper (reuse for subfolder grouping):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetRelPath.cs`
  (`RelPath(asset, baseFolder)`)
- **Icon provider + cell map to extend:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs`
  (`DefaultCellMap`, `KeyToCellMap`, `TryGet`)
- **Test project:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/` (xUnit; example icon test:
  `Adapters/AIE002_SilkIconProviderTests.cs`; atlas-faking pattern: `new IconAtlas(99, 256f, 256f, 16f)`)

### Report Submission
When done, write `.dev/_DONE/main-toolbar-1/reports/BATCH-28-REPORT.md`.

---

## Context

T1 added `PickerEntry.IconKey` and made NodeEdit's Tree layout draw leaf type-icons (via
`ctx.Icons.TryGet(IconKey)`), folder icons, and fuzzy highlights. This batch adds the **editor-side asset
source** that projects the asset catalog into `PickerEntry` values the Tree picker can show, plus confirms
the per-kind/folder icon keys are distinct and resolvable (DBT-1). **No NodeEdit changes in this batch.**

**Important (DEC-15):** the source is authored as an `IPickerSource<IEditableAsset>` (its asset→entry
**projection** is the unit-tested seam), but in production (T3, next batch) it will be opened via the
entry-driven `IPickerRegistry.OpenPicker(PickerRequest{ ItemsProvider = source.BuildEntries, Layout=Tree })`
path — because the source-driven `registry.Open` path discards `Category`/icon. So this batch must expose a
**public projection** (`ToEntry` / `BuildEntries`) that T3 can feed into a `PickerRequest`.

---

## ✅ Tasks

### Task 2.1 — `AssetPickerSource : IPickerSource<IEditableAsset>`

**File (NEW):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerSource.cs`
**Namespace:** `Hrot.Editor.AiShared.Browser`

Constructor (use injectable seams so it is headless-deterministic — do NOT touch the real filesystem in
logic that tests exercise):

```csharp
public AssetPickerSource(
    IAssetCatalog catalog,
    AssetKindFilter kinds = AssetKindFilter.All,
    Func<AssetKind, string?>? baseFolderResolver = null,   // default: AssetBrowserPanel.BaseFolderFor
    Func<IEditableAsset, string?>? describe = null);        // default: _ => null  (recipe metadata → Description)
```

Behavior:
- **`Query(text, context)`** → assets from `catalog.All` whose `Kind` is permitted by `kinds`
  (`AssetKindFilterMapping.PermittedKinds(kinds)`), optionally filtered by `text`
  (case-insensitive `Name.Contains`). Return `IReadOnlyList<IEditableAsset>`. `QueryAsync` wraps it.
- **`ToEntry(IEditableAsset asset)` → `PickerEntry`** (PUBLIC; the projection seam):
  - `Id` = `GetItemKey(asset)` (stable — see below).
  - `Name` = `asset.Name`.
  - `Description` = `describe(asset)` (recipe metadata when present; else null).
  - `Category` = **kind-grouped path**:
    - relpath = `AssetRelPath.RelPath(asset, baseFolderResolver(asset.Kind))`.
    - subfolder = the **directory part** of relpath (everything before the last `/`); empty if none.
    - **All / multi-kind** (`kinds` permits >1 kind): `Category = subfolder.Length>0 ? $"{KindLabel}/{subfolder}" : KindLabel` where `KindLabel = asset.Kind.ToString()` (e.g. `"Blueprint"`, `"Hsm"`).
    - **single-kind variant** (`kinds` permits exactly 1 kind): `Category = subfolder.Length>0 ? subfolder : null` (no kind prefix).
  - `Keywords` = null; `IconTextureId` = null.
  - `IconKey` = `AssetKindIcons.GetIconKey(asset.Kind)`.
  - `Tag` = `asset` (the `IEditableAsset` — this is what T3's router consumes).
- **`BuildEntries(string text, IReadOnlyDictionary<string,object?>? context)` → `IReadOnlyList<PickerEntry>`**
  (PUBLIC) = `Query(text, context).Select(ToEntry).ToList()`. (T3 wires this as `PickerRequest.ItemsProvider`.)
- **`GetItemKey(asset)`** = `asset.AssetId.ToString()` (stable across queries).
- **`GetSearchableText(asset)`** = `asset.Name`.
- `Title` = `"Open Asset"`; `EmptyResultText` = `"No assets found."`;
  `PreferredLayout = PickerLayout.Tree`; `SelectionMode = PickerSelectionMode.Single`;
  `Cost = QueryCost.Cheap`; `IsAsync=false`; `AllowsDragOut=AllowsDragIn=AllowArbitraryTextInput=false`.
- `RenderItem` / `RenderPreview` — minimal, guarded like `BlueprintPickerSources` (`if
  (ImGui.GetCurrentContext() != IntPtr.Zero) ImGui.TextUnformatted(asset.Name)` / Description). These are
  NOT on the production render path (Tree uses `PickerEntry` directly) but must satisfy the interface
  safely.
- `IsPreviewExpensive` → false; `CanAcceptDrop` → false.

Provide a **scenario-filtered variant** simply as `new AssetPickerSource(catalog, AssetKindFilter.Scenario)`
(no new type needed — the `kinds` arg drives it). If you add a convenience factory/registration helper,
keep it thin.

### Task 2.2 — Per-kind & folder icon keys: distinct + resolvable (resolves DBT-1 testable part)

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs` (UPDATE only if needed)

- The default cell map already maps `asset/blueprint|btree|hsm|scenario|blackboard|utility` + `folder` +
  `folder_open`. **Verify** that the **6 asset-kind cells + `folder` + `folder_open`** are **pairwise
  distinct** (no two share an atlas cell). If any collide, reassign to distinct, semantically-appropriate
  silk cells. (Cross-context reuse with unrelated `bt/*`/`hsm/*` keys is acceptable; only the asset-kind +
  folder set must be internally distinct.)
- Do NOT remove or repurpose existing keys used elsewhere (`bt/*`, `hsm/*`, `bp/*`, `status/*`, `debug/*`,
  `perspective/*`, `browser/open`, `asset/new`). No scope creep.

> Note: true visual "recognizability" can only be confirmed at runtime against the real atlas art; the
> testable contract here is **distinct + resolvable**. Record any cell you change in the report.

---

## 🔄 MANDATORY WORKFLOW
1. Implement Task 2.1 → build `Hrot.Editor.AiShared` → write + run `AssetPickerSourceTests` → all pass ✅
2. Task 2.2 → add the icon-distinctness test → all pass ✅
3. Run the **full** `Hrot.Editor.AiShared.Tests` suite green (WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`).

Finish everything until green, then write the report. No asking permission for obvious steps; no stubbing.

---

## 🧪 Tests Required (exact names — acceptance bar)

**File (NEW):** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetPickerSourceTests.cs`

Use a small fake `IEditableAsset` + fake `IAssetCatalog` (return a fixed `All` list). Inject a deterministic
`baseFolderResolver` (e.g. `kind => "C:/proj/Assets/Blueprints"`) and craft fake assets whose
`SourceFilePath` = `<base>/AI/Foo.bp.json` so relpath = `"AI/Foo.bp.json"`.

- `Entries_HaveKindGroupedCategory_AndPerKindIcon_AndAssetTag` — for an All-kinds source: a Blueprint asset
  at subfolder `AI` ⇒ `Category == "Blueprint/AI"`, `IconKey == AssetKindIcons.GetIconKey(AssetKind.Blueprint)`
  (== `"asset/blueprint"`), and `Tag` is the **same** `IEditableAsset` instance. Also assert an asset with no
  subfolder ⇒ `Category == "Blueprint"`.
- `ScenarioVariant_YieldsOnlyScenarios` — catalog holds mixed kinds; `new
  AssetPickerSource(catalog, AssetKindFilter.Scenario)` `.Query("")` (and `BuildEntries`) returns ONLY
  `Scenario`-kind items.
- `GetItemKey_StableAcrossQueries` — querying twice returns the same `GetItemKey` for the same asset
  (== `asset.AssetId.ToString()`).
- `Description_FromRecipeMetadata_WhenPresent` — inject `describe = a => a == theAsset ? "Recipe desc" :
  null`; `ToEntry(theAsset).Description == "Recipe desc"`; another asset ⇒ `Description == null`.
- `SingleKindVariant_OmitsKindPrefixInCategory` — `new AssetPickerSource(catalog,
  AssetKindFilter.Blueprint)` ⇒ a Blueprint at subfolder `AI` has `Category == "AI"` (no `"Blueprint/"`
  prefix); no subfolder ⇒ `Category == null`.

**File (NEW or extend `AIE002_SilkIconProviderTests`):**
`Hrot/Editor/Hrot.Editor.AiShared.Tests/Adapters/AssetKindIconsRegistrationTests.cs`
- `EachAssetKind_ResolvesToDistinctIcon_NoSharedCell` — for every `AssetKind` value,
  `SilkIconProvider.TryGet(AssetKindIcons.GetIconKey(kind), out _)` is true, AND the 6 resolved cells
  (`KeyToCellMap[GetIconKey(kind)]`) are **pairwise distinct**.
- `FolderIcons_ResolveAndAreDistinct` — `TryGet("folder")` and `TryGet("folder_open")` both true and their
  cells differ.

> Tests must assert **actual values** (Category strings, IconKey, Tag identity, distinct cells), not
> "not null". No `[Skip]`, no tautologies, no asserting a mock you just configured without exercising the
> projection.

---

## 🎯 Success Criteria (from TASK-DETAIL MTB-P8-T2)
- [ ] `AssetPickerSource` projects catalog → `PickerEntry` with kind-grouped `Category`, per-kind `IconKey`,
      `Tag = IEditableAsset`, recipe `Description`; `PreferredLayout=Tree`, `SelectionMode=Single`.
- [ ] Scenario-filtered variant (`AssetKindFilter.Scenario`) yields only scenarios.
- [ ] `GetItemKey` stable; single-kind variant omits the kind prefix.
- [ ] 6 asset-kind icon keys + `folder`/`folder_open` resolve and are pairwise distinct (DBT-1 testable part).
- [ ] Public `BuildEntries`/`ToEntry` projection exposed for T3's `PickerRequest.ItemsProvider`.
- [ ] All `AssetPickerSourceTests` + icon-registration tests pass; full `Hrot.Editor.AiShared.Tests` green.

---

## ⚠️ Hard Constraints
- **No NodeEdit changes** in this batch (T1 already added what's needed). Do NOT modify
  `IPickerSource<TItem>` or any `NodeEditor.*` file.
- **No production wiring** here (registry registration + entry points are T3). This batch is the source +
  icons + tests only. (You MAY add a thin `AssetPickerSources.Register` helper if convenient, but do not
  wire it into `EditorSubsystem`.)
- **No scope creep / no deletions** — only the files listed. Do NOT touch `AssetPickerModal`,
  `AssetBrowserPanel` behavior, or the docked browser.
- Reuse `AssetRelPath`, `AssetKindFilterMapping`, `AssetBrowserPanel.BaseFolderFor`, `AssetKindIcons` — do
  NOT duplicate relpath/kind logic. (`BaseFolderFor` is `internal` in the same assembly — accessible.)
- Headless-deterministic logic (injected resolver/describe); never depend on real disk in tested paths.
- Zero new warnings; no `TODO`/`NotImplementedException` on covered paths; no test weakening.

---

## 📊 Report Requirements (`reports/BATCH-28-REPORT.md`)
- The `AssetPickerSource` shape, the Category derivation (All vs single-kind), and the `ToEntry`/`BuildEntries`
  seam T3 will use.
- Whether any icon cell was reassigned (and to what), and the distinctness result.
- Full `Hrot.Editor.AiShared.Tests` run summary (counts, 0 failed).
- Any edge cases / weak points; suggested commit message.

If something cannot be done as specified, **stop and report why** rather than stubbing it.
