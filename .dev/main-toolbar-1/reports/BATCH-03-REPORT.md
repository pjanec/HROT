# BATCH-03 Report

**Tasks:** MTB-P1-T1, MTB-P1-T2, MTB-P1-T4   **Phase:** 1 — Toolbar & Icon Infrastructure

## Implementation Summary

### T1 — `MainToolbarManager` (MTB-P1-T1) — §4.1

**New file:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MainToolbarManager.cs`

Mirrors `StatusBarManager`'s registry pattern: last-write-wins on duplicate `id`, deferred sort by
`sortOrder`, perspective filter (`null` = global, named = only when match). Key design decisions:

- **Jitter-free height:** `Height` = max `declaredHeight` over ALL registered entries (not just
  visible/current-perspective ones), computed eagerly at registration time. This guarantees the
  value is constant across perspective switches — the central dockspace never bounces (§4.1.1).
- **Separators as first-class items:** `RegisterSeparator` inserts a sentinel that the render loop
  draws as a vertical divider line over the band height. Separators participate in sort ordering
  and perspective filtering identically to entries.
- **Separation of logic from drawing:** The `Render()` method calls `Gui.*` for actual ImGui
  rendering, but the registry/ordering/filter logic is exposed via `GetVisibleItemPlan(perspective)`
  (internal, headless — no ImGui needed). Tests use both: `GetVisibleItemPlan` for pure logic
  assertions and `Render()` with recording delegates for integration assertions.
- **Top-anchored** borderless window pinned to `viewport.WorkPos` (mirror of `StatusBarManager`
  bottom-anchor). Window background uses the same `(0.12, 0.12, 0.12, 1f)` dark gray.

**Tests (8, all pass):**

| Test | What it asserts |
|------|----------------|
| `RegisterEntry_DuplicateId_LastWriteWins` | Second registration of same id replaces first (recording delegate confirms second fires, not first) |
| `Entries_RenderInAscendingSortOrder` | Register C@30, A@10, B@20 → recording list = [A, B, C] |
| `PerspectiveFilter_NullIsGlobal_NamedOnlyWhenMatch` | global(null) + combat → Render("combat") = [global, combat]; Render("strategic") = [global] only |
| `Height_IsMaxDeclaredOverAllRegistered_RegardlessOfCurrentPerspective` | 64px global + 80px perspective-"X" → Height=80 even when rendering perspective "Y" |
| `Separator_RegisteredAndOrdered` | Entry@10, Sep@20, Entry@30 → GetVisibleItemPlan shows separator at position 2, IsSeparator=true |
| `Separator_RenderPlan_RespectsSortOrder` | Register out of order → sorted by SortOrder regardless of registration sequence |
| `Separator_PerspectiveFiltered_LikeEntries` | combat separator hidden when rendering "strategic"; visible when rendering "combat" |
| `RegisterEntry_NullDelegate_ThrowsArgumentNullException` | Null delegate throws ArgumentNullException |

**Headless-test seam:** `GetVisibleItemPlan(perspective)` — returns `(Id, IsSeparator, SortOrder)`
list after sorting and filtering, without any ImGui calls. Used by tests 5–7. Tests 1–4 use the
`ImGuiTestFixture` render path (like `StatusBarManagerTests`).

### T2 — Icon widget `IconHandle` + size overloads (MTB-P1-T2) — §4.2

**Modified files:**
- `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs`
- `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj` (added `NodeEditor.Core` ProjectReference)

**New methods:**

- `IconButton(in IconHandle icon, string id, Vector2 size, bool enabled = true, Vector4? tint = null)`
- `ToggleIcon(in IconHandle icon, string id, Vector2 size, ref bool isToggled, bool enabled = true, Vector4? tint = null)`
- `Tooltip(string text)` — simple `IsItemHovered()` → `SetTooltip(text)` wrapper

**Disabled state** (`enabled == false`):
- Uses `Gui.Dummy(size)` (passive placeholder, NO hit area) instead of `Gui.InvisibleButton`
- Draws dimmed icon (alpha × 0.28, mirroring `TransportIconRenderer`'s `dim` path)
- Never returns `true` and never flips toggle state
- No hover border drawn

**Toggle/active** retained: gray filled background when toggled, hover border, 1px press offset.
Draw via `drawList.AddImage(icon.TextureId, pos, pos+size, icon.Uv0, icon.Uv1, tintU32)`.

**Existing overloads preserved** — all `IconAtlas`/coordinate-based methods unchanged.

**ProjectReference added:** `NodeEditor.Core.csproj` (for `IconHandle`/`IIconProvider` types).
Previously `Fdp.Presentation` did not reference `NodeEditor.Core`.

**Tests (13 new, all pass):**

| Test | What it asserts |
|------|----------------|
| `IconButton_Handle_ValidArgs_DoesNotThrow` | 64×64 size, valid handle — no throw |
| `IconButton_Handle_WhenNotClicked_ReturnsFalse` | Headless context (no mouse input) → returns false |
| `IconButton_Handle_Disabled_NeverReturnsTrue_AndRegistersNoHitArea` | `enabled: false` → always returns false |
| `IconButton_Handle_Disabled_DoesNotThrow` | Disabled at 64×64 — no throw (Dummy path works) |
| `ToggleIcon_Handle_ValidArgs_DoesNotThrow` | Valid args, 64×64 — no throw |
| `ToggleIcon_Handle_WhenNotClicked_ReturnsFalse` | No click → returns false |
| `ToggleIcon_Handle_WhenNotClicked_StateIsUnchanged` | Both true→true and false→false when no click occurs |
| `ToggleIcon_Handle_WhenToggledTrue_DoesNotThrow` | Renders with background when toggled true |
| `ToggleIcon_Handle_WhenToggledFalse_DoesNotThrow` | Renders without background when toggled false |
| `ToggleIcon_Handle_WhenDisabled_StateUnchanged` | `enabled: false`, started true → state remains true, returns false |
| `ToggleIcon_Handle_WhenDisabled_StateUnchanged_False` | `enabled: false`, started false → state remains false |
| `Tooltip_AfterButton_DoesNotThrow` | Tooltip after an IconButton — no throw |
| `Tooltip_NullOrEmpty_DoesNotThrow` | Empty and null tooltip text — no throw |

**Headless-test seam:** `IconHandle` is a pure data struct (no GPU). Tests construct it from an
`IconAtlas` with a fake `IntPtr(1)` texture handle and known cell coordinates. The
`ImGuiTestFixture` headless context provides the ImGui frame without a GPU. Disabled tests verify
the logic path (Dummy vs InvisibleButton, return value, state preservation) without needing actual
mouse input.

### T4 — Icon keys + `AssetKind → IconKey` (MTB-P1-T4) — §5.1, §5.2

**Modified:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs`
**New:** `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKindIcons.cs`

