# Main Toolbar 2 — File Operations & Toolbar Polish — DESIGN (for review)

**Status:** DESIGN — **approved 2026-06-12**; ready to decompose into tasks. **No tasks kicked yet** (awaiting separate go-ahead).
**Process:** same as main-toolbar-1 (batches → claude-worker-orchestrator `pro` → hard review → gated commit).
**Dev rules:** [../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md) · **Lead rules:** [../.guides/DEV-LEAD-GUIDE.md](../.guides/DEV-LEAD-GUIDE.md)

## Scope (the four items)
1. **New-from-recipe** — a recipe picker (Phase-8 Tree style) listing all kinds' recipes incl. "Empty" → New Asset dialog.
2. **Save icon** in the main toolbar next to "Open Asset", wired to the existing `shell.save` (Ctrl+S).
3. **Unify File / Scenario menus** into New / Open / Save / Save-As / Save-All keyed off the **active save target**
   (focused document, else the scenario), with explicit `Save Scenario` primitives and a **dynamic Save
   label/tooltip** (e.g. `Save [blueprint: Count5]` / `Save [scenario: test-move]`) that names exactly what will save.
4. **Toolbar icon UX** — ~90% icon inset (margin to breathe), and clear hover + toggled states, fixed **generically**
   in the shared icon widget so every toolbar icon benefits.

---

## Verified current state (grounded in source, not assumptions)
- **Toolbar render path:** `MainToolbarManager` (entries via render delegates) → `ToolbarCommandAdapter.RenderEntry`
  → `IconWidgets.IconButton/ToggleIcon(in IconHandle, id, size, enabled, tint)` (the `IconHandle` overloads,
  `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs` L211–330). These overloads **already** draw a toggled
  bg (only when `isToggled && enabled`), a thin hover **border**, and disabled dimming — but the **image is drawn at
  100% of `size`** (no inset) and hover/toggle visuals are faint.
