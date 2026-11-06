<!--STATUS
state: LIVE
build-state: DESIGN — top-level, for the user's review (R-4122); the building lanes detail it during implementation (R-4123)
updated: 2026-11-06 (first version — coordinator; editing-UX design queue item 1: E9 + E1 core)
current-answer: §2 the model (diagrams) · §3 how it reconciles the earlier designs · §4 the stages · §5 questions with leans
stale-below: nothing
known-rot: none
known-conflict: AQ26-B says a registration omits checked / icon / shortcut — the built menus use all three; §3 row 1 resolves it. UX_Feature_Entity_Action_Vocabulary / Cross_Surface_Actions STATUS lines predate the built HostActions path — §3 rows 3–4
related-designs:
  - DESIGN_Editing_UX.md — the programme: §3.2 (Requires + mode on tabs and actions), §3.6 (the action table), §3.13 (menus per surface and mode), D2, D4, D13–D15
  - Architect_Question_26_Entity_Action_Model.md — the action model this design BUILDS (RegisterAction(descriptor, isApplicable, isEnabled, execute), AND over the selection, C1 on GlobalActionRegistry, the two axes)
  - UX_Feature_Entity_Action_Vocabulary.md — the descriptor / binding split, ActionConcurrency, CancelsModalTool (UXI-03)
  - UX_Feature_Cross_Surface_Actions.md — the id rule: a map item needs a GlobalActionRegistry binding (UXI-04)
  - UX_Feature_Menu_Follows_Focus.md — the main menu bar (built, CE-1243): MenuBinding per perspective, RegisterContributor with ContextMenuItemDto
  - ../designs/gizmos-1/canvas-context-menu-design.md — §10–§12 the built path: MenuJsonFor re-served to inspector and ORBAT, HostActions, Has() filtering
  - UX_Feature_Tool_Model.md · Architect_Question_27_Tool_Model.md — "an action activates a tool"; ToolDescriptor; central Escape (step 6)
  - ../blueprints/DESIGN_Details_Panel_View_Switching.md — DetailsViewDescriptor / DetailsContext, which share this design's Requires and mode
  - ../DESIGN_Remote_Edit_Outcome.md — an action's edit goes through the one edit framework (E0) and reports its outcome
  - ../DESIGN_Node_Features.md — the station's editing level (D4) is a node feature the context carries
-->

# DESIGN — the action catalogue: one declaration per user action, every menu and tab built from it

## 0. Why, and what the user ruled

🔒 *"menu building should be unified and shared (it was already one of UX designs) - needs design"* · *"what to show in context menus for
various stuff. Including the context menu on the timeline - there are many possibilities, the question is what makes sense in what mode"*
· R-4119: editing is gated by the entity's components, never by the node · R-4121: editing levels per node, scarce live confirmations.
Programme decisions it implements: **D2** (tabs registered once, with `Requires`), **D13** (one catalogue builds every menu), **D14**
(what each surface offers per mode), **D15** (no map orders in offline Edit), **D4** (editing levels).

## 1. INVENTORY *(2026-11-06)*

Queries: `search_graph ".*(Action|Menu).*(Registry|Descriptor|Handler|Id|Ids|Provider|Populator)$"` Class (29), Interface (7);
`".*(MenuItem|CommandDescriptor|ContextMenuProvider|UiAction|ActionDescriptor).*"` (16); `".*ContextMenu.*"` (44); grep for
`EntityActionContext|EntityActionGroup|ActionConcurrency|CancelsModalTool` (**0** — AQ26-B / UXI-03 never built). Coverage not checkable
(no `check_index_coverage` in the CLI).