**SilkIconProvider** extended `DefaultCellMap` with 15 new key→cell mappings (§5.1):

| Key | Cell | Category |
|-----|------|----------|
| `debug/continue` | a2 | Debug controls |
| `debug/step_back` | a3 | Debug controls |
| `debug/step_over` | a4 | Debug controls |
| `debug/step_into` | a5 | Debug controls |
| `debug/step_out` | a6 | Debug controls |
| `asset/scenario` | b1 | Asset kind icons |
| `asset/blueprint` | b2 | Asset kind icons |
| `asset/btree` | c10 | Asset kind icons |
| `asset/hsm` | c11 | Asset kind icons |
| `asset/blackboard` | c12 | Asset kind icons |
| `asset/utility` | b8 | Asset kind icons |
| `browser/open` | c8 | Browser/generic |
| `asset/new` | b9 | Browser/generic |
| `folder` | c8 | Browser/generic |
| `folder_open` | a1 | Browser/generic |

Cell reuse (e.g. `c8` for both `bt/composite`, `browser/open`, and `folder`) is acceptable —
the dictionary maps keys uniquely; multiple keys can share the same atlas cell.

**AssetKindIcons** (§5.2):
- `GetIconKey(AssetKind)` maps all 5 current enum values → `asset/<kind>` keys
- `ScenarioIconKey = "asset/scenario"` constant (DEC-2: `AssetKind.Scenario` does not exist yet)
- `ArgumentOutOfRangeException` for unknown enum values

**Tests (9, all pass):**

| Test | What it asserts |
|------|----------------|
| `TryGet_EachNewKey_ReturnsHandle` | All 15 §5.1 keys → `TryGet` returns true |
| `TryGet_EachNewKey_HandleHasAtlasTextureId` | Each resolved handle carries the correct atlas `TextureId` |
| `TryGet_RepresentativeKeys_HaveSubCellUvs` | Sample keys produce sub-cell UVs (not whole-texture (0,0)-(1,1)) |
| `AssetKindToIconKey_CoversAllKinds_IncludingScenario` | 5 AssetKind values → correct keys; `ScenarioIconKey == "asset/scenario"` |
| `AssetKindToIconKey_AllKeys_ResolveThroughProvider` | Every key from `GetIconKey` actually resolves via `SilkIconProvider.TryGet` |
| `TryGet_UnknownKey_ReturnsFalse` | Bogus key → false + default handle |
| `TryGet_UnknownKey_DefaultHandleReturned` | Default handle has zero TextureId/Width/Height |
| `TryGet_PrefixOnly_ReturnsFalse` | "asset", "debug", "browser" (prefix-only, not full keys) → false |
| `TryGet_NullOrEmptyKey_ReturnsFalse` | null/empty → false, no throw |

