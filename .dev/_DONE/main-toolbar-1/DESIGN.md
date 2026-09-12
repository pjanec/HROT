# Main Toolbar, Generic Asset Browser & Unified Asset Creation — Detailed Design

**Status:** Draft v2 · **Date:** 2026-06-10 · **Repo:** `IOS-IG-SimHost-FDP`

This document is the authoritative design. `TASK-DETAIL.md` references these chapters by
number; design rationale lives here, not in the task docs.

> **v2 delta** (post architect + stakeholder review): AI-debug polymorphism (§9), jitter-free
> declared toolbar height (§4.1), typed catalog `Changed` event (§3, §10.4), a new
> **Asset Creation & Recipes** area (§17–§20), a prerequisite **folder reorganization**
> (§16), scenario subfolder support (§10.4, §19), and Save/Save-As/Save-All as editor
> commands fixing the focus-gated Ctrl+S bug (§6, §20).

---

## 1. Overview & Goals

We add an operator-facing **Main Toolbar** (a perspective-aware band of 64×64 icons under the
main menu bar) and replace the ad-hoc asset windows with a single **generic Asset Browser**
that any caller opens as a modal picker or a docked window. We also add a **unified asset
creation** flow (New / Save-As, recipe-driven) and correct several structural issues the work
exposes:

- Time-transport, AI-debug, scenario, and save actions currently call handlers directly or via
  focus-gated key polling. We route them through the **editor action system**
  (`IEditorCommands`); menus and toolbar icons become thin views over command descriptors.
- Perspective switching is mixed into the main menu bar; we give it a dedicated **Perspective**
  menu and a toolbar radio-group.
- Two legacy `AssetBrowserWindow`s and a button-based `ScenarioBrowserPanel` are retired.
- Recipes and final assets physically mix under one folder; we reorganize to disjoint
  **`Assets/`** and **`Recipes/`** roots.

### 1.1 Non-goals

- No change to distributed/cluster scenario loading (orchestrator path) — editor all-in-one only.
- No new low-level rendering framework — reuse `IconAtlas`, `IconWidgets`,
  `IIconProvider`/`IconHandle`, the `InvisibleButton + ImDrawList` pattern.
- No model-level merge of open documents and the loaded scenario (§11).
- No recipe-authoring UI — recipes are hand-curated (§18.4).

---

## 2. Existing Infrastructure We Build On