| piece | where | state |
|---|---|---|
| `GlobalActionRegistry` — `Register(int, handler)` (throws on a duplicate), `RegisterLocal`, `TryGetHandler`, `Has(id)`; handler `(ISimulationView, Entity)` | `Hrot.Common/Interactions/GlobalActionRegistry.cs:15` | ✅ built — the dispatch backbone (AQ26 C1) |
| `GlobalActionIds` — 56 `const int` (orders, entity, edit, tools, view, designate, macro) | `Hrot.Common/Constants/GlobalActionIds.cs:10` | ✅ — 257 / 258 unused (HA-7 superseded) |
| `GlobalActionRequestedEvent { ActionId, Target, Relayed }` | `Hrot.Common/Events/GlobalActionRequestedEvent.cs:12` | ✅ |
| `HostActions` (one per host: `Actions`, `Dispatch`, `Request`) · `SharedHostActions.Create()` on all five hosts | `HostActions.cs:11`, `Map/SharedHostActions.cs:13` | ✅ canvas §12 |
| `ContextMenuProjectorGizmo.MenuJsonFor(view, entity)` — a hard-coded `if` chain over components (`EditablePolyline`, `RoutePlan`, else a unit menu), cached per flag set; served to map, inspector (`MapEntityMenuHandler`, filters `Has`) and ORBAT | `Hrot.Common/Diagnostics/Gizmos/ContextMenuProjectorGizmo.cs:45-66, 210` | ✅ — ⛔ the map popup does NOT filter `Has` (`GizmoMap.Presentation/UI/ContextMenuAdapter.cs`) |
| `ContextMenuItemDto` — id, label, icon, enabled, tooltip, shortcut, separator, children, checked, priority, style | FDP | ✅ the one menu DTO (map, main bar) |
| `MenuShortcuts` — a key runs the primary unit's menu item whose `Shortcut` matches | `Map/MenuShortcuts.cs:26` | ✅ CE-1076 |
| `CanvasMenuUpdateSystem` (empty ground: Add Entity ▸, a hand-typed Measure) | `Systems/CanvasMenuUpdateSystem.cs:23` | ✅ |
| `IContextMenuBuilder` + `LambdaEntityContextMenuHandler` (inspector extras: Rename, Delete, Mark Target… — closures, per host) | `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IEntityContextMenuHandler.cs:21,44` | ✅ — no tooltip / checked / shortcut; `PopulateMenu(collection)` is a no-op (`MapEntityMenuHandler.cs:52`) |
| timeline: `TimelineMenuItem(Label, Run, Enabled)` + `TimelineMenuContext(LaneId, Ticks, Frame, Event)` | `Fdp.Toolkits/ReplayBrowser/Timeline/ITimelineLaneProvider.cs:71,75` | ✅ — its own closures, outside the ids |
| main bar: `GlobalMenuRegistry` (`MenuBinding` per perspective, `RegisterContributor` with `ContextMenuItemDto` + `Action<int>`) | `GlobalMenuRegistry.cs:28,52,396` | ✅ CE-1243 |
| tools: `ToolDescriptor(Id, Label, Modality, Arbiter, ShowOnToolbar, ToggleOnReactivate)`, `IToolController` — "an action activates a tool" | `Tools/ToolDescriptor.cs:67` | ✅ |
| Details: `DetailsViewDescriptor(Id, Title, Rank, AppliesTo, Create)`; `DetailsContext(Focus, Selection, Entities, Asset, Perspective, Mode: VariableRunState)` | `Hrot.Editor.AiShared/Shell/DetailsViewDescriptor.cs:40`, `DetailsContext.cs:36` | ✅ |
| ⚠ **two mode vocabularies** | `VariableRunState {Planning, Running, Paused, Replay}` (Details) vs `RunArmed {Preview, Live, AAR}` + DAR (`RunMode/RunModeTypes.cs:15`) vs `ReplayState.IsActive` (menus) | — |
| dead | `SharedContextMenuPopulator`, `IEntityActionController`, `MapContextActionController` (0 production callers) | delete in stage 1 |

## 2. The model

### 2.1 Classes

```mermaid
classDiagram
  direction LR
  class ActionDecl {
    <<NEW, Hrot.Common, data>>
    Id : int (GlobalActionIds)
    Label
    Group : Edit|Orders|View|Review|Debug|Destructive
    Target : Unit|Graphic|Ground|Event|Moment|Rule|Row
    Requires : ComponentSet
    Modes : per mode Offered|Greyed|Hidden
    Level : Full|Instructor|Controller|Observer
    Execution : PerEntity|Selection
    Shortcut
    CancelsModalTool
    Confirm : never|live-destructive
  }
  class ActionCatalogue {
    <<NEW, Hrot.Common, one per process>>
    Declare(ActionDecl)
    Query(ActionContext) ActionOffer[]
  }
  class ActionContext {
    <<NEW>>
    Target kind + ids
    Selection
    Mode : EditingMode
    Perspective
    StationLevel
    Host : HostActions
  }
  class EditingMode { <<NEW enum>> Edit Preview Live Review }
  class HostActions { <<existing>> Actions : GlobalActionRegistry ; Dispatch ; Has(id) }
  class MenuComposer { <<NEW>> Compose(ActionContext) ContextMenuItemDto[] }
  class ContextMenuItemDto { <<existing>> }
  class DetailsViewDescriptor { <<existing>> + Requires + Modes }
  class ToolDescriptor { <<existing>> activated by an action }
  ActionCatalogue "1" o-- "*" ActionDecl
  ActionCatalogue ..> ActionContext : evaluates
  ActionContext --> EditingMode
  ActionContext --> HostActions : bound here?
  MenuComposer --> ActionCatalogue
  MenuComposer --> ContextMenuItemDto : builds
  DetailsViewDescriptor ..> ActionDecl : same Requires, Modes, Level
  ActionDecl ..> ToolDescriptor : may activate
```
*What the picture shows that prose hid: the DECLARATION (what an action is, what it needs, where and when it is offered) is data in ONE
catalogue; the BINDING (what runs) stays the existing per-host `GlobalActionRegistry` — so "offered here" is a lookup, never a host
remembering to register a menu item. Details tabs carry the SAME `Requires` / `Modes` / `Level`, so a tab and an action agree by
construction.*