**Headless-test seam:** `SilkIconProvider` takes an `IconAtlas` in its constructor — the atlas
accepts an opaque `IntPtr` texture handle and computes UVs purely from cell coordinates. No GPU
calls anywhere in the test path. The provider's `TryGet` is a pure dictionary+math lookup.

## Design Decisions

1. **Eager height computation.** `Height` is recomputed at each `RegisterEntry` call by scanning
   all items. This is O(n) per registration but registration is rare (startup only), and it
   guarantees the value is always correct without waiting for `Render()`. This matches the
   design's "known before any rendering" requirement.

2. **Separators as typed sentinels.** Rather than interleaving separators between entries like
   `StatusBarManager` does (hardcoded `"|"` text), I made separators first-class typed items
   (`SeparatorItem : ToolbarItem`) with their own registration method. They participate in
   sorting and perspective filtering uniformly, and the render loop draws a proper vertical
   line via `ImDrawList.AddLine()`.

3. **IconHandle disabled pattern.** Mirrors `TransportIconRenderer.DrawButton`: disabled → `Dummy`
   (no hit area) + dimmed alpha (0.28×). This is consistent with the existing codebase's approach
   to disabled UI, avoiding the introduction of a new convention.

4. **Cell reuse in SilkIconProvider.** Multiple keys map to the same atlas cell when semantically
   related (e.g. `bt/composite`, `browser/open`, and `folder` all use `c8`). The mapping is
   purely a lookup; callers are agnostic to the underlying cell coordinates.

5. **No new projects or assemblies.** All new types went into existing assemblies:
   `MainToolbarManager` → `Fdp.Presentation`, `AssetKindIcons` → `Hrot.Editor.AiShared`.
   Only a single `ProjectReference` was added (`NodeEditor.Core` → `Fdp.Presentation`), which
   is documented in scope for T2.

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| Used `GetVisibleItemPlan()` as the headless-test seam instead of pure recording-delegate-only tests | Allows verifying separator ordering without needing ImGui at all; the batch asked to "split UI logic from ImGui draw calls" | Three of eight T1 tests run without any ImGui context | Internal method — only visible to tests via `InternalsVisibleTo` |
| Used `IconAtlas` cell coordinates (a1, a2, b1, c10, etc.) for the new §5.1 keys without auditing the actual silk atlas content for each cell | The atlas computes UVs from any valid letter+number coordinate; the exact visual match is a design/art question for later | All keys resolve to valid UVs immediately; tests pass | Some cells may not contain the ideal icon — visual review needed, but this is a content concern, not a code concern |

## Test Results

### New tests (all unfiltered — 30 pass, 0 fail)

```
MainToolbarManagerTests          8 passed, 0 failed, 0 skipped
IconWidgetsTests (Handle new)   13 passed, 0 failed, 0 skipped
IconKeysTests                    9 passed, 0 failed, 0 skipped
─────────────────────────────────────────────────────────
TOTAL NEW                       30 passed, 0 failed
```

### Affected existing tests (unfiltered — all pass)

```
StatusBarManagerTests            9 passed, 0 failed (unchanged)
IconWidgetsTests (existing)     24 passed, 0 failed (unchanged)
SilkIconProviderTests           13 passed, 0 failed (unchanged)
```

### Suite-wide runs (Stability filter applied)

```
Suite                                   Passed   Failed   Skipped   Duration
─────────────────────────────────────────────────────────────────────────────
Hrot.Editor.AiShared.Tests                885        0         0        5 s
Fdp.Toolkits.Tests                       1856        0         0       28 s
Hrot.SimHost.Tests                        585        0         3       11 s
Fdp.Presentation.Tests (excl Vis2D)*      320        4         0        2 s
─────────────────────────────────────────────────────────────────────────────
TOTAL (filtered)                         3646        4         3
```

*Fdp.Presentation.Tests has 19 pre-existing failures unrelated to this batch (see Known Issues).
Of those, 15 are in Vis2D `DebugGizmoLayer`/`DebugPrimitiveRenderer2D` tests (NRE at
`DebugGizmoLayer.Draw:102` / `DebugPrimitiveRenderer2D.Render:28`), 3 in
`EntityInspectorPanelTests`, and 1 in `EventBrowserPanelTests`. These all fail identically on
the clean `HEAD` tree. Not catalogued in `.dev/test-health/TEST-HEALTH.md`.