| Concern | Type / file | Notes |
|---|---|---|
| Status bar (pattern) | `StatusBarManager` — `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/StatusBarManager.cs` | `RegisterSection(id, sortOrder, renderDelegate, perspective?)`, sorted, perspective-filtered, exposes fixed `Height`. |
| Icon atlas (coordinate) | `IconAtlas`, `IconWidgets` — `FDP/Engine/Fdp.Presentation/ImGui/Icons/` | Cells via `"c9"` coords. Widgets draw at `atlas.IconSizeVec` (no size param). |
| Icon abstraction (semantic) | `IIconProvider` / `IconHandle` — `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IIconProvider.cs` | `TryGet(key, out IconHandle)`. `IconHandle = { TextureId, Width, Height, Uv0, Uv1 }` — carries its own texture → multi-atlas for free. |
| Semantic→atlas bridge | `SilkIconProvider` — `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs` | Implements `IIconProvider` over `IconAtlas` via a `key → cell` dict; accepts custom map. |
| Editor action system | `IEditorCommands`, `EditorCommandDescriptor`, `EditorCommandsImpl`, `CommandRegistration` — `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/` | Descriptor: `Id, DisplayName, Category, Description, IconKey, DefaultKey, IsEnabled, IsChecked`. `Invoke`, `AvailabilityChanged`. Doc: *"the host binds these to its toolbars, menus, and hotkeys."* Today **per document** (`BlueprintDocumentFactory`). |
| Hotkey pump | `EditorHotkeyDispatcher` — `Hrot/Editor/Hrot.Editor.AiShared/Windows/EditorHotkeyDispatcher.cs` | Per-frame, evaluates each command's `DefaultKey` against input; perspective-level. |
| Time transport | `ITimeTransportFacade` — `Hrot/Engine/Hrot.Presentation/Facades/` | State + `TogglePlayPause/Step/Stop/SetTimeScale`. |
| Time bar (vectors) | `ClusterTimeControlStatusBarSection` — `Hrot/Engine/Hrot.Presentation/Panels/` | Hand-draws Play/Pause/Step/Stop via `ImDrawList`. |
| Asset catalog | `AssetCatalog`, `IAssetCatalog`, `IAssetCatalogContributor`, `IEditableAsset`, `AssetKind` — `Hrot/Editor/Hrot.Editor.AiShared/Catalog` + `/Identity` | Contributor-aggregated. `IEditableAsset`: `Kind`, `SourceFilePath`, `Name`. `Changed` event is **parameterless** today. `AssetKind = { Blueprint, BTree, Hsm, Blackboard, Utility }` (no Scenario). |
| File contributors | `BlueprintAssetContributor`, `BTreeJsonAssetContributor`, `HsmJsonAssetContributor`, … | Recursive scan (`SearchOption.AllDirectories`); `Name` from JSON header, `SourceFilePath` = file. BTree/HSM JSON contributors dormant pending persistence-unification migration. |
| Reference index | `ReferenceCatalog` / `IReferenceCatalog` — `Hrot/Editor/Hrot.Editor.AiShared/References/` | Subscribes to `IAssetCatalog.Changed`; on each event **clears + re-walks `_catalog.All × all contributors`** (heavy AI-FQN/var scan). |
| Open documents | `AiDocumentManager` — `Hrot/Editor/Hrot.Editor.AiShared/Documents/` | `OpenDocuments`, `Active`, `Open/Activate/Close`; perspective switch on activate. |
| Scenario state | `IEditorLogic` / `EditorApplication` — `Hrot/Subsystems/Hrot.Editor/` | `LoadedScenarioName` (singular), `AvailableScenarios`, `LoadScenarioByName`, `NewScenario`, `SaveCurrentScenario`, `SaveScenarioAs`. Scenario = folder `ScenariosRoot/<name>/scenario.json` (flat name today). `ScenariosRoot` = NAS path. |
| Scenario panel (retire) | `ScenarioBrowserPanel` — `Hrot/Subsystems/Hrot.Editor/UI/` | Button row + Load modal + Save-As (flat) + Migration History. |
| AI debug | `IDebugSessionRegistry`, `IAiDebugSession`, `IAiTraceObserver` — `Hrot/Editor/Hrot.Editor.AiShared/Debug/` | `ActiveSession : IAiDebugSession`, `Changed`. `IAiDebugSession`: `Continue/StepOver/StepInto/StepOut/Pause/IsPaused/OnSessionStateChanged`. **`StepBack`/`CurrentNodePointer`/`RecordedNodeCount` are Blueprint-only** (`IBlueprintDebugSession`). |
| Debug controls | `DebugStepControls` — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/` | Text-button row (blueprint). |
| Recipes (BP-only) | `RecipeCreateModal`, `NewFromRecipeService`, `BlueprintEditorBootstrap.DiscoverRecipes` — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` | Recipe = `BlueprintAsset` with `EditorMetadata.Recipe` (`RecipeMetadata`). Scanned from `Blueprints/Recipes/`. No BTree/HSM/Scenario recipes exist. |
| Creation/save (BP) | `SaveActiveBlueprintCommand`, collision guard (`SaveAllWithCollisionGuardTests`) | Save path is **flat** (`…/Blueprints/{name}.bp.json`). Ctrl+S handled inline in the Blueprint-Tools panel, **gated on that panel's focus** (bug, §20). |
| Menu | `GlobalMenuRegistry` / `MenuItemNode` — `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/` | Trie of `/`-paths; `RegisterItem/RegisterCheckableItem/RegisterSeparator`. |
| Perspective | `WindowManager` — same dir | `CurrentPerspective`, `SwitchPerspective`, `OnPerspectiveChanged`, `RenderPerspectiveSwitcher` (enumerates `PerspectiveBound` `OwningPerspective`s). |
| Dockspace host | `Hrot/Runner/Hrot.ClusterRunner/Program.cs` (~L296–325) | `##DockSpace` at `viewport.WorkPos`, height inset by `StatusBar.Height`; then `WindowManager.Render()`. |

---

## 3. Architectural Principles

1. **Command-driven UI.** Menus, toolbar icons, and hotkeys are *views* over
   `EditorCommandDescriptor`s. Clicking/keying issues `IEditorCommands.Invoke(id)`. `IsEnabled`
   → disabled, `IsChecked` → toggled, `DisplayName`/`Description` → tooltip, `IconKey` → icon.
   No UI element calls a domain handler directly.
2. **Generic, perspective-aware composition** (toolbar mirrors `StatusBarManager`).
3. **Generic pick component.** The Asset Browser never executes — it returns the picked asset;
   the caller decides what to do, in both modal and docked forms.
4. **Presentation-level unification, not model-level** (§11).
5. **Semantic, multi-atlas icons** via `IIconProvider` → `IconHandle`.
6. **Respect layering.** Perspective switching stays `WindowManager`-native (engine), not an
   editor command.
7. **Typed catalog change.** `IAssetCatalog.Changed` carries which `AssetKind` changed, so
   downstream indices (e.g. `ReferenceCatalog`) can skip irrelevant kinds (§10.4).
8. **One asset root family.** Browse roots (`Assets/<Kind>`) and recipe roots
   (`Recipes/<Kind>`) are physically disjoint and centralized in `AssetRoots` (§16).
9. **Path is relative to the asset root.** An asset's logical location is `<subfolder>/<name>`
   under its root — used for tree placement and the collision guard. Browser rows are **labeled
   by `Name`** and **placed by relative path**.

---

## 4. Main Toolbar Framework

### 4.1 `MainToolbarManager` (engine — `Fdp.Presentation.WindowManager`)

Sibling of `StatusBarManager`, rendered as a band directly under the main menu bar.

```csharp
public sealed class MainToolbarManager
{
    // declaredHeight: the fixed vertical pixels this entry needs (NOT measured per-frame).
    public void RegisterEntry(string id, int sortOrder, float declaredHeight,
                              Action renderDelegate, string? perspective = null);
    public void RegisterSeparator(string id, int sortOrder, string? perspective = null);

    public float Height { get; }   // see §4.1.1
    public void Render(string currentPerspective = "");
}
```

- **Last-write-wins** on duplicate `id`, **sorted ascending** by `sortOrder`, **perspective
  filter** (`null` = global; else only when `perspective == currentPerspective`) — identical to
  `StatusBarManager`.