| member | rule |
|---|---|
| `Requires` | the components every target must carry — for a multi-selection, **all** of them (AQ26: AND over the selection) |
| `Modes` | per `EditingMode`: Offered, Greyed (with a reason), Hidden — e.g. map orders: Edit Hidden, Preview / Live Offered, Review Hidden (D15) |
| `Level` | the lowest station level that may run it (D4); a station below it shows the item greyed: "this station is an observer" |
| binding | `HostActions.Has(id)` false ⇒ **greyed, "not available on this station"** — a gap to fix (R-4119), never silently clickable |
| `Confirm` | only `live-destructive` asks (R-4121: delete, end exercise, change many) |
| `Execution` | `PerEntity` — the event per target; `Selection` — once with the selection (AQ26-B) |
| `Shortcut` | declared here once; `MenuShortcuts` reads it (today: a display string in the DTO) |

### 2.2 One menu, any surface

```mermaid
sequenceDiagram
  participant S as Surface (map, outliner, Details row, timeline, mode indicator, key)
  participant C as MenuComposer
  participant K as ActionCatalogue
  participant H as HostActions (this station)
  participant E as Edit framework (E0)
  S->>C: Compose(context: target, selection, mode, level)
  C->>K: Query(context)
  K-->>C: offers: decl + Offered / Greyed(reason) per Requires, Modes, Level
  C->>H: Has(id) for each
  C-->>S: ContextMenuItemDto tree (groups ordered by mode, ≤ ~12 rows, submenus, tooltips)
  S->>H: user clicks id → GlobalActionRequestedEvent per target (PerEntity) or once (Selection)
  H->>E: the handler's edit (local or to the owner)
  E-->>S: outcome → Messages (✓ ✗ ⏳)
```
*What it shows: a surface only says WHAT was clicked; the composer is the one place that decides what is offered — today that logic is
spread over six mechanisms (Editing UX §1.6).*

### 2.3 Where it lives, who calls it

```mermaid
graph TD
  subgraph HrotCommon["Hrot.Common"]
    CAT[ActionCatalogue + ActionDecl]
    REG[GlobalActionRegistry / HostActions]
    MODE[EditingMode]
  end
  subgraph Presentation["Hrot.Presentation"]
    COMP[MenuComposer]
    MAP[map popup]
    ORB[outliner / ORBAT]
    DET[Details rows + tabs]
    TL[timeline]
    IND[mode indicator]
    KEYS[MenuShortcuts]
  end
  DECL[feature packs DECLARE actions at composition] --> CAT
  BIND[each host BINDS handlers] --> REG
  RUN[RunModeChoice + ReplayState] --> MODE
  MAP -->|right-click| COMP
  ORB -->|right-click| COMP
  DET -->|right-click, tab offer| COMP
  TL -->|right-click| COMP
  IND -->|open| COMP
  KEYS -->|key press| COMP
  COMP --> CAT
  COMP --> REG
  COMP --> MODE
  GRAPH[graph editors NodeEdit menus]:::out
  GRAPH -.->|stage 2| COMP
  classDef out stroke-dasharray: 5 5
```
*What it shows: nothing calls the composer per frame — only a right-click, a key press, opening a menu or a selection change for the tab
offer (hot path R-2084: the per-frame cost is zero). The graph editors' menus (dashed) stay their own world in stage 1 (§5 Q3).*

## 3. How it reconciles the earlier designs