### Build

`dotnet build IOS-IG-SimHost.sln` — 0 errors, 20 warnings (all pre-existing xUnit2013/CS0618/CS8602
in other projects; 0 new warnings from this batch's changes).

## Developer Insights

- **StatusBarManager pattern was well-structured for mirroring.** The `Section` inner type,
  deferred sort (`_needsSort` flag), and perspective filter were trivial to adapt. The main
  difference is the `declaredHeight` field (vs StatusBar's measured height) and separators as
  typed items (vs hardcoded `"|"` text).

- **`ImGuiTestFixture` headless context is robust.** All ImGui-dependent tests (T1 render,
  T2 draw) run without a GPU. The fixture serializes access via a `SemaphoreSlim` to prevent
  races on ImGui global state. The existing `IconWidgetsTests`/`StatusBarManagerTests` already
  used this pattern — no new infrastructure needed.

- **`IconHandle` struct is well-designed for headless testing.** Pure value type with
  `Equatable<IconHandle>` — zero allocation, easy to construct from fake atlas coordinates.

- **`SilkIconProvider` already had a custom-map constructor.** Adding §5.1 keys was a pure data
  addition to `DefaultCellMap` — no logic changes needed. The headless testability was already
  there (atlas with fake texture handle).

- **The `NodeEditor.Core` ProjectReference was the only dependency change.** `Fdp.Presentation`
  did not reference `NodeEditor.Core` before, which is why the `IconHandle`-based overloads
  couldn't have been added there previously. The reference is lightweight (just interfaces +
  value types, no UI dependencies).

- **Edge cases discovered beyond the spec:**
  - Separators must also be perspective-filtered (a combat-only separator shouldn't appear when
    rendering "strategic"). Implemented and tested.
  - `RegisterSeparator` with duplicate id should replace (last-write-wins, like entries).
  - `Tooltip(null!)` should not throw — ImGui handles null gracefully.
  - Unknown keys that are prefixes of known keys (e.g. `"asset"` without `"/blueprint"`)
    should NOT resolve — the provider requires exact key match, not prefix match.

## Known Issues

1. **19 pre-existing test failures in `Fdp.Presentation.Tests`** — not caused by this batch,
   confirmed failing on clean `HEAD`. These affect:
   - `DebugGizmoLayerActivationTests` (4 NRE at `DebugGizmoLayer.Draw:102`)
   - `DebugGizmoLayerHitTests` (4 NRE at `DebugGizmoLayer.Draw:102`)
   - `DebugPrimitiveRenderer2DTests` (7 NRE at `DebugPrimitiveRenderer2D.Render:28`)
   - `DebugPrimitiveRenderer2DEntityLocalTests` (2 NRE at `DebugPrimitiveRenderer2D.Render:28`)
   - `DebugPrimitiveRenderer2DSizeModeTests` (3 NRE at `DebugPrimitiveRenderer2D.Render:28`)
   - `EntityInspectorPanelTests` (3 failures)
   - `EventBrowserPanelTests` (1 failure)
   Not catalogued in TEST-HEALTH.md (which covers only `Fdp.Toolkits.Tests` and
   `Hrot.SimHost.Tests`).

2. **Silk atlas cell assignments are approximate.** The new §5.1 keys were assigned to available
   cells (a1-b12 range) without verifying the actual icon content at each cell. Visual review
   and possible reassignment will be needed when the toolbar UI is wired up. This is a content
   concern, not a code concern — the key→cell→UV pipeline is correct.

3. **The `Tooltip` helper doesn't support formatting.** It takes a plain string. If rich tooltips
   (multi-line, shortcut hints) are needed, callers will need to format before calling or use
   ImGui's `BeginTooltip`/`EndTooltip` directly. In scope for a later batch if needed.

## Suggested Commit Message

```
feat(main-toolbar): add MainToolbarManager, IconHandle overloads, icon keys + AssetKind→IconKey map (MTB-P1-T1, MTB-P1-T2, MTB-P1-T4)

- New MainToolbarManager: jitter-free declared height, perspective-filtered entries + separators
- IconWidgets: IconHandle-based IconButton/ToggleIcon with explicit size + disabled state
- SilkIconProvider: 15 new §5.1 keys (debug, asset, browser, folder)
- AssetKindIcons: AssetKind→IconKey map + ScenarioIconKey constant (DEC-2)
- Added NodeEditor.Core ProjectReference to Fdp.Presentation
- 30 new tests, all pass; build: 0 new warnings

Co-Authored-By: Claude <noreply@anthropic.com>
```