- Entries render **left-to-right** with `SameLine`; registered **separators** draw a vertical
  divider (§4.3).

#### 4.1.1 Jitter-free declared height

`Height` = **max `declaredHeight` over *all registered* entries** (not just the currently
visible ones). This is known before any rendering, so there is **no measure-then-size lag**,
and because it does not depend on which entries are visible, it is **constant across
perspective switches** — the central dockspace never bounces. With uniform 64×64 icons the
value is effectively `64 + 2·WindowPadding.Y`, but the data-driven max honors taller
contributors if any register. (This supersedes the v1 "return height from render" mechanism,
which caused one-frame dockspace jitter on perspective change.)

#### 4.1.2 Window placement & dockspace integration

`Render` opens a borderless, non-docking, no-saved-settings window pinned to `viewport.WorkPos`
with size `(WorkSize.X, Height)` — the `StatusBarManager` approach anchored at the **top**.
The host (`Program.cs`) insets the central dockspace at the **top** by `MainToolbar.Height`,
symmetric to the existing bottom inset by `StatusBar.Height`:

```
pos  = WorkPos + (0, toolbarHeight)
size = (WorkSize.X, WorkSize.Y - toolbarHeight - statusBarHeight)
```

`WindowManager.Render()` calls `MainToolbar.Render(CurrentPerspective)` alongside `StatusBar`,
and `WindowManager` exposes a `MainToolbar` property mirroring `StatusBar`.

### 4.2 Icon widget extensions (engine — `Fdp.Presentation.Icons`)

Add **`IconHandle`-based overloads with explicit size** so any provider-resolved icon renders
at 64×64 from any atlas:

```csharp
public static bool IconButton(in IconHandle icon, string id, Vector2 size,
                              bool enabled = true, Vector4? tint = null);
public static bool ToggleIcon(in IconHandle icon, string id, Vector2 size,
                              ref bool isToggled, bool enabled = true, Vector4? tint = null);
public static void Tooltip(string text);   // helper: if (IsItemHovered) SetTooltip
```

- **Disabled** (new): `enabled == false` → passive `Dummy` (no click) + dimmed draw, mirroring
  `DrawTransportButton`'s `dim` path.
- **Toggle/active** background retained. Draw path:
  `drawList.AddImage(icon.TextureId, pos, pos+size, icon.Uv0, icon.Uv1, tintU32)`.
- Existing `IconAtlas`/coordinate overloads remain.

### 4.3 Group separators

`RegisterSeparator` inserts a sentinel the render loop draws as a vertical line over the band
height. Groups: **Time**, **Perspective**, **AI-debug**, **Asset-browser launcher** (§14).
Group membership is a function of `sortOrder` ranges chosen by registrants.

---

## 5. Icon Infrastructure (semantic keys, multi-atlas)

### 5.1 Resolver

Standardize on `IIconProvider`. Extend `SilkIconProvider`'s map (or register an additional
provider behind the same interface) with new keys:

```
debug/continue, debug/step_back, debug/step_over, debug/step_into, debug/step_out
asset/scenario, asset/blueprint, asset/btree, asset/hsm, asset/blackboard, asset/utility
browser/open, asset/new, folder, folder_open
perspective/<name>
(time uses vector shapes, §7 — toolbar/* keys optional)
```

Unknown keys → `TryGet` returns `false`; callers render text only, never a broken sprite.

### 5.2 `AssetKind → IconKey`

Asset rows resolve their leading icon via a small `AssetKind → IconKey` map (semantic, **not**
`→ coordinate`), then through `IIconProvider`. Keeps atlas layout in one documented place.

### 5.3 Multi-atlas

`IconHandle.TextureId` is per-handle; a provider may hold several atlases and resolve different
keys to different textures. Consumers are agnostic.

---

## 6. Editor Action System Integration

### 6.1 Shell-level command set

`IEditorCommands` is today **per document** (per-canvas selection scope). Shell-wide actions
(scenario lifecycle, AI-debug stepping, save/save-all, open-browser, new-asset) have no
per-document home. We introduce a single long-lived **shell command set** — an
`EditorCommandsImpl` owned by the editor composition root. Subsystems register their shell
commands once at startup. Toolbar/menu/hotkeys bind to the union of:

- the **shell** command set (always present), and
- the **active document's** command set (when a document is focused).

Canvas-scoped commands stay per-document, unchanged.

### 6.2 Descriptor-binding adapters

Two generic adapters, neither knowing what a command does:

- **Menu adapter** — registers a `GlobalMenuRegistry` item with `OnClick = () => Invoke(id)`;
  greys out when `!IsEnabled()`; checkmark when `IsChecked()`; shortcut from `DefaultKey`.
- **Toolbar adapter** — a `MainToolbarManager` entry that resolves `IconKey` via
  `IIconProvider`, draws via §4.2 (`enabled = IsEnabled()`, toggled = `IsChecked()`), tooltip =
  `DisplayName` (+ `Description`/shortcut), `Invoke(id)` on click.

Both re-read `IsEnabled`/`IsChecked` every frame (immediate mode); no caching.

### 6.3 Hotkey dispatch (fixes Ctrl+S)