| # | earlier statement | resolution |
|---|---|---|
| 1 | AQ26-B: a registration omits checked / icon / shortcut | the built menus use `Checked` (pins), `Shortcut` (keys that work) and `Tooltip`; ⭐ `Shortcut` is declared on the action (one source); `Checked` stays a binding-side dynamic value (like the dynamic label) |
| 2 | AQ26-D: "mode" = the subsystem, decided at composition; "is running is an ordinary isApplicable condition, not an axis" (user) | ⭐ kept: the subsystem axis is `HostActions.Has` (what this host binds) plus R-4119; the run state stays a CONDITION in the context — `EditingMode` — but declared as data per action (`Modes`) rather than coded in each predicate. ⛔ No new axis |
| 3 | UXI-03: the registry in `Hrot.Presentation`, per subsystem | the built `HostActions` is in `Hrot.Common`, per host object — ⭐ the catalogue goes beside it in `Hrot.Common` (one per process); bindings stay per host |
| 4 | UXI-03 / UXI-04: a typed C# registry, inspector first | the built path made the map's `MenuJsonFor` the single source; ⭐ the catalogue REPLACES `MenuJsonFor`'s `if` chain behind the same output (stage 1), so map, inspector, ORBAT and keys keep working |
| 5 | canvas §5 / §7: a `CanvasMenuUpdateSystem` variant per subsystem | ⭐ superseded: empty ground is one more target kind (`Ground`) in the one catalogue |
| 6 | Tool Model §560 still says `StealsFocus` | `CancelsModalTool` (the rest of the doc and Q27) |
| 7 | UXI-04: closure-only items cannot reach the map, yet the inspector still has closures (Rename, Delete, Mark Target) | ⭐ every inspector closure becomes a declared action with an id; the retired "Mark Target for N Units" (Tool Model §4.13) is deleted |
| 8 | main bar `MenuBinding.OnClick` (closures) vs context menus (ids) | ⭐ the main bar keeps its built registry; an item that is also an action (Delete, Rename, Center) references the action id, so its enablement comes from the catalogue |
| 9 | `DetailsContext.Mode : VariableRunState` vs `RunArmed` vs `ReplayState.IsActive` | ⭐ one derived `EditingMode` {Edit, Preview, Live, Review}, computed in one place from `RunModeChoice` + `ReplayState`; `DetailsContext` carries it (§5 Q1) |

## 4. Stages *(top level — the ui lane details each; every stage keeps today's menus unless the stage says otherwise)*

| stage | what | safety |
|---|---|---|
| **A1** | `ActionDecl`, `ActionCatalogue`, `EditingMode`, `MenuComposer`; declare TODAY's unit / graphic / ground items exactly as `MenuJsonFor` builds them; `MenuJsonFor` delegates | a golden of the menu JSON per entity kind × mode (the S0 pattern) must stay byte-identical; delete the three dead pieces |
| **A2** | the map popup filters by `Has` → greyed "not available on this station" | the golden moves ONLY for items without a handler, listed |
| **A3** | the target per surface and mode (Editing UX §3.13): Edit-group first in Edit, no map orders in Edit (D15), Review set, waypoint / spawn area / weather region get their own target kinds | the new goldens reviewed by the user |
| **A4** | multi-selection: AND over the selection, `PerEntity` / `Selection`; the inspector's multi menu; `MenuShortcuts` on all selected | rails per rule |
| **A5** | inspector closures → actions (Rename, Delete unified wording); the canvas menu as target `Ground` | one wording per action across surfaces |
| **A6** | timeline + mode indicator: targets `Event` / `Moment` / `Range` / `Playhead` / `Rule`; the run-mode moves declared once, worded once | — |
| **A7** | Details tabs: `Requires` / `Modes` / `Level` on `DetailsViewDescriptor`; tabs registered once in `EntityDetailsBundle` (D2) | the tab offer per entity kind × host is a golden — identical across hosts |
| **A8** | station levels (D4) read by the composer | needs the node feature (E10 / D4 design) |
| later | graph editors' menus through the composer (§5 Q3) | — |

## 5. Questions — leans for the user

| # | question | ⭐ lean | what would change it |
|---|---|---|---|
| **Q1** | one `EditingMode` replaces `VariableRunState` in `DetailsContext` | yes — one vocabulary; `Paused` is not a mode (a paused Live is Live) | a Details view that truly needs "paused" — then it reads the clock, not the mode |
| **Q2** | an item with no handler on this station | greyed with "not available on this station" — it exposes a gap R-4119 says to fix — and HIDDEN only when the target kind can never have it | if the greyed list gets long on a station by design (e.g. a Player) — then the station's level hides them |
| **Q3** | graph editors (BTree / HSM / Blueprint node menus) | outside stage 1 — they are NodeEdit's generic menus (AQ26 constraint 1: no engine concept in a generic component); later the same composer behind NodeEdit's provider seam | — |
| **Q4** | where declarations come from | each feature pack declares its actions at composition (orders by the orders pack, Embark by the embark pack …) — never one central list; the catalogue only holds them | — |
| **Q5** | the order of groups per mode | Edit: Edit · View · Properties · Debug — Preview / Live: Orders · Edit · View · Properties · Debug — Review: Review · View · Properties · Debug; Destructive last in every group | the user's practice |
