# Main Toolbar 2 — Task Detail (Zoo-tailored)

**Design:** [DESIGN.md](./DESIGN.md) (decisions DEC-A1…A7). **Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md).
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md). **Dev rules:** [../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md).

Each task is **small + single-objective** and gets **its own batch** (Zoo loses focus on multi-objective work).
Tasks are sequential; obey phase-order (T1→T7). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

---

## ⚙️ Zoo Execution Contract — paste into EVERY batch verbatim

```
RULES (non-negotiable):
1. Do this batch's ONE objective only. Do NOT touch files outside the listed scope. Do NOT edit, re-litigate,
   or "improve" code from other batches/commits. No drive-by refactors or renames.
2. NEVER make a build/test pass by hiding the problem: do NOT exclude user assets from compilation, comment out
   or [Skip]/[Fact(Skip)] tests, delete/weaken assertions, stub with NotImplementedException, suppress
   diagnostics/warnings, or use #if false. If something cannot be done as specified, STOP and report why.
3. Add the EXACT named tests listed; they must assert real values/behavior (not Assert.True(true), not the mock
   you just configured). Tests must FAIL if the production code is wrong.
4. DO NOT STOP until: the project builds with 0 warnings AND the specified test command shows `Failed: 0`.
   Run tests WITHOUT setting BLUEPRINT_REGENERATE_SNAPSHOTS. Re-run after any fix until green.
5. Report: exact files changed, exact tests added, and paste the final `dotnet test` summary line(s). Do not
   claim "already existed" — describe what YOU changed. Leave no litter (no debug File.WriteAllText, temp files).
```

> Lead (me) verifies independently regardless of Zoo's report: read the diff + assertions, rebuild, run the
> suite myself without the regen flag, runtime-check visuals where noted, curate litter, then commit.

**Test-run notes (environment):**
- `Fdp.Presentation.Tests` **full** suite is flaky/deadlocks (DEBT PRE-2) → run **class-filtered**, e.g.
  `dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --filter "FullyQualifiedName~IconWidgets|FullyQualifiedName~ToolbarCommandAdapter|FullyQualifiedName~MainToolbarManager|FullyQualifiedName~WindowManagerMainToolbar|FullyQualifiedName~PerspectiveToolbar"`.
- Editor logic tests: `Hrot.Editor.AiShared.Tests` and `Hrot.Editor.Tests` run clean in full.
- If a batch touches anything Blueprints-adjacent, `Hrot.Blueprints.Tests` must stay at the **9 PRE-1** baseline
  (see DEBT PRE-1) — no NEW failures.

---

## MTB2-T1 — Generic toolbar icon UX: 90% inset + clear hover/toggle  {#mtb2-t1}
**Item 4 · Batch BATCH-30 · Model: `pro` · Design: DEC-A3**

**Objective:** make toolbar icons breathe (~90% inset) and show clear hover + toggled states — **generically**, in
the shared widget, so every toolbar icon benefits. Hitbox/layout must NOT change.