`EditorHotkeyDispatcher` already pumps per-frame at the perspective level using each command's
`DefaultKey`. Registering **Save/Save-As/Save-All** as commands with bindings (Ctrl+S,
Ctrl+Shift+S) makes them fire regardless of which sub-window is focused — replacing the
focus-gated inline key polling in the Blueprint-Tools panel (§20).

---

## 7. Time Control Group

`MainToolbarTimeControlSection` reads the **same** `ITimeTransportFacade` as the status-bar
version. Reusing vector graphics, the `DrawShape`/`DrawTransportButton` logic is extracted into
a shared static helper (`TransportIcons`) and rendered at 64 px: Play/Pause (state from
`IsPaused`), Step (`IsStepEnabled`), Stop (`IsStopEnabled`) as a group, plus the
`HH:MM:SS.mmm` time and the multiplier selector (`TimeScale` + popup). The status-bar section
is unaffected.

---

## 8. Perspective Group & Menu (WindowManager-native)

Perspective switching stays in `WindowManager` (layering, §3.6).

1. **Remove** perspective buttons from inside the main menu bar.
2. **Top-level "Perspective" menu** — built by `WindowManager` from the perspective
   enumeration; each entry checkable (checked = active) → `SwitchPerspective`.
3. **Toolbar radio-group** — one `ToggleIcon` per perspective behaving as a radio group
   (exactly one toggled); clicking a non-active one calls `SwitchPerspective`. Faces from a
   **per-perspective `IconKey`** (new optional field on perspective/window registration);
   perspectives without a key fall back to a text-label button.
4. Existing `TitleBarColor` accenting preserved for tinting where available.

---

## 9. AI Debug Group (polymorphic)

Debug stepping is shared across Blueprint/BTree/HSM via `IDebugSessionRegistry.ActiveSession`
(`IAiDebugSession`). The toolbar group binds debug commands to the **active** session, so it
works in every AI perspective (not just Blueprint).

- **Common commands** (always present, keyed off `ActiveSession`): Continue, Step Over, Step
  Into, Step Out, Pause. `IsEnabled = ActiveSession is { IsPaused: true }` (Pause enabled when
  attached & running).
- **Blueprint-only extras** render only when `ActiveSession is IBlueprintDebugSession bp`:
  **Step Back** (`IsEnabled = bp.CurrentNodePointer > 0`) and the **node-position indicator**
  (`DebugStepControls.FormatNodePosition`). These are *not* on the shared interface.
- Group label: **"AI Debug"** (not "Blueprint Debug").
- Commands live in the shell set (§6.1), `IconKey = debug/*`. The existing
  `DebugStepControls` text row may remain for the debug panel; the toolbar group is the icon
  surface over the same session.

---

## 10. Generic Asset Browser

### 10.1 `AssetBrowserPanel` (shared editor infra — `Hrot.Editor.AiShared`)

Pure content panel (`DrawContent()`), depending only on `IAssetCatalog`/`IEditableAsset` and an
`IIconProvider`. It never opens documents or loads scenarios.

```csharp
[Flags] public enum AssetKindFilter { None=0, Scenario=1, Blueprint=2, BTree=4, Hsm=8,
                                      Blackboard=16, Utility=32, All=~0 }

public sealed class AssetBrowserPanelOptions
{
    public AssetKindFilter Kinds { get; init; } = AssetKindFilter.All;
    public bool   ShowAllTab { get; init; } = true;          // chip-filtered flat tab
    public AssetKind? InitialKind { get; init; }
    public string?    InitialFullPath { get; init; }         // relative-to-root path
}

public event Action<IEditableAsset>? AssetActivated;         // double-click
public IEditableAsset? Selection { get; }                    // single-click highlight
```

The host (modal or window) decides what to do with `AssetActivated`; the panel performs no
side effects.

**Layout**
- **Per-kind tabs** (one per permitted kind): foldable **tree** + **incremental name filter**.
  No chips.
- **"All" tab** (when `ShowAllTab`): **flat list** + **kind filter chips** + incremental name
  filter. No tree.
- **Every row** shows a leading **kind icon** (§5.2) before the `Name`, in both tree and flat
  list.

**Tree construction** uses the shared `FolderTreePicker` in read mode (§18.3):
- File kinds: folders from `SourceFilePath` relative to the kind's **`Assets/<Kind>` root**.
- Non-file kinds (Scenario): folders from splitting the relative scenario path on `'/'` (§19).
- `InitialFullPath` auto-expands ancestors + highlights the leaf.

**Incremental filter**: case-insensitive substring on `Name`; trees prune to matching leaves +
ancestors; flat list filters rows.

**Last-opened memory**: the host persists the last-activated relative path **per kind** in
editor session prefs (the `WindowManager` already serializes settings; add a per-kind map). On
open with no explicit target, pre-select/reveal the remembered path for the active tab.

### 10.2 Asset roots & the base-folder seam

`IAssetCatalogContributor` gains `string? BaseFolder { get; }` (already exposes `Kind`). The
**`Assets/<Kind>`** root is the browse base (see §16 for the root layout and the `AssetRoots`
constants). The panel computes each asset's tree path as `SourceFilePath` relative to that
base. Recipe roots (`Recipes/<Kind>`) are **not** browse roots and never appear here.

### 10.3 Hosts: modal picker and docked window

Same panel, two hosts:
- **Modal picker** — opened with `AssetBrowserPanelOptions` + a callback. On `AssetActivated`:
  close + invoke `Action<IEditableAsset?>` with the asset. **Esc** → close with `null`. Never
  executes.