- **Save:** `ShellSaveCommands` (`Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`) already registers
  `shell.save` (Ctrl+S, `IconKey "shell/save"`), `shell.saveAs` (`shell/saveAs`), `shell.saveAll` (Ctrl+Shift+S,
  `shell/saveAll`). It is **pure seam-based** (injected per-kind save delegates + `requestSaveAs`) and unit-tested.
  Routing today: `Save` uses `AiDocumentManager.Active`; **perspective-agnostic**, so in the Editor/map perspective
  (a loaded scenario, no active document) Save is disabled/no-op. **`"shell/save"` has no atlas cell** in
  `SilkIconProvider` → toolbar currently falls back to a text button (and it isn't registered in `MainToolbar` anyway).
- **New asset:** `INewAssetService` (per-kind) exposes `AvailableRecipes()` → `IReadOnlyList<IEditableAsset>`
  **including the in-code "Empty" entry**, and `CreateNew(recipe, name, relPath)`. Generic `NewAssetDialog`
  (Kind/Recipe/Name/FolderPicker/`CanConfirm`/`Confirm`) exists. A per-kind registry `_newAssetServices`
  (Blueprint/BTree/HSM/Scenario) is already built in `EditorSubsystem`. The only blueprint-specific surface is the
  combo-based `RecipeCreateModal` (to be superseded by the generic picker).
- **Active asset / scenario (architect-confirmed, source-verified):**
  - `AiDocumentManager.Active` (+`ActiveChanged`, `OpenDocuments`) is the active asset for Blueprint/BTree/HSM.
  - Scenarios are **not** `AiDocument`s — they load/save via `IEditorLogic` (`LoadScenarioByName`,
    `SaveCurrentScenario()`, `SaveScenarioAs(name)`, `LoadedScenarioName`, `NewScenario()`, `AvailableScenarios`).
  - `WorkspaceMenuBuilder` already aggregates `OpenDocuments` + `LoadedScenarioName` as parallel entries — the
    established "unified presentation of parallel states" pattern.
  - `WindowManager.CurrentPerspective` (string) exists for the resolver.

---

## Architect decision (verified against source) — DEC-A1
**Question relayed:** how to represent "the active asset" uniformly for Save when scenarios aren't documents.
**Architect answer (Option b), adopted:** keep a **separate notion of the active scenario** and use an
**editor-shell-level resolver** to pick the save target. Do **not** wrap scenarios as `AiDocument`s (incompatible
lifecycle: docs = local file I/O via AtomicFileWriter/emitters; scenarios = cluster 2PC over the DDS bus via
`IEditorLogic`). Mirror the `WorkspaceMenuBuilder` parallel-state pattern and the `AssetPickActionRouter`
shell-routing pattern. **All five load-bearing claims independently verified in code.** *(The architect framed the
resolver around `CurrentPerspective`; on review (DEC-A4) we generalize it to **focused-document-else-scenario**, with
perspective used only as the scenario's activity signal — see the Active-save-target model.)*

→ Implementation: see the **Active-save-target model** below (converged after the multi-document / blackboard /
dynamic-label review).

---

## Per-item design

### Item 4 — Generic toolbar icon UX (do FIRST: smallest, unblocks visual polish, touched by all others)
**Where:** `IconWidgets.ToggleIcon/IconButton(in IconHandle, …)` (the generic widget) + `ToolbarCommandAdapter.DefaultSize`.
**Change (generic, no per-call-site edits):**
- **Inset ~90%:** keep the `InvisibleButton` hit/spacing box at full `size` (layout unchanged), but draw the image
  centered at `size * IconScale` (IconScale ≈ 0.9; ~5% margin each side). Expose `IconScale` as an optional param
  (default 0.9) so it's tunable.
- **Hover:** replace the faint border with a clear filled hover highlight (e.g. `SelectionAccent`/light fill at low
  alpha) behind the icon when `IsItemHovered() && enabled`.
- **Toggled/checked:** make the toggled fill clearly read as "active" (accent-tinted fill + optional border), distinct
  from hover.
**Tests (headless):** `IconWidgets` has no ImGui-free seam today; add a tiny pure helper
  `IconWidgets.ComputeIconRect(boxPos, boxSize, scale)` returning the centered inset rect, and unit-test it
  (centered, 90%, never larger than box). Visual hover/toggle is **runtime-verified** by the user (note in batch).
**Risk:** low; but this widget is shared beyond the toolbar — must not change layout/spacing (hitbox stays full size),
  only the drawn image rect + state visuals. Verify no regression in existing icon-widget tests.

### Item 2 — Save icon in the main toolbar (small; depends on Item 4 only cosmetically)
- Add a `"shell/save"` atlas cell to `SilkIconProvider.DefaultCellMap` (distinct, recognizable "disk" cell);
  optionally `"shell/saveAs"`, `"shell/saveAll"` too (their commands already declare those keys).
- In `EditorSubsystem.RegisterWindows`, `ToolbarCommandAdapter.Register(MainToolbar, ShellCommands,
  ShellSaveCommands.SaveId, iconProvider, sortOrder)` placed just right of "Open Asset" (e.g. open=-10, **save=-9**),
  with the existing separator after the group. `shell.save` is already Ctrl+S (no hotkey change).
- The Save icon's **hover tooltip uses the dynamic label** (DEC-A6) → e.g. `Save [blueprint: Count5]`.
- **Note:** the *enabled/routing* correctness of Save in the scenario context + the dynamic label are delivered by
  **Item 3** — Item 2 just surfaces the button (static "Save" tooltip until Item 3's `DynamicDisplayName` lands).
**Tests:** extend the toolbar-guardrail test to assert a `shell.save` toolbar entry exists; reuse `SaveCommandsTests`.

### Item 3 — Unified File menu + active-save-target routing + dynamic label (the core)
- **`EditorCommandDescriptor` gains `Func<string>? DynamicDisplayName`** (DEC-A6; in `NodeEditor.Core/Action`,
  generic, consistent with its `Func<bool>` `IsEnabled`/`IsChecked`). `MenuCommandAdapter` renders the menu item text
  and `ToolbarCommandAdapter` the tooltip as `DynamicDisplayName?.Invoke() ?? DisplayName`.
- **Active-save-target resolver seams in `ShellSaveCommands.Register`** (see model below): scenario activity signal =
  the `Editor` perspective; `saveScenario`/`requestScenarioSaveAs` → `IEditorLogic`; document case → existing
  per-kind logic over `AiDocumentManager.Active`. `shell.save`/`shell.saveAs` route to the resolved target;
  `DynamicDisplayName` returns `"Save [{kind}: {name}]"`. Keep `ScenarioMenuCommands` signature unchanged.
- **Explicit `scenario.save` / `scenario.saveAs`** commands (DEC-A5) routed straight to `IEditorLogic` — always
  available regardless of perspective; placed in the menu.
- **Perspective display-label** (DEC-A7): `WindowManager` shows label **"Scenario"** for perspective id `Editor`
  (small label-map enhancement; **no key rename** — `"Editor"` collides with the cluster node/subsystem name).
- **Unified File menu** (`GlobalMenu`): `File/New Asset…` (Item 1), `File/Open Asset…` (Ctrl+O), `File/Save` (Ctrl+S,
  dynamic label), `File/Save As…`, `File/Save All` (Ctrl+Shift+S). Fold scenario New/Load/Save/Save-As into these
  unified commands; scenario-specific extras (Migration History) stay where they are.
**Tests (headless, injected seams — no ImGui):** `ShellSaveCommands` —
  `Save_InScenarioContext_CallsSaveScenario_NotDocument`, `Save_InDocumentContext_CallsDocumentSave_NotScenario`,
  `SaveAs_InScenarioContext_RequestsScenarioSaveAs`, `Save_IsEnabled_ReflectsActiveTarget`,
  `DynamicDisplayName_NamesKindAndAsset` (e.g. `"Save [scenario: test-move]"`); adapter tests that the menu label +
  toolbar tooltip use `DynamicDisplayName`.
**Risk:** medium — menu restructure + a small NodeEdit descriptor change + a behavioral branch in a shared command.
  Mitigate with the pure-seam tests + keeping `ScenarioMenuCommands` intact.

### Item 1 — New-from-recipe picker (Phase-8 pattern reused)
- **`RecipePickerSource`** (editor-side, mirrors `AssetPickerSource`): projects `INewAssetService.AvailableRecipes()`
  across the `_newAssetServices` registry → `PickerEntry`: `Category = "<Kind>"` (or `"<Kind>/<recipe-category>"` from
  recipe metadata), `Name = recipe DisplayName` (incl. the "Empty" entry), `IconKey = per-kind`, `Tag = (kind, recipe
  IEditableAsset)`, `Description = recipe metadata`. `PreferredLayout = Tree`, `Single`.
- **`NewAssetLauncher`** (mirrors `AssetPickerLauncher`): opens the Tree picker via the dedicated `_shellPickers`
  registry; on pick → open the existing generic `NewAssetDialog` seeded with the chosen kind + recipe (name + folder),
  `Confirm()` → `CreateNew` (+ subfolder save for blueprint per DEC-12) → open the new doc / activate.
- **Surfacing:** `File/New Asset…` menu + a toolbar "New" button (`asset/new` cell exists) left of "Open Asset"
  (sortOrder ≈ -11) + optional Ctrl+N. Supersede the blueprint-only `RecipeCreateModal` (remove its production wiring;
  **do not delete** the class — no-deletion rule).
**Tests (headless):** `RecipePickerSourceTests` (entries incl. "Empty" per kind, Category/IconKey/Tag, kind grouping);
  `NewAssetLauncherTests` (pick → seeds `NewAssetDialog` with kind+recipe; confirm → `CreateNew` called). The
  `NewAssetDialog.Confirm` path already has Phase-6 tests.
**Risk:** medium — reuses proven Phase-8 + Phase-6 pieces; main work is the projection + the pick→dialog handoff.

---

## Proposed task breakdown & batches
| Task | Item | Summary | Batch |
|------|------|---------|-------|
| **MTB2-T1** | 4 | Generic `IconWidgets` 90% inset + clear hover/toggle visuals + `ComputeIconRect` test | BATCH-30 |
| **MTB2-T2** | 2 | `shell/save` (+`saveAs`/`saveAll`) atlas cells + Save toolbar button next to Open Asset | BATCH-31 |
| **MTB2-T3** | 3 | `Func<string>? DynamicDisplayName` on `EditorCommandDescriptor` + menu/toolbar adapters consume it (DEC-A6) | BATCH-32 |
| **MTB2-T4** | 3 | Active-save-target resolver + explicit `Save Scenario`/`Save Scenario As` + dynamic Save label in `ShellSaveCommands` + tests | BATCH-33 |
| **MTB2-T5** | 3 | Unified File menu wiring + perspective display-label "Scenario" (DEC-A7) | BATCH-34 |
| **MTB2-T6** | 1 | `RecipePickerSource` + per-kind recipe projection (incl. "Empty") + tests | BATCH-35 |
| **MTB2-T7** | 1 | `NewAssetLauncher` + wire `File/New` + New toolbar button; supersede `RecipeCreateModal` wiring | BATCH-36 |

**Recommended sequence:** T1 → T2 → T3 → T4 → T5 → T6 → T7, **one task per batch** (BATCH-30…36) — Zoo loses focus on
multi-objective batches. T3 **before** T4 (T4's dynamic Save label needs the descriptor field); T6 **before** T7
(T7 needs the source). See [TASK-DETAIL.md](./TASK-DETAIL.md) / [TASK-TRACKER.md](./TASK-TRACKER.md).

## Decisions (approved 2026-06-12)
- **DEC-A1** — scenario stays a **non-document**; an editor-shell resolver picks the save target (architect Option b;
  all five load-bearing claims source-verified). Documents save via local file I/O; scenarios save via `IEditorLogic`
  cluster-2PC over the DDS bus. Do **not** wrap scenarios as `AiDocument`s.
- **DEC-A2** — recipe picker reuses the NodeEdit Tree picker + dedicated `_shellPickers` registry (Phase-8 / DEC-15
  consistency), not the combo `RecipeCreateModal`.
- **DEC-A3** — `IconWidgets` fix is **generic** (shared widget), not toolbar-local; hitbox/layout unchanged, only the
  drawn rect + state visuals.
- **DEC-A4** — **active save target = the focused document (`AiDocumentManager.Active`) when a document surface is
  focused; else the scenario.** Perspective is used **only** as the scenario's *activity signal* (it has no focusable
  surface); document kinds — current and future — resolve via `Active` independent of perspective.
- **DEC-A5** — provide **explicit, always-available `Save Scenario` / `Save Scenario As`** primitives
  (`scenario.save` / `scenario.saveAs` → `IEditorLogic`); Ctrl+S / File→Save call them in the scenario case. A future
  non-document saveable follows the same pattern (own explicit `Save X` + activity signal) — the unified Save command
  never changes.
- **DEC-A6** — **dynamic Save label/tooltip.** Add `Func<string>? DynamicDisplayName` to `EditorCommandDescriptor`
  (consistent with its `Func<bool>` `IsEnabled`/`IsChecked`); menu item + toolbar tooltip render
  `DynamicDisplayName?.Invoke() ?? DisplayName`. Content = the resolver's output: `"Save [{kind}: {name}]"`
  (e.g. `Save [scenario: test-move]`); `"Save"` greyed when nothing dirty/active. One source of truth feeds the
  label, the tooltip, and the dispatch.
- **DEC-A7** — **do NOT rename the `Editor` perspective key** (it collides with the cluster node/subsystem name
  `"Editor"` and ~10 `PerspectiveBound` window keys, and would reset persisted dock layouts). Instead **decouple the
  perspective display-label from its id**: keep id `Editor`, show label **"Scenario"** in the switcher/menu/toolbar
  (small `WindowManager` label-map enhancement). The dynamic Save label already removes save-ambiguity, so the rename
  is switcher-polish only.

## Active-save-target model (the converged design)

**One resolver, consumed by the label, the tooltip, and the dispatch.** A shell-level seam yields the current save
target as a small descriptor — `{ Kind, Name, CanSave, Save(), SaveAs() }` — or "none" when there's nothing to save:

- **Documents (all kinds, current & future):** `AiDocumentManager.Active` — focus / last-activated-wins (canvas
  windows call `Activate(doc)` on focus; a future tab strip calls it on tab switch). Covers Blueprint/BTree/HSM and
  any future document-backed kind **with no new code**.
- **Scenario (the lone non-document):** has no focusable surface, so its **activity signal is the `Editor`
  perspective** (displayed as "Scenario"). In that perspective, with a scenario loaded, it is the target.
- **Resolution rule:** focused document if a document surface is focused; else the scenario (when loaded). In
  practice today: canvas perspective → `AiDocumentManager.Active`; `Editor` perspective → scenario.

`ShellSaveCommands` depends only on this seam (`Func<>` delegates — **no** direct `IEditorLogic`/`WindowManager`
coupling; preserves its unit-tested design): `isScenarioContext`, `hasLoadedScenario`, `saveScenario`,
`requestScenarioSaveAs`, and a `describeActiveTarget` (for `DynamicDisplayName`). `shell.save`/`shell.saveAs` branch
on `isScenarioContext()` first, else the existing per-kind active-document logic; `IsEnabled =
isScenarioContext() ? hasLoadedScenario() : docManager.Active != null`.

- **Save Scenario primitives (DEC-A5):** explicit `scenario.save` / `scenario.saveAs` → `IEditorLogic`, always
  available regardless of perspective. Ctrl+S routes here in the scenario case.
- **Save All:** enumerate `AiDocumentManager.OpenDocuments` **+ the scenario**; perspective-independent (current
  shape, plus the scenario participant).
- **Dynamic label (DEC-A6):** `DynamicDisplayName` = `"Save [{kind}: {name}]"` from `describeActiveTarget`.

**Scalability — the new-asset-type question, answered.** A new **document** kind flows through `Active` for free.
A new **non-document** saveable registers like the scenario (its own activity signal + explicit `Save X` + a resolver
entry). The unified Save command and menu **never change**. Perspective is only ever the activity signal for
surface-less participants, never a per-type dispatch.

**Future upgrade (no ripple).** If the scenario ever gains a focusable surface (or finer control is wanted), replace
its perspective activity-signal with a real focus report — the resolver seam and `ShellSaveCommands` stay untouched.

**Tab-readiness.** `AiGraphCanvasWindow` is single-canvas-per-kind today (renders `Active`, `Activate()`s on focus);
a future tab strip just `Activate()`s on tab switch, so `Active` (and thus the save target + dynamic label) stays
correct with no change here.

**Blackboard note (verified).** The planned visual blackboard editor (`Blackboard_Authoring_Detailed_Design.md`, v2
JSON-backed) is a **per-asset Variables panel embedded in the owning BTree/HSM document's JSON** — *not* a standalone
asset/perspective; a "top-level shared blackboard" is explicitly out of scope (line 1407). Saving the owning document
saves its blackboard. `AssetKind.Blackboard`/`Utility` are sub-element/catalog kinds, not document save targets. So
the blackboard editor does not stress this model; if a standalone shared blackboard ever lands as a document, it
flows through `Active` like any other document.