**Scope (only these files):**
- `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs` — the **`IconHandle` overloads** `ToggleIcon(in IconHandle,
  string id, Vector2 size, ref bool isToggled, bool enabled, Vector4? tint)` and `IconButton(in IconHandle, …)`
  (≈ L211–330). (Leave the `IconAtlas`-based overloads' behavior intact unless trivially shared.)
- (Read-only ref, do not change behavior) `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ToolbarCommandAdapter.cs`.
- Tests: `FDP/Engine/Fdp.Presentation.Tests/ImGui/Icons/IconWidgetsTests.cs`.

**Requirements:**
1. Add a pure, ImGui-free helper `public static (Vector2 Min, Vector2 Max) ComputeIconRect(Vector2 boxPos,
   Vector2 boxSize, float scale)` — returns the centered sub-rect at `scale` of the box (e.g. 0.9 → 90%, centered).
2. In the `IconHandle` `ToggleIcon`/`IconButton` overloads: keep the `InvisibleButton` (hit/spacing box) at the FULL
   `size`; draw the icon **image** into `ComputeIconRect(pos, size, IconScale)`. Add an optional `float iconScale =
   0.9f` parameter (default 0.9) — callers unchanged.
3. **Hover:** when `IsItemHovered() && enabled`, draw a clear **filled** hover highlight behind the icon (e.g.
   `SelectionAccent` at low alpha), not just the faint border.
4. **Toggled:** when `isToggled && enabled`, draw a clearly-readable "active" fill (accent-tinted), visually distinct
   from hover.
5. Disabled rendering stays dimmed; no hover/toggle visuals when disabled.

**Success conditions (exact test names, in `IconWidgetsTests.cs`):**
- `ComputeIconRect_CentersAtNinetyPercent` — for box (pos (0,0), size (20,20), scale 0.9): Min ≈ (1,1), Max ≈ (19,19),
  rect size ≈ (18,18), centered (equal margins).
- `ComputeIconRect_NeverExceedsBox` — scale 1.0 → rect == box; scale 0.5 → rect strictly inside, centered.
- `ComputeIconRect_DefaultScaleIsNinety` (or assert the overload's default) — the `IconHandle` overload uses 0.9 when
  `iconScale` is omitted.
- Build green (0 warnings); existing `IconWidgetsTests` + `ToolbarCommandAdapterTests` still pass (class-filtered).

**Lead runtime check (not Zoo):** verify in the live editor that toolbar icons have margin + visible hover + visible
toggle (perspective buttons). Visual judgment is mine; Zoo only proves `ComputeIconRect` + no regressions.

---

## MTB2-T2 — Save icon in the main toolbar  {#mtb2-t2}
**Item 2 · Batch BATCH-31 · Model: `pro` · Depends: T1 (cosmetic only)**

**Objective:** surface the existing `shell.save` command as an icon button in the MainToolbar, just right of
"Open Asset".

**Scope (only these files):**
- `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs` — add a `"shell/save"` cell to `DefaultCellMap`
  (distinct, recognizable "disk/save" silk cell; must not collide with the asset-kind / folder cells). Optionally add
  `"shell/saveAs"`, `"shell/saveAll"` (their commands already declare those `IconKey`s).
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — in `RegisterWindows`, register a `ToolbarCommandAdapter.Register(
  windowManager.MainToolbar, windowManager.ShellCommands, ShellSaveCommands.SaveId, toolbarIconProvider, sortOrder)`
  next to Open Asset (Open Asset is `sortOrder: -10`; use **`-9`** for Save). Null-safe (bare-ctor `RegisterWindows`
  must not throw).
- Tests: extend `Hrot.Editor.Tests` toolbar guardrail (`EditorSubsystem_RegisterWindows_*`) +
  `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/ToolbarCommandAdapterTests.cs` if a new behavior is added.

**Requirements:**
- Do NOT change `shell.save` behavior/keybinding (already Ctrl+S). Do NOT remove the old blueprint-specific save
  button in this batch.
- The Save icon's tooltip is whatever `ToolbarCommandAdapter` shows today (`DisplayName`); the **dynamic** tooltip
  comes in T4 — do not implement dynamic label here.

**Success conditions:**
- `SilkIconProvider.TryGet("shell/save", out _)` is true and its cell is distinct from the asset-kind/folder cells
  (add/extend a test like `Shell_Save_Icon_Resolves_DistinctCell` in `AssetKindIconsRegistrationTests` or the Silk
  test).
- A guardrail test asserts a `shell.save` entry exists in `MainToolbar` after `RegisterWindows` (e.g.
  `EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry`).
- Build green; `Hrot.Editor.Tests` `Failed: 0`; toolbar-class-filtered `Fdp.Presentation.Tests` `Failed: 0`.

---

## MTB2-T3 — `DynamicDisplayName` on `EditorCommandDescriptor` + adapters consume it  {#mtb2-t3}
**Item 3 · Batch BATCH-32 · Model: `pro` · Design: DEC-A6**

**Objective:** let a command supply a per-frame label, used by the menu item text and the toolbar tooltip. Generic;
prerequisite for T4's dynamic Save label.

**Scope (only these files):**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/IEditorCommands.cs` — add a trailing optional
  `Func<string>? DynamicDisplayName = null` to the `EditorCommandDescriptor` record (after `IsChecked`; all existing
  constructions stay valid).
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/GlobalMenuRegistry.cs` — **D-T3-1:** the menu renders a leaf via
  `Gui.MenuItem(child.Name, …)` (`WindowManager.cs` L537/L548), so add a `Func<string>? DynamicLabel` to the leaf
  `MenuItemNode`; `WindowManager` renders `child.DynamicLabel?.Invoke() ?? child.Name`.
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — at the two leaf-render sites (L537, L548) use
  `child.DynamicLabel?.Invoke() ?? child.Name` for the menu-item text.
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MenuCommandAdapter.cs` — in `ApplyLeafNode`, set
  `node.DynamicLabel = descriptor.DynamicDisplayName` (when non-null).
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ToolbarCommandAdapter.cs` — the hover **tooltip** uses
  `DynamicDisplayName?.Invoke() ?? DisplayName` (the first tooltip line); the checkable-no-icon text fallback (L114)
  likewise.
- Tests: `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/ToolbarCommandAdapterTests.cs` + new
  `MenuCommandAdapterTests.cs` (none exists today).

**Requirements:**
- `DynamicDisplayName` / `DynamicLabel` are re-read every frame (immediate mode), like `IsEnabled`/`IsChecked`.
- No NodeEdit behavior change beyond adding the optional field. No other descriptor/menu-node fields touched.
- A leaf with no `DynamicLabel` renders exactly as today (`child.Name`).

**Success conditions (exact names):**
- `Descriptor_DynamicDisplayName_DefaultsNull` — a descriptor built without it has `DynamicDisplayName == null`;
  existing constructions compile (compile-time proof = build green).
- `MenuNode_DynamicLabel_OverridesName_WhenSet` — a `MenuItemNode` with `DynamicLabel` returning `"X"` resolves its
  rendered label to `"X"`; with `DynamicLabel == null` resolves to `Name`. (Add a pure label-resolution accessor on
  the node, e.g. `ResolveLabel()`, and assert it — headless, no ImGui.)
- `MenuAdapter_SetsDynamicLabel_FromDescriptor` — `MenuCommandAdapter.Register` with a descriptor whose
  `DynamicDisplayName` is set produces a leaf node whose `ResolveLabel()` returns the dynamic value; null → `Name`.
- `ToolbarTooltip_UsesDynamicDisplayName_WhenSet` — add a pure tooltip-text seam on `ToolbarCommandAdapter` (e.g.
  `ResolveTooltip(commands, id)`) returning `DynamicDisplayName?.Invoke() ?? DisplayName` (+ description/shortcut);
  assert dynamic value used when set, `DisplayName` when null. (No ImGui.)
- Build green; `NodeEditor.UI.Tests` + `NodeEditor.Core.Tests` `Failed: 0`; toolbar+menu-filtered
  `Fdp.Presentation.Tests` `Failed: 0`.

---

## MTB2-T4 — Active-save-target resolver + `Save Scenario` + dynamic Save label  {#mtb2-t4}
**Item 3 · Batch BATCH-33 · Model: `pro` · Design: DEC-A4/A5/A6 + Active-save-target model · Depends: T3**

**Objective:** make Save resolve the active target (focused document, else scenario), add explicit Save-Scenario
commands, and give Save a dynamic `"Save [{kind}: {name}]"` label — all via injected seams (no ImGui, no direct
`IEditorLogic`/`WindowManager` coupling in `ShellSaveCommands`).

**Scope (only these files):**
- `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs` — extend `Register(...)` with seams:
  `Func<bool>? isScenarioContext`, `Func<bool>? hasLoadedScenario`, `Action? saveScenario`,
  `Action? requestScenarioSaveAs`, `Func<string>? describeActiveTarget`. Branch `shell.save`/`shell.saveAs` on
  `isScenarioContext()` first (→ `saveScenario` / `requestScenarioSaveAs`), else the existing per-kind active-document
  logic. Set `IsEnabled = (isScenarioContext?.Invoke() ?? false) ? (hasLoadedScenario?.Invoke() ?? false) :
  docManager.Active != null`. Set `DynamicDisplayName = () => describeActiveTarget?.Invoke() ?? "Save"`. Register
  explicit `scenario.save` / `scenario.saveAs` commands routed to `saveScenario` / `requestScenarioSaveAs`. All seams
  optional (null) → behavior identical to today (back-compat).
- (Wiring) `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — pass production seams:
  `isScenarioContext = () => windowManager.CurrentPerspective == "Editor"`,
  `hasLoadedScenario = () => !string.IsNullOrEmpty(_editorLogic?.LoadedScenarioName)`,
  `saveScenario = () => _editorLogic?.SaveCurrentScenario()`, `requestScenarioSaveAs` → existing scenario Save-As
  path, `describeActiveTarget = () => <"Save [scenario: {LoadedScenarioName}]" | "Save [{Active.Kind}: {Active.Name}]"
  | "Save">`. Keep `ScenarioMenuCommands` signature unchanged.
- Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs`.

**Success conditions (exact names, headless via injected seams):**
- `Save_InScenarioContext_CallsSaveScenario_NotDocument`
- `Save_InDocumentContext_CallsDocumentSave_NotScenario`
- `SaveAs_InScenarioContext_RequestsScenarioSaveAs`
- `Save_IsEnabled_ReflectsActiveTarget` (scenario ctx → hasLoadedScenario; doc ctx → Active != null)
- `DynamicDisplayName_NamesKindAndAsset` — e.g. returns `"Save [scenario: test-move]"` and `"Save [Blueprint: Count5]"`
- `ScenarioSave_Command_RoutesToSaveScenario` — invoking `scenario.save` calls `saveScenario`.
- `NullSeams_PreserveLegacySaveBehavior` — with all new seams null, the three commands behave exactly as before.
- Build green; `Hrot.Editor.AiShared.Tests` + `Hrot.Editor.Tests` `Failed: 0`.

---

## MTB2-T5 — Unified File menu + perspective display-label "Scenario"  {#mtb2-t5}
**Item 3 · Batch BATCH-34 · Model: `pro` · Design: DEC-A7 · Depends: T4**

**Objective:** present a unified File menu (New/Open/Save/Save-As/Save-All) and show the `Editor` perspective as
"Scenario" — **without renaming the perspective key**.

**Scope (only these files):**
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — add a display-label map mirroring
  `_perspectiveIconKeys`: `RegisterPerspectiveLabel(string id, string label)` + `GetPerspectiveLabel(string id)`
  (returns label or the id). Make `BuildPerspectiveMenuModel()` return the **label** for display (add a `Label`
  field to the tuple, or a parallel accessor) WITHOUT changing the id used by `SelectPerspective`/`IsPerspectiveActive`.
- (Render consumers) `PerspectiveToolbarSection` / the perspective menu builder — render `GetPerspectiveLabel(id)`.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — `windowManager.RegisterPerspectiveLabel("Editor", "Scenario")`;
  and the **unified File menu** wiring: `File/New Asset…` (placeholder until T7), `File/Open Asset…` (exists),
  `File/Save`, `File/Save As…`, `File/Save All`; surface the `scenario.save`/`scenario.saveAs` commands. Fold the
  scenario menu's Save/Save-As/New/Load into the unified commands; leave scenario-specific extras (Migration History).
- Tests: `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/` (perspective label) + `Hrot.Editor.Tests` (menu).

**Requirements (CRITICAL):** do **NOT** rename the `"Editor"` perspective key anywhere (it is also the cluster
node/subsystem name and ~10 `PerspectiveBound` window keys — renaming breaks cluster identity / dock layout). Only add
a display-label.

**Success conditions (exact names):**
- `PerspectiveLabel_OverridesDisplay_NotId` — `RegisterPerspectiveLabel("Editor","Scenario")`; `GetPerspectiveLabel
  ("Editor")=="Scenario"`; `GetPerspectiveLabel("BTree")=="BTree"` (unset → id); `SelectPerspective`/
  `IsPerspectiveActive` still keyed by `"Editor"`.
- `BuildPerspectiveMenuModel_UsesLabel` — the menu model shows `"Scenario"` for the `Editor` row, id unchanged.
- A `Hrot.Editor.Tests` assertion that `File/Save`, `File/Save As…`, `File/Save All`, `File/Open Asset…`,
  `File/New Asset…` and `scenario.save` are registered/menu-bound after `RegisterWindows`.
- Build green; `Hrot.Editor.Tests` `Failed: 0`; perspective-filtered `Fdp.Presentation.Tests` `Failed: 0`.

---

## MTB2-T6 — `RecipePickerSource` (per-kind recipe projection incl. "Empty")  {#mtb2-t6}
**Item 1 · Batch BATCH-35 · Model: `pro` · Design: DEC-A2 · mirrors Phase-8 `AssetPickerSource`**

**Objective:** project the per-kind recipes (from `INewAssetService.AvailableRecipes()`, which already includes the
in-code "Empty" entry) into Tree-picker `PickerEntry`s — the data seam for T7's New-from-recipe launcher.

**Scope (only these files):**
- `Hrot/Editor/Hrot.Editor.AiShared/Browser/RecipePickerSource.cs` (NEW) — mirror
  `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerSource.cs`. Ctor takes the per-kind services
  `IReadOnlyDictionary<AssetKind, INewAssetService>` (+ optional describe). `Query`/`BuildEntries` enumerate each
  service's `AvailableRecipes()`; `ToEntry(kind, recipe)` → `PickerEntry`: `Category = "<Kind>"` (or
  `"<Kind>/<recipe-category>"` if recipe metadata has one), `Name = recipe.Name`/DisplayName (incl. "Empty"),
  `IconKey = AssetKindIcons.GetIconKey(kind)`, `Tag = (kind, recipe)` (a small `RecipeChoice` record),
  `Description = recipe metadata`. `PreferredLayout = Tree`, `Single`. `GetItemKey` stable per (kind, recipe).
- Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/RecipePickerSourceTests.cs` (NEW).

**Success conditions (exact names):**
- `Entries_IncludeEmptyPerKind` — each kind's projection includes its "Empty" recipe entry.
- `Entries_HaveKindCategory_PerKindIcon_AndRecipeTag` — Category starts with the kind; IconKey == GetIconKey(kind);
  Tag carries (kind, recipe).
- `GetItemKey_StableAcrossQueries`.
- `Description_FromRecipeMetadata_WhenPresent`.
- Build green; `Hrot.Editor.AiShared.Tests` `Failed: 0`.

---

## MTB2-T7 — `NewAssetLauncher` + File/New + New toolbar button  {#mtb2-t7}
**Item 1 · Batch BATCH-36 · Model: `pro` · mirrors Phase-8 `AssetPickerLauncher` · Depends: T6**

**Objective:** open the recipe Tree picker; on pick, drive the existing generic `NewAssetDialog` (name + folder) →
`CreateNew` → open the new asset. Surface via File/New + a toolbar "New" button.

**Scope (only these files):**
- `Hrot/Subsystems/Hrot.Editor/Browser/NewAssetLauncher.cs` (NEW) — mirror
  `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickerLauncher.cs`: injected `openPicker` seam
  (`Action<PickerRequest, Action<PickerResult>>`), the per-kind `INewAssetService` registry, and a `showNewAssetDialog`
  seam. `Open()` builds a Tree `PickerRequest` from `RecipePickerSource.BuildEntries`; on pick → invoke
  `showNewAssetDialog(kind, recipe)`; on dialog confirm → `CreateNew` (+ blueprint subfolder-save per DEC-12) → open.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — construct the launcher with `_shellPickers.OpenPicker` + the
  `_newAssetServices` registry; wire `File/New Asset…` (replace the T5 placeholder) + a toolbar "New" button
  (`asset/new` cell exists) at `sortOrder: -11` (left of Open Asset). **Remove the production wiring of the
  blueprint-only `RecipeCreateModal`** (do NOT delete the class/tests).
- Tests: `Hrot/Subsystems/Hrot.Editor.Tests/Browser/NewAssetLauncherTests.cs` (NEW).

**Success conditions (exact names, headless via fake openPicker + fake dialog seam):**
- `Open_BuildsTreeRequest_FromRecipeSource` — captured `PickerRequest.Layout == Tree`; `ItemsProvider()` yields recipe
  entries (incl. "Empty") with `Tag` = (kind, recipe).
- `Open_Pick_InvokesNewAssetDialog_WithKindAndRecipe` — confirming a pick calls `showNewAssetDialog` with the picked
  (kind, recipe).
- `Open_Cancel_DoesNothing`.
- A `Hrot.Editor.Tests` assertion that `File/New Asset…` + a New toolbar entry are registered after `RegisterWindows`.
- Build green; `Hrot.Editor.Tests` `Failed: 0`; `Hrot.Blueprints.Tests` stays at the 9 PRE-1 baseline (no new
  failures — `RecipeCreateModal` retirement must not regress blueprint creation paths).

---

## Lead runtime checks (mine, after the batches land)
- T1/T2: toolbar icons have margin + visible hover/toggle; Save icon present next to Open Asset.
- T4/T5: Ctrl+S in a BTree/HSM/Blueprint canvas saves the active doc; in the Scenario perspective saves the scenario;
  the Save menu/tooltip reads `Save [kind: name]`; perspective switcher shows "Scenario".
- T7: File/New (and the New toolbar button) opens the recipe Tree picker → New Asset dialog → creates + opens.