- **Docked window** — a registry-registered `ManagedWindow` hosting the same panel; also
  generic: on `AssetActivated` it invokes the callback supplied by whoever registered it.

Both take the kind-filter options ("scenarios only", "blueprints only", combos).

### 10.4 `AssetKind.Scenario`, contributor, typed `Changed`

- Add `Scenario` to `AssetKind`.
- Add a `ScenarioCatalogContributor` (editor-host/orchestrator assembly, keeping AiShared free
  of an orchestrator dependency) projecting available scenarios into `IEditableAsset`s: `Name`
  = scenario relative path (may contain `/`, §19), `SourceFilePath` = empty,
  `IsEditorOwned = false`. Source: the editor-side scenario list. Raises `ContributorChanged`
  on update.
- **Typed change event** (architect fix): change `IAssetCatalog.Changed` to carry which
  `AssetKind` changed (e.g. `event Action<AssetKind>? Changed`, or
  `AssetCatalogChangedEventArgs`). `ReferenceCatalog.OnCatalogChanged` **ignores
  `AssetKind.Scenario`**, so scenario saves/creates do not trigger the full AI-reference rescan.
  All other subscribers updated to the new signature.

### 10.5 Callers (who executes the pick)

- Blueprint/BTree/HSM activation → editor caller invokes `AiDocumentManager.Open`.
- Scenario activation → editor caller invokes the editor-way load
  (`IEditorLogic.LoadScenarioByName`), loading into the single global ECS repo.

### 10.6 Retirement (after Phase 5/7 land)

Delete: `Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs` (open-docs section → §12),
`Hrot.Blueprints.Editor/AssetBrowserWindow.cs` + `FileSystemAssetCatalog` + its `IAssetCatalog`
(salvage scan logic into contributors).

---

## 11. Scenario / Document Unification (verdict)

**Catalog + presentation unified; storage left separate.** A scenario is the single global ECS
world state — no per-document `ViewState`, no perspective, not one-of-many, "activate" is
meaningless. So no model merge: `AiDocumentManager` and `IEditorLogic.LoadedScenarioName` stay
as-is. Scenarios become a pickable kind (§10.4); "what is live" is shown read-only (§12.2).

---

## 12. Scenario Menu Migration & Workspace Surface

### 12.1 Scenario lifecycle → main-menu commands

`ScenarioBrowserPanel` is deleted. Its actions become **shell commands** (§6) surfaced as
main-menu items via the menu adapter:

- **New** → `IEditorLogic.NewScenario`
- **Save** → `SaveCurrentScenario` (falls back to Save-As when none loaded)
- **Save As…** → the unified Save-As dialog (§18.2), scenario branch
- **Load…** → scenario-filtered Asset Browser modal (`Kinds = Scenario`); on pick → caller
  invokes `LoadScenarioByName`
- **Migration History…** → scenario diagnostic command (lists migration sidecars for the loaded
  scenario via `GetMigrationSidecarsForCurrentScenario`). It is scenario-only and belongs under
  the Scenario menu — **not** in any creation dialog or the browser.

### 12.2 Workspace submenu (dynamic, read-only)

The old browser's "OPEN docs" section is re-homed as a **dynamic main-menu submenu**
("Workspace", final name TBD) aggregating read-only:
- open documents from `AiDocumentManager.OpenDocuments` (active ●, dirty *), and
- the single loaded scenario from `IEditorLogic.LoadedScenarioName`,

each row prefixed with its kind icon. Selecting an open document activates it
(`AiDocumentManager.Activate`). Rebuilt each frame from live state.

---

## 13. Layering & Assembly Placement

| Component | Assembly | Rationale |
|---|---|---|
| `MainToolbarManager`, icon-widget overloads, separators | `Fdp.Presentation` (engine) | Reusable; beside `StatusBarManager`/`IconWidgets`. |
| Perspective menu + radio-group, per-perspective `IconKey` | `Fdp.Presentation` (`WindowManager`) | Perspective is a WindowManager concept. |
| `IIconProvider` impl / key maps | reuse/extend `SilkIconProvider` (`Hrot.Editor.AiShared`) | Bridge lives here. |
| Shell `EditorCommandsImpl`, menu/toolbar adapters | editor composition root + small shared adapter | Shell-wide, editor-owned. |
| Time toolbar section + `TransportIcons` | `Hrot.Presentation` | Reads `ITimeTransportFacade`. |
| AI-debug toolbar group | `Hrot.Editor.AiShared` (over `IDebugSessionRegistry`) + blueprint extras in `Hrot.Blueprints.Editor` | Polymorphic seam in shared infra. |
| `AssetBrowserPanel`, `FolderTreePicker`, kind-filter, hosts | `Hrot.Editor.AiShared` | Depends only on catalog + `IIconProvider`. |
| `ScenarioCatalogContributor`, `AssetKind.Scenario` wiring | editor-host/orchestrator assembly | Keeps orchestrator dep out of AiShared. |
| Unified New/Save-As dialogs, `RecipeMetadata` (kind-agnostic), `INewAssetService` | `Hrot.Editor.AiShared` + per-kind impls in each editor assembly | Shared shell, per-kind minting. |
| `AssetRoots` constants, folder migration | `Hrot.AI.Behaviors` project + editor bootstrap | Single root authority. |

