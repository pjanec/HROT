# DEC-08 — Picker categories + icons (was DEC-03c)

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** NodeEditor.UI picker framework (shared) + Hrot.BTree.Editor source. **Size: large.**

User: the node picker shows a single flat "All" list with no category grouping and no icons. The catalog provides both (`NodeCatalogEntry.CategoryPath`, `.IconKey`; `BTreeNodeCatalog.Categories` = Composites/Leaves/Decorators/ReactiveGuard) and `SilkIconProvider` registers all `bt/*` icon keys — but the picker drops the metadata.

## Root cause (verified)
- `PickerWindow.cs:117-118` builds `new PickerEntry(it.Key, it.SearchText, null, null, null, null, it.Raw)` — `Description`/`Category`/`Keywords`/`IconTextureId` all hardcoded `null`. The source-driven path carries only `AdaptedItem(Key, SearchText, Raw)` (`PickerSourceAdapter.cs:45`) — no category/icon channel.
- `PickerItemListHelper.DrawItems` (`PickerItemListHelper.cs`) groups rows only by Favorite/Recent; `DrawRow` renders the name text only — **no icon, no category header**.
- `PickerWindow.cs:~530` implements `IIconProvider.TryGet` as a **stub returning false** — even a set IconKey wouldn't resolve. The real provider is `SilkIconProvider` (host bundle's `IconProvider`), which maps `bt/sequence`→cell etc.
- `IPickerSource<TItem>` (interface, ~line 37 of a Picker file) has `GetSearchableText`/`GetItemKey`/`RenderItem` but no category/icon getters. `BTreeNodePickerSource` (`BTreePickerSources.cs`) returns raw `NodeCatalogEntry`s.

## Implementation

### Part A — thread Category + IconKey to PickerEntry
1. `IPickerSource<TItem>`: add two default-implemented members so existing sources are unaffected:
   ```csharp
   string? GetCategory(TItem item) => null;
   string? GetIconKey(TItem item) => null;
   ```
2. `AdaptedItem` (`PickerSourceAdapter.cs:45`): add `string? Category` and `string? IconKey`. Populate in BOTH `PickerSourceAdapter.Query` and `QueryAsync` from `_source.GetCategory(i)` / `_source.GetIconKey(i)`.
3. `PickerWindow.cs:117-118`: map `it.Category` into the `PickerEntry.Category` slot and `it.IconKey` into the `IconKey` slot (replace the two `null`s for those positions; keep Description/Keywords/IconTextureId null).
4. `BTreeNodePickerSource` (`BTreePickerSources.cs`): `public string? GetCategory(NodeCatalogEntry e) => e.CategoryPath;` and `public string? GetIconKey(NodeCatalogEntry e) => e.IconKey;`.

### Part B — group the list by category
In `PickerItemListHelper.DrawItems`, within the "normal" (non-favorite, non-recent) items, draw a **category header** (muted text, like the Favorites/Recent headers) whenever `Entry.Category` changes from the previous normal row. Requires the normal items to be ordered by category — confirm the ordering source (`PickerState.Refilter` / `state.Filtered`); if items aren't category-ordered, group them for display (e.g. a stable sort by Category within the normal section that preserves score order inside each category). Do NOT reorder Favorites/Recent. When a search query is active, grouping may be suppressed (ranking dominates) — acceptable; headers matter most for the empty-query browse case. Keep the existing virtualization/clipper working (or disable the clipper only when grouping is active and result count is small — BTree has ~15 entries).

### Part C — row icons via the real icon provider
1. Plumb the host `IIconProvider` (the bundle's `IconProvider` — `SilkIconProvider`) into the picker so `IconKey` resolves. Trace how `PickerWindow` / the picker render context is constructed (the `##canvas_*` picker is opened via `view.Host.Pickers.Open`; the `IPickerRenderContext` / `PickerWindow`'s `IIconProvider` is the stub at ~line 530). Replace the stub with the real provider — inject it when the picker is created/opened (e.g. pass the host `IIconProvider` into the `PickerRegistry`/`PickerWindow`, or expose it on `IPickerRenderContext`). Find the cleanest injection point; the BTree host has it via `bundle.IconProvider` / `IEditorHostServices`.
2. In `PickerItemListHelper.DrawRow`, before the name text, if `re.Entry.IconKey` resolves via the provider (`TryGet(key, out IconHandle h)`), draw it (`ImGui.GetWindowDrawList().AddImage` with the handle's texture + UV rect, sized ~16px square) and advance `textX` by ~20px. If it doesn't resolve, render as today (no icon, no gap regression). Mirror how nodes render inline icons elsewhere (e.g. how `ContainerRenderer`/node header draws `IconHandle` via the icon provider) for the exact ImGui call + UV usage.

### Part D (optional, only if cheap) — category filter panel
`PickerRequest.CategoryRoot` (a `CategoryNode` tree) drives the left filter panel that currently shows only "All". If the open path lets a source supply a `CategoryRoot`, build one from `BTreeNodeCatalog.Categories` and pass it. If this requires non-trivial plumbing, SKIP it and note in the report — Parts A–C deliver the visible win (grouped, icon'd list).

## Constraints
- Additive to `IPickerSource` (default members) — HSM/Blueprint/other pickers keep working unchanged. The icon-provider injection must default safely (stub/no-op) if a host supplies none.
- No `.btree.json`/codegen changes. (If a Parallel CS7036 appears building the test project, `dotnet build-server shutdown` then rebuild — stale analyzer, not your bug.)

## Tests
- `PickerSourceAdapter` populates `AdaptedItem.Category`/`IconKey` from the source (small fake source).
- `BTreeNodePickerSource.GetCategory`/`GetIconKey` return the entry's `CategoryPath`/`IconKey`.
- If `DrawItems` grouping logic can be factored into a testable pure helper (e.g. "compute display order + header positions from entries"), test it; ImGui draw itself isn't unit-testable — note that honestly.

## Verification (run + paste RAW output)
1. `dotnet build` NodeEditor.Core, NodeEditor.UI, the NodeEditor.Demo (it has picker scenarios), `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, `Hrot.Blueprints.Editor` → 0 errors.
2. `dotnet test` NodeEditor.UI.Tests, NodeEditor.Core.Tests, `Hrot.BTree.Editor.Tests` → counts; no new failures vs baseline (Core 195/0, UI 78/0, BTree.Editor 556/0).

## Report back
Per part: what changed; how the icon provider was injected (and the fallback); whether category grouping is shown always or only on empty query; whether Part D was done or skipped (why); raw build + test output. **Do NOT commit** — lead reviews & commits. (Visual confirmation will be the user opening the picker.)