---

## 14. Toolbar Composition (groups, left→right)

```
[ Time ▸ play pause step | time | rate ] ║ [ Perspectives ▸ ◉ ○ ○ ] ║ [ AI Debug ▸ ▮▶ ⤺ ⤼ ⤽ ⏏ pos ] ║ [ 🗂 Asset Browser ]
```

Each `║` is a registered separator (§4.3). Time = vector icons (§7); Perspectives = radio
toggles (§8); AI Debug = command icons (§9); Asset Browser = a launcher command opening the
modal (§10.3). Sort-order ranges define group boundaries.

---

## 15. Risks & Notes

- **Toolbar height is data-driven but jitter-free** (§4.1.1): max over *all registered* entries,
  not visible ones — do not revert to per-frame measurement.
- **Per-document vs shell commands** (§6.1): AI-debug stepping lives in the shell set keyed off
  the *active* session; do not duplicate into each document set.
- **Typed `Changed` migration** (§10.4): changing the event signature touches every subscriber;
  audit them. The payoff is no AI-reference rescan on scenario churn.
- **Icon-map drift**: unknown `IconKey` degrades to text, never a wrong cell.
- **Folder migration is broad** (§16): many hardcoded paths + `.csproj` globs; do it first,
  behind tests, as a discrete phase.
- **Retirement ordering**: don't delete legacy windows until the docked host + scenario
  contributor land.

---

## 16. Folder Reorganization (prerequisite cleanup)

Today recipes live under the asset folder (`Blueprints/Recipes/`), so recipe and final assets
mix when scanning, and there is no separation of concerns. We reorganize to disjoint roots:

```
Hrot/Subsystems/Hrot.AI.Behaviors/
  Assets/            ← browsed by the Asset Browser (final assets only)
    Blueprints/  HSMs/  BTrees/
  Recipes/           ← consumed by the New Asset dialog only (never browsed)
    Blueprints/  HSMs/  BTrees/  Scenarios/   (Scenarios = initial-content seeds)
```

- **Scenarios have no `Assets/` root** — they are orchestrator/NAS-backed (`ScenariosRoot`).
  They **do** have a `Recipes/Scenarios/` root for initial-content seeds.
- **`AssetRoots` constants** become the single authority for both root families (replacing
  scattered literals such as `DiscoverRecipes`'s `Blueprints/Recipes`, `AiBehaviorsProjectPath`,
  per-contributor scan roots).
- Each kind now has **two roots**: an **Assets root** (browse/save destination) and a **Recipe
  root** (creation source).

**Migration work (broad, tested, done first):**
1. Move existing files: `Blueprints/*.bp.json` → `Assets/Blueprints/`,
   `Blueprints/Recipes/*` → `Recipes/Blueprints/`, `Machines/*` → `Assets/HSMs/`,
   `Trees/*` → `Assets/BTrees/`.
2. Update every hardcoded path to read from `AssetRoots` (contributors' scan roots,
   `DiscoverRecipes`, the recipe-save path in `EditorSubsystem`, etc.).
3. Update `.csproj` content/copy globs that ship these files to the output directory.
4. Keep scan recursive (`SearchOption.AllDirectories`) so subfolders under each root work.

This phase changes no UI; it is purely structural and lands before the creation work (§17–§20).

---

## 17. Asset Creation & Recipes — Model

A **recipe** is an ordinary asset-of-its-kind that carries kind-agnostic recipe metadata and
lives under the kind's `Recipes/<Kind>` root. Final assets are created **from a recipe** (the
unified "always-recipe" flow).

- **Kind-agnostic `RecipeMetadata`** — lift `RecipeMetadata` (DisplayName, Difficulty,
  Category, Description, ConceptsTaught) out of blueprint-specific code into shared editor infra
  so Blueprint/BTree/HSM/Scenario reuse it.
- **Built-in "Empty" recipe is hardcoded in code** — there is **no** on-disk minimal JSON. Each
  kind's `INewAssetService` synthesizes a minimal valid asset in-code; to the operator it
  appears as a normal recipe named "Empty". This unifies "from scratch" and "from recipe" into
  one path.
- **Recipe discovery** — recipes for a kind are the assets found under `Recipes/<Kind>` (file
  kinds) plus the in-code "Empty"; for Scenario, seeds under `Recipes/Scenarios/` plus "Empty"
  (new empty world).
- **No authoring UI** — recipes are hand-curated (a technical user copies a file into the
  recipe root and edits its metadata). The system only *consumes* recipes.
- **All four kinds designed up front** (BP/BTree/HSM/Scenario), even though only Blueprint
  recipe *content* exists today.

---

## 18. Unified Creation/Save Dialogs & Folder Picker

### 18.1 `FolderTreePicker` (shared widget)

A folder-tree widget bounded to a single root, used in two modes:
- **read mode** — render-only tree (the browser, §10.1).
- **pick mode** — select an existing folder **or** add a new folder; yields a path **relative
  to the root**. No system dialog — the root is always enforced.

Used by the browser (read) and by New/Save-As (pick). Folder icons via `folder`/`folder_open`
keys (§5.1).

### 18.2 New Asset & Save-As dialogs

Two thin dialogs sharing the picker + a common validator (collision guard):

```
New Asset:  Kind · Recipe(▼ incl. "Empty") · Name · FolderTreePicker(pick) · [collision guard]
Save As:    Kind · (source = current document content) · Name · FolderTreePicker(pick) · [guard]
            → write to <Assets-root>/<relpath>/<name>.<ext>            (file kinds)
            → IEditorLogic.SaveScenarioAs("<relpath>/<name>")          (scenario backend, §19)
```

- **New** = choose recipe (incl. "Empty"), mint `AssetId` + `$meta`, clone from the recipe,
  write under the Assets root, then hand the new asset to the caller (open via
  `AiDocumentManager`; scenario → its backend).
- **Save-As** = take the current in-memory asset, pick destination, collision-check, write.
  Replaces the old flat Save-As modal (which had no subfolder support).
- The **collision guard** reuses the existing path-at-creation guard.

### 18.3 Per-kind minting

A common `INewAssetService` (per kind) behind the dialog: `NewFromRecipeService` (Blueprint,
exists) + new BTree/HSM/Scenario implementations that mint identity and write JSON via the
existing persistence services (`BTreeJsonServices`, `HsmJsonServices`) or, for scenario, route
to `IEditorLogic` (§19).

### 18.4 Subfolder-aware save for files

File-based save now takes the picked relative subfolder; load already recurses, so only
authoring was flat. New/Save-As write to `<Assets-root>/<subfolder>/<name>.<ext>`.

### 18.5 Save-As identity rule (collision-safe)

`AssetCatalog` deduplicates by `AssetId` (last-writer-wins), so two on-disk files sharing a
GUID would silently swallow one asset. Therefore:

- **Save-As always produces a new file with a freshly minted `AssetId`** and a `Name` derived
  from the chosen file name — i.e. Save-As has **duplicate** semantics. This guarantees the
  catalog sees two distinct assets and never collides. (Consistent with the New-Asset flow,
  which also mints a fresh identity when cloning a recipe.)
- **`Save` with a null/empty `SourceFilePath`** (an in-memory asset never written) **redirects
  into Save-As**, replacing today's `NoSourcePath` dead-end ([SaveActiveBlueprintCommand](Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/SaveActiveBlueprintCommand.cs) `SaveStatus.NoSourcePath`).
- **Rename / move is *not* Save-As** — it is the existing refactor/rename service, which
  preserves identity and rewrites references. Do not route rename through Save-As (that would
  fork identity).

> **Out of scope:** an "initial-save / promotion" path that *retains* a pre-existing `AssetId`
> when first serializing an assembly-projected asset to JSON. No legacy or shipped assets exist
> (pre-ship), so there is no identity to preserve; Save-As uses duplicate semantics uniformly.

---

## 19. Scenario Subfolder Support (in scope)

Scenarios become organizable into subfolders. Today a scenario is `ScenariosRoot/<name>/
scenario.json` with `<name>` a single segment. Changes:

- **Scenario name is a relative path** (e.g. `Combat/Patrol`). `SaveScenarioAs` and
  `SaveCurrentScenario` treat the name as a relative path under `ScenariosRoot` (still a
  per-scenario folder containing `scenario.json` + sidecars, now possibly nested).
- **`AvailableScenarios` enumerates nested relative paths**, not just top-level directory names.
- The **NAS layout** gains nested scenario folders; enumeration walks recursively to the
  `scenario.json` marker.
- The `ScenarioCatalogContributor` (§10.4) projects these relative paths as `Name`, so the
  browser's Scenario tab shows a folder tree (§10.1).

---

## 20. Save Commands & the Ctrl+S Fix

Today Ctrl+S is detected inline in the Blueprint-Tools panel render and gated on **that panel's
focus**, so it only fires when that panel is focused (not on the canvas) — and it bypasses the
command/hotkey system. Fix:

- **Save / Save As / Save All become editor commands** (§6) with key bindings (Ctrl+S,
  Ctrl+Shift+S), `IsEnabled` driven by the active document's dirty/source state.
- Dispatched by `EditorHotkeyDispatcher` at the **perspective** level (with its text-field
  gate), so Ctrl+S works regardless of focused sub-window.
- Surfaced as menu items and (optionally) toolbar icons via the adapters.
- **Save** acts on the active document (per-kind write path: blueprint reuses
  `SaveActiveBlueprintCommand`; BTree/HSM via their JSON services; scenario via
  `SaveCurrentScenario`). When the active document has **no `SourceFilePath`**, `Save`
  redirects into **Save-As** (§18.5). **Save As** → §18.2/§18.5 (mints a fresh `AssetId`).
  **Save All** = shell-wide.

---

## 21. Implementation Phases

Ordered so each builds on a tested predecessor. Task ids assigned in `TASK-DETAIL.md`.

**Phase 0 — Folder reorganization (prerequisite).** [§16]
- `AssetRoots` constants; move files; update all hardcoded paths + `.csproj` globs; verify
  recursive scans + recipe discovery against new roots.

**Phase 1 — Toolbar & icon infrastructure.** [§4, §5]
- `MainToolbarManager` (registry, sorted/perspective-filtered, **declared jitter-free height**,
  separators); `IconHandle`+size widget overloads + disabled state + `Tooltip`;
  `WindowManager.MainToolbar` + `Render`; `Program.cs` top inset; extend `IIconProvider` keys +
  `AssetKind → IconKey`.

**Phase 2 — Shell command set & binding adapters.** [§6]
- Shell `EditorCommandsImpl`; menu-binding adapter; toolbar-binding adapter; Save/Save-As/
  Save-All commands + `EditorHotkeyDispatcher` wiring (Ctrl+S fix, §20).

**Phase 3 — Toolbar groups: Time, Perspective, AI Debug.** [§7, §8, §9]
- `TransportIcons` + `MainToolbarTimeControlSection`; perspective relocation (menu +
  radio-group + per-perspective `IconKey`); AI-debug shell commands + polymorphic toolbar group.

**Phase 4 — Generic Asset Browser panel.** [§10.1–§10.2, §18.1]
- `FolderTreePicker` (read mode); `AssetBrowserPanel` (tabs/tree/All+chips/incremental filter/
  row icons/selection event); `AssetKindFilter`; auto-expand/select; last-opened-per-kind;
  `IAssetCatalogContributor.BaseFolder` over `Assets/<Kind>`.

**Phase 5 — Hosts, scenarios, typed change, wiring.** [§10.3–§10.5, §19]
- Modal + docked hosts (shared panel, callback return); `AssetKind.Scenario` +
  `ScenarioCatalogContributor`; **typed `IAssetCatalog.Changed`** + `ReferenceCatalog` ignore
  Scenario; scenario nested-name support; caller wiring (Open / LoadScenarioByName).

**Phase 6 — Unified creation & recipes.** [§17, §18]
- Kind-agnostic `RecipeMetadata`; `INewAssetService` per kind (+ hardcoded "Empty"); New Asset
  dialog; Save-As dialog; `FolderTreePicker` pick mode; collision guard reuse; subfolder-aware
  file save.

**Phase 7 — Scenario menu, Workspace, retirement.** [§12, §10.6]
- Scenario lifecycle shell commands + main-menu items; Load → scenario-filtered modal;
  Migration History scenario command; delete `ScenarioBrowserPanel`; Workspace dynamic submenu;
  retire both legacy `AssetBrowserWindow`s + `FileSystemAssetCatalog`.

---

## 22. Design-Talk Coverage Checklist

- [x] 64×64 icon toolbar, tooltips, click/toggle/disabled — §4.1, §4.2
- [x] Generic, perspective-aware, horizontally stacked — §4.1
- [x] Jitter-free, data-driven (declared) height — §4.1.1, §15
- [x] Group separators (vertical line) — §4.3, §14
- [x] Time bundle (start/pause/step, time, multiplier), reuse vectors — §7
- [x] Reuse vectors **and** support scaled famfamfam atlas icons — §4.2, §5, §7
- [x] Asset Browser as modal **and** permanent window, shared rendering — §10.3
- [x] Tabs per kind + "All" tab with chips — §10.1
- [x] Tree from disk path (files) / from name slashes (non-file) — §10.1, §19
- [x] Per-kind base folder; orchestrator-sourced scenarios — §10.2, §10.4, §16
- [x] Incremental name filter everywhere; chips only on flat list — §10.1
- [x] Per-row kind icons in flat list and tree — §10.1, §5.2
- [x] Remember last-opened per kind; optional (kind, path) open arg — §10.1
- [x] Modal/window returns picked asset; caller executes (generic) — §3.3, §10.3, §10.5
- [x] Configurable kind filter (scenarios only / blueprints only / combos) — §10.1
- [x] Asset-browser launcher icon on the toolbar — §14
- [x] Menus, toolbar, hotkeys use the editor action system — §3.1, §6
- [x] Shell-level command set — §6.1
- [x] Perspective out of menu bar → menu + toolbar radio-group — §8
- [x] Perspective stays WindowManager-native — §3.6, §8
- [x] AI debug icons (Continue/Step…) as a separate, **polymorphic** group — §9
- [x] Scenario buttons → main-menu items; Load → scenario-filtered picker — §12.1
- [x] Plain scenario button panel removed — §12.1
- [x] "Currently open" → dynamic submenu (open docs + loaded scenario) — §12.2
- [x] Unification verdict: catalog + presentation, not model — §11
- [x] Semantic `IconKey` via `IIconProvider`; multi-atlas — §5
- [x] `AssetKind → IconKey` (not → coordinate) — §5.2
- [x] Retire legacy browsers + `FileSystemAssetCatalog`, salvage scan — §10.6
- [x] Single new asset browser, nothing legacy — §10, §10.6
- [x] AI-debug polymorphism via `IDebugSessionRegistry` (architect #1) — §9
- [x] Typed catalog `Changed`; ReferenceCatalog ignores Scenario (architect #3) — §10.4
- [x] Unified New/Save-As dialog (architect #4, reshaped) — §18
- [x] Generalized recipes, hardcoded "Empty", no authoring UI — §17
- [x] Folder reorg: `Assets/` vs `Recipes/` roots (prerequisite) — §16
- [x] Scenario subfolder support (relative paths) — §19
- [x] Shared `FolderTreePicker` (read + pick), root-bounded, add-folder — §18.1
- [x] Full path relative to asset root; label by Name, place by relpath — §3.9, §10.1
- [x] Save/Save-As/Save-All as commands; Ctrl+S focus bug fixed — §20
- [x] Save-As mints fresh `AssetId` (collision-safe); rename ≠ Save-As — §18.5
- [x] Migration History → scenario-menu diagnostic — §12.1
