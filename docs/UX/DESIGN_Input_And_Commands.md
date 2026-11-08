<!--STATUS
state: LIVE
build-state: DESIGN — top-level; §5 the BUILD design for K1 (ui, 2026-11-07) — K1 BUILT, as-built in §5a; K2 · K3 · K7 BUILT (§6, §6c, §6d, ui 2026-11-08); K4 BUILT (§6g, ui 2026-11-08) — K1–K7 all built, the overrides file / rebind UI (Q6) open; ✅ approved by the user 2026-11-06 with the dumb-terminal split and hard-coded defaults (R-4132); the building lanes detail it (R-4123)
updated: 2026-11-08 (K4 — §6g, ui Z-7: Hold / Axis rows, the FPS station, the timeline, the Player's map keys, the debug F-keys) · 2026-11-08 (K5 + K6 — §6e / §6f, ui Z-6) · 2026-11-08 (K2 rest + K3 + K7 BUILT — §6 / §6c / §6d, ui Z-5) · 2026-11-06 (§2.4 the map is a dumb terminal: map keys resolved on the backend, ImGui routing for the ImGui side only; hard-coded defaults complete, the overrides file optional — R-4132) · 2026-11-06 (first version — coordinator; editing-UX design queue item 4b: E14)
current-answer: §2 the model · §3 stages · §4 questions with leans · §5 the K1 build design (homes, the chord, the id, the per-frame call) · §6 / §6c / §6d the map: hat + camera scopes, one-level Esc, the mouse gestures (as built) · §6g held inputs: Hold / Axis rows (as built)
stale-below: nothing
known-rot: the node called IG here is the Player node since CE-3371 (backend, 2026-11-06): Hrot.IG → Hrot.Player, IgApplication → PlayerApplication (line numbers cited before the rename may have moved)
known-conflict: UX_Feature_Tool_Model.md:302 "No keyboard shortcut activates any tool" — kept for TOOLS armed by a key? §4 Q5 decides (lean: a key may run the ACTION that arms a tool)
related-designs:
  - DESIGN_Map_Interaction.md — E8: the drawing keys (Enter finish, Backspace, PgUp / PgDn level) as the armed tool's own keys on the backend
  - ../designs/gizmos-1/gizmo-input-focus-design.md — OWNS the dumb terminal (raw events, backend focus); the map scopes of the keymap are resolved on its backend side
  - DESIGN_Action_Catalogue.md — every key binds to an ACTION ID from the catalogue; the action declares its default key and scope (design 1, R-4125)
  - DESIGN_Seats_And_Hats.md — each hat is a keymap scope; Esc = one level out (R-4131 Q6)
  - DESIGN_Editing_UX.md — the programme: E14; §3.7 the expected basics (Esc, Enter, Ctrl+Z, copy / paste, Delete asks)
  - UX_Feature_Menu_Follows_Focus.md — OWNS the main menu bar (GlobalMenuRegistry, MenuBinding per perspective); its items bind action ids here
  - UX_Feature_Tool_Model.md — OWNS tools (ToolDescriptor, ToolController); central Escape (migration step 6) is built here
  - ../DESIGN_Entity_Control.md — §13.6 MenuShortcuts (CE-1076), §8b–§8r the FPS keys — both move into the keymap
  - ../DESIGN_Director_Timeline_Editor.md — the timeline keymap ("panel focused only, never stolen from the map") is one scope here
-->

# DESIGN — input and commands: one keymap, scoped, every key an action

## 0. Why

🔒 User, `2026-11-06`: *"the key map to app functions scoped to concrete context should be solved on whole app level i guess, no idea how it
is now. same the unification of how command/actions are registered (this probably already exists as one of UX designs) and no ad hoc direct
calls to features etc to make the internal architecture clear."*

## 1. INVENTORY *(2026-11-06)*

Queries: `search_graph .*(Hotkey|Shortcut|Keymap|KeyBinding|Keybind).*` Class (**6**: `EditorHotkeyDispatcher`, NodeEdit `HotkeyDispatcher`,
`KeyBinding`, `MapShortcutKeyEvent`, `MenuShortcuts`, `MenuShortcutKeyLayer`); grep `IsKey*(` (**43 files**: 24 ImGui, 13 raylib; **0** use
ImGui's own `Shortcut()` / key routing, available in our ImGui.NET **1.91.6.1**). Prior art: VS Code (keybindings with *when* contexts, one
JSON, a searchable shortcuts editor) · Unreal (UI commands bound in command lists per context) · Blender (a keymap per editor area).

| finding | evidence |
|---|---|
| **~12 separate key mechanisms**, each with its own scope rule: shell hotkeys, graph-canvas hotkeys, NodeEdit canvas raw keys, the map's key queue, map-menu letters (`MenuShortcuts`), gizmo raw keys (12 gizmos handle Esc themselves), the RTS tool, the FPS station (raw raylib), the command wheel, the free-fly camera, the timeline panel, IG pan / zoom / undo, debug F-keys, panel-local Enter / Esc / Ctrl+Z | inventory table, file:line per mechanism |
| **focus does not separate the map from panels** — the map gets keys whenever the MOUSE is over it (`MapCanvas.cs:206`); ImGui keyboard nav is never enabled ⇒ a focused panel does not take keys away | **CE-4282** |
| **conflicts at the same moment**: A / S / E free-fly vs Attack / Strike / Edit (**CE-4281**, verified) · Space timeline vs map Select · Esc fires in several places (graph canvases are not focus-gated) · F3 / F8 / F9 debug vs graph editor · Delete reached by two graph paths | CE-4281, CE-4282 |
| **no rebinding, no persistence**; display and binding share a source only for `IEditorCommands` (`DefaultKey`) and the map menu (`Shortcut` text); FPS / timeline / IG / audio keys are shown nowhere; map order letters are auto-derived from the name (silent collisions) | `ContextMenuProjectorGizmo.cs:149` |
| **commands**: `GlobalActionRegistry` (56 int ids) · `GlobalMenuRegistry.RegisterItem(path, closure)` (9 production calls, all closures) · `RegisterContributor` (ids, 4) · `IEditorCommands` (string ids, the graph editors) · tools (no key) · a dead `RegisterShortcut` seam · direct calls from UI (`() => timelinePanel.RequestLoadFdp()`, `() => newAssetLauncher.OpenMacro()`, `_gizmoUndoStack.Undo()` from a key …) | inventory §5 |

## 2. The model

### 2.1 Classes

```mermaid
classDiagram
  direction LR
  class ActionDecl { <<design 1>> Id · Label · + DefaultKey · + KeyScope · + KeyKind }
  class Keymap {
    <<NEW, one per app>>
    Bindings
    Overrides : the user's settings file
    Validate() : no chord twice in overlapping scopes
  }
  class KeyBinding { <<NEW>> Chord · Scope · ActionId · Kind : Press|Hold|Axis }
  class InputScope {
    <<NEW, ordered — first match wins>>
    TextField · Modal · FocusedWindow(kind) · Hat(active) · Map · Global
  }
  class KeyRouter {
    <<NEW, per frame>>
    ScopeChain() from ImGui focus + hat + armed tool
    Dispatch(chord) → the first enabled binding
    Held(axis) → state for the active hat
  }
  class ActionCatalogue { <<design 1>> Query(context) · enabled? }
  class HostActions { <<existing>> Dispatch(id) }
  class ShortcutsWindow { <<NEW>> searchable, per scope, rebind (later) }
  ActionDecl ..> KeyBinding : default
  Keymap "1" o-- "*" KeyBinding
  KeyBinding --> InputScope
  KeyRouter --> Keymap
  KeyRouter ..> ActionCatalogue : is it enabled here?
  KeyRouter ..> HostActions : run the action
  ShortcutsWindow ..> Keymap
```
*What the picture shows that prose hid: a key never calls code — it names an ACTION; the action is the same one a menu item, a button or
the HTTP API runs; and WHICH binding wins is decided in ONE place from an ordered scope chain, not by twelve mechanisms each asking a
different question.*

### 2.2 The scope chain — who gets a key

```mermaid
sequenceDiagram
  participant K as key press
  participant R as KeyRouter
  participant C as ActionCatalogue
  participant A as HostActions
  K->>R: chord
  R->>R: chain = TextField? → Modal? → focused window (Timeline, Graph, Details …) → active hat (Edit / Command / Embodied / Observe / Direct) → Map → Global
  loop each scope, innermost first
    R->>C: a binding for this chord in this scope — enabled in this context?
  end
  C-->>R: the first enabled action (or none)
  R->>A: Dispatch(action id) — the key is consumed
```
*What it shows: typing in a text field never triggers a command; a focused panel owns its keys and the map gets nothing; on the map the
ACTIVE HAT's keys come before the map's general ones — so free-fly (Observe) A / S / E can never fire Attack / Strike / Edit.*

| | rule |
|---|---|
| **focus is ImGui's, one truth** | a focused ImGui window gets its scope; **clicking the map takes focus to the map** (ImGui window focus cleared) — ⛔ no longer "the mouse is over it" (CE-4282) |
| **held inputs are not commands** | W A S D, Shift sprint, Ctrl crouch, T wheel, RMB look: bindings of kind **Hold / Axis**, READ by the active hat each frame (rebindable and shown like the rest), never dispatched as actions |
| **ImGui's own routing** | panel and global scopes use ImGui 1.91's key routing (`Shortcut()` with route flags) underneath — ⛔ no second focus arbitration of our own |
| **Esc — central** | one chain: text field cancels → modal closes → the armed tool cancels → the hat steps out → nothing (Tool Model step 6; seats-and-hats Q6) — split between the local app and the backend, §2.4 |
| **declared, checked, shown** | an action declares its default key and scope (design 1); the keymap REFUSES at start-up a chord bound twice in overlapping scopes (a rail); menus, tooltips and a **Keyboard shortcuts** window (searchable, per scope, F1) show the SAME binding ⛔ no auto-derived letters |
| **defaults and rebinding** | ⭐ **the hard-coded defaults declared on the actions are the keymap** — the app works fully with NO settings file (user, `2026-11-06`: *"settings file presence should not be necessary, the app must work well with hard coded defaults"*); an OPTIONAL per-user overrides file, applied on top when present; a rebind UI later |
| **debug keys** | F-keys of the audio / performance overlays are actions in the Global scope, behind Expert — ⛔ never raw reads |

### 2.3 Commands — every entry point names an action

| entry point today | target |
|---|---|
| `GlobalActionRegistry` int ids (map, inspector, ORBAT) | ✅ already actions — the keymap binds them |
| `IEditorCommands` string ids (graph editors) | declared as actions scoped to the graph perspective (the catalogue's perspective condition, AQ26 D) — one registry |
| `GlobalMenuRegistry.RegisterItem(path, closure)` | the main bar item names an action id; the closure becomes the action's binding on that host ⛔ no closure-only menu items |
| buttons / toolbar calling a subsystem method | the button runs an action id |
| keys calling methods (`_gizmoUndoStack.Undo()`, `cursor.TogglePlay()`, `ToggleMessageLog()`) | key → action id |
| `IShellCommandRegistrar.RegisterShortcut` (no caller) | deleted |

### 2.4 The map is a dumb terminal — two key worlds, one keymap *(user, `2026-11-06`)*

🔒 *"from gizmo perspective the keys are provided by "remote dumb terminal" (like in remote viewer app) and our map just happens to be an
"remote dumb terminal" for gizmos - not sure how well that play with imgui shortcuts"* — ⭐ it does not, and must not: the gizmo design
(`designs/gizmos-1/gizmo-input-focus-design.md`) rules a **stateless dumb terminal** that *"blindly reflects hardware events declared by data
on the wire"*, with every meaning and the focus on the BACKEND (it names the terminal's hard-coded `Escape == Cancel` as the defect).

```mermaid
graph TD
  subgraph LOCAL["the local app (ImGui)"]
    PANELS["ImGui panels and the main bar — TextField · Modal · FocusedWindow · Global scopes — ImGui's own Shortcut routing"]
    FOCUS["who has the keyboard: a focused ImGui window, or the map (clicked)"]
  end
  subgraph TERM["the map = a dumb terminal (local OR a remote viewer)"]
    RAW["forwards raw key events while IT has the keyboard — no meaning"]
  end
  subgraph BACK["the backend (gizmos + host actions)"]
    CAP["1 · a gizmo that captured raw input (an armed tool) gets the key"]
    HAT["2 · the active hat's bindings"]
    MAPS["3 · the map scope's bindings"]
    ACT["→ action id → HostActions"]
  end
  FOCUS -->|the map has it| RAW
  FOCUS -->|a window has it| PANELS
  RAW -->|key events on the wire| CAP
  CAP -->|not consumed| HAT
  HAT -->|not bound| MAPS
  HAT --> ACT
  MAPS --> ACT
  PANELS --> ACT
```
*What the picture shows that prose hid: ONE keymap (one declaration per action, one settings source), but it is RESOLVED in two places —
the local app resolves the ImGui scopes with ImGui's routing; the map scopes are resolved on the BACKEND from the raw events any terminal
forwards, so a remote viewer gets exactly the same map keys as the local map, and the terminal stays dumb.*

| | rule |
|---|---|
| **the terminal's only job** | forward raw key events while it holds the keyboard (the local app: the map was clicked and no ImGui window is focused; a remote viewer: its window is focused) — ⛔ no binding, no meaning, no Esc |
| **the backend resolves map keys** | 1 · a gizmo that captured raw input (the existing `InputCaptureBinding`, an armed tool) — the tool interprets its own keys, Esc included (cancel → the tool controller pops it) · 2 · the active hat's bindings · 3 · the map scope's bindings → an action id |
| **central Esc, correctly split** | panels / modals: the local app (ImGui); on the map: the backend's chain (the armed tool's gizmo cancels → the hat steps out). ⛔ The earlier wording "gizmos stop handling Esc themselves" is SUPERSEDED: a gizmo keeps receiving Esc as a raw event — what is centralised is that Esc on the map always reaches ONE resolver on the backend, never the terminal |
| **ImGui `Shortcut()` routing** | only for the ImGui side (Q7) |


### 2.9 The graphical design *(R-4143)* — shortcuts as the user sees them: on every menu item (the keymap's binding)

![A unit's context menu in Live and in Review, the Edit menu's undo, the one live-destructive confirmation](menus-visual.svg)

*What the picture shows that the prose hid:* one catalogue makes every menu look and behave the same. Groups sit in a fixed order, every item shows its shortcut, a forbidden item is greyed with its reason, and confirmation is so rare that the one dialog is easy to read. Generated by `scripts/gen-menus-mockup.py`.


## 3. Stages *(ui owns it; each stage keeps today's keys working unless it says otherwise)*

| stage | what |
|---|---|
| **K1** | `Keymap` + `KeyBinding` + `KeyRouter` + the scope chain; focus = ImGui focus, click-to-focus the map (fixes CE-4282); the graph canvases gated by focus |
| **K2** ✅ | the map: hat scopes before the map scope; the map menu letters declared on the actions (fixes CE-4281); `MenuShortcuts` becomes the router's map scope — ⭐ **BUILT** (§6, as built; R-4169: letters stay on while following) |
| **K3** ✅ | central Esc (Tool Model step 6) — ⭐ **BUILT as ONE-LEVEL ownership (§6c)**; ⛔ "gizmos stop reading Esc" NOT done — superseded by §2.4 and Tool Model Q27-E (the gizmo keeps its own cleanup), see §6c |
| **K4** ✅ *(BUILT, §6g; ⛔ the Player's undo STOPPED — nothing records onto it, R-4120)* | the FPS station, the timeline, IG pan / zoom, debug F-keys onto bindings (Hold / Axis kinds for the held ones) |
| **K5** ✅ *(BUILT, §6e)* | `IEditorCommands` and `RegisterItem` closures as actions; buttons and keys call action ids; the dead seam deleted |
| **K7** ✅ *(R-4163; BUILT §6d)* | **every user input, not only keys**: mouse gestures on the map (left / right click on an entity, on ground, a drag, Shift / Ctrl variants) become bindings in the same scope chain; the map scope binds the selection rules (Action Catalogue §8) and *right click = the menu*; the **RTS scope** overrides only what it changes (§6b); `RtsCommandGizmo`'s hand-written mouse handling becomes its bindings | a rail per scope: the same click resolves to the menu in the map scope and to Move / Attack in the RTS scope; an unbound key (2-D / 3-D, Ctrl+S) still works in RTS |
| **K6** ✅ *(BUILT, §6f)* | the Keyboard shortcuts window; user overrides in a settings file; rebind UI later |

## 4. Questions — ✅ approved as revised *(🔒 user, `2026-11-06`: "otherwise it looks good")*

| # | question | ⭐ lean | rejected |
|---|---|---|---|
| **Q1** | one keymap, keys → action ids | **yes**: an action declares its default key and scope; the keymap holds bindings and the user's overrides | per-feature key handling — today's twelve mechanisms |
| **Q2** | the scope order | **TextField → Modal → focused window → active hat → Map → Global**, first enabled match wins | a flat global table — the same letter cannot mean two things in two contexts |
| **Q3** | focus | **ImGui's focus is the one truth on the local app; clicking the map focuses it** — then the map (a dumb terminal, local or remote) forwards raw keys and the BACKEND resolves them (§2.4) | mouse-over gives the map the keys — today's leak; ImGui shortcuts for map keys — breaks the dumb terminal and remote viewers |
| **Q4** | held keys (WASD, sprint, wheel) | **Hold / Axis bindings read by the active hat**, rebindable and shown, not actions | making movement keys actions |
| **Q5** | a key that arms a tool | **allowed** — the key runs the ACTION that arms the tool (supersedes "no keyboard shortcut activates any tool") | keep tools key-less — R (rotate) already arms a tool through the menu letter |
| **Q6** | rebinding | **hard-coded defaults are complete; an optional overrides file**, a rebind UI later; conflicts refused at start-up | a settings file required |
| **Q7** | ImGui key routing | **ImGui 1.91's `Shortcut()` routing for the ImGui side only** (panels, modals, the main bar, global); the map's keys are resolved on the backend from terminal events | one router for both — the terminal would have to know ImGui |


## 5. ⭐ The K1 build design *(ui, `2026-11-07`; `build-state: READY-TO-BUILD` for K1 — K2/K3/K5/K6 detailed when reached)*

INVENTORY: the worker's [`INVENTORY_Input_Keys.md`](INVENTORY_Input_Keys.md) (`ui@66bf0989e`: 207 key readers / 45 files, 41 Esc readers, 4
command registries in 3 id spaces, 5–6 key enums) + `scripts/cbm.sh search_graph .*(Hotkey|Shortcut|Keymap|KeyBinding|KeyRouter|InputScope|KeyChord|ActionDecl|ActionCatalogue).*`
(⇒ `EditorHotkeyDispatcher` (AiShared), NodeEdit's demo `HotkeyDispatcher`, `MapShortcutKeyEvent`, `MenuShortcuts`; ⛔ no Keymap / KeyRouter /
ActionDecl / ActionCatalogue; coverage could not be checked). Measured homes: `KeyBinding(EditorKey, KeyModifiers)` is a record in
`NodeEditor.Core` (`IEditorCommands.cs:46`), which pulls ImGui.NET; `EditorKey` / `KeyModifiers` live in **`NodeEditor.Primitives`** (net8.0,
**no references** — a leaf); `Hrot.Common` references `Fdp.Toolkits`, not NodeEdit; `Fdp.Presentation` references `Fdp.Toolkits` and
`NodeEditor.Core`; every managed window opens in ONE place, `ManagedWindow.cs:214` (`Gui.Begin`); `HostActions.Request(int, Entity)` publishes
`GlobalActionRequestedEvent` (`HostActions.cs:30`).

```mermaid
classDiagram
  direction LR
  class KeyBinding { <<existing, MOVED to NodeEditor.Primitives, namespace kept>> Key : EditorKey · Modifiers : KeyModifiers }
  class ActionRef { <<NEW, Fdp.Toolkits.Input>> Global(int GlobalActionIds) | Command(string IEditorCommands id) }
  class InputScope { <<NEW enum>> TextField · Modal · FocusedWindow · Hat · Map · Global }
  class KeymapEntry { <<NEW record>> Chord : KeyBinding · Scope · WindowKind? · Action : ActionRef · Kind : Press|Hold|Axis }
  class Keymap { <<NEW, Fdp.Toolkits.Input, one per process>> Declare(entry) · Validate() conflicts · Find(scope, kind, chord) · ApplyOverrides(file?) }
  class IKeyScopeWindow { <<NEW, Fdp.Presentation>> WindowKind · TryInvoke(ActionRef) }
  class KeyRouter { <<NEW, Fdp.Presentation, per frame>> NoteFocused(window) · MapHasKeyboard · Dispatch() }
  class ManagedWindow { <<existing>> Begin at :214 → router.NoteFocused when IsWindowFocused }
  class EditorHotkeyDispatcher { <<existing, becomes the keymap's Command source>> }
  class HostActions { <<existing>> Request(int, Entity) }
  class MapCanvas { <<existing>> key drain gated by router.MapHasKeyboard }
  Keymap "1" o-- "*" KeymapEntry
  KeymapEntry --> KeyBinding
  KeymapEntry --> ActionRef
  KeymapEntry --> InputScope
  KeyRouter --> Keymap
  ManagedWindow ..> KeyRouter : focus, read from ImGui
  KeyRouter ..> IKeyScopeWindow : Command refs
  KeyRouter ..> HostActions : Global refs
  EditorHotkeyDispatcher ..> Keymap : declares the graph commands' DefaultKey
  MapCanvas ..> KeyRouter : only when the map has the keyboard
```
*What the picture shows that the prose hid: one chord type already exists and only has to move one assembly down to be reachable from the
action declarations (Hrot.Common) and the map's backend resolver; the keymap holds BOTH id spaces honestly until K5 folds the graph commands
into the catalogue — no sixth key enum, no premature id migration.*

```mermaid
sequenceDiagram
  participant W as each ManagedWindow (Begin, :214)
  participant R as KeyRouter
  participant K as Keymap
  participant T as the focused window (IKeyScopeWindow)
  participant H as HostActions
  W->>R: NoteFocused(this) when ImGui says it is focused (RootAndChildWindows)
  Note over R: after every window drew (end of WindowManager.Render)
  R->>R: WantTextInput? → stop (text field owns keys)
  R->>R: a modal popup open? → Modal scope only
  loop each chord pressed this frame
    R->>K: Find(FocusedWindow + its kind) then Find(Global)
    K-->>R: the first entry
    alt Command ref
      R->>T: TryInvoke(ref) — its own IEditorCommands, enabled?
    else Global ref
      R->>H: Request(id, the selection)
    end
  end
```
*What it shows: ONE resolver per frame, after all windows drew; focus is read from ImGui (the one truth, Q3); a graph canvas only gets its
keys while focused — the CE-4282 Esc / Delete / Ctrl+Z firing in every drawn canvas ends here.*

```mermaid
graph TD
  WM["WindowManager.Render (every host's one frame)"] -->|each window| MW["ManagedWindow Begin :214 — NoteFocused"]
  WM -->|last| KR["KeyRouter.Dispatch — once per frame"]
  KR --> GC["the focused graph canvas: TryInvoke (replaces AiGraphCanvasWindow's own pump :445-472)"]
  KR --> SH["Global: the shell's commands (Ctrl+S) — replaces EditorSubsystem's pump :2808"]
  MC["MapCanvas.ProcessInputPipeline :232 — key drain"] -->|reads| MF["KeyRouter.MapHasKeyboard"]
  CLICK["a click on the map with no ImGui window hovered"] -->|"SetWindowFocus null, map takes the keyboard"| MF
  FOC["any ImGui window focused"] -->|map loses the keyboard| MF
```
*What it shows: who calls the router each frame (only the window manager, on every host by construction) and the one place the map's key
drain asks for the keyboard — the "mouse over an ImGui window" gate stays for the MOUSE only.*

| decision | why |
|---|---|
| ① the chord is NodeEdit's `KeyBinding(EditorKey, KeyModifiers)`, MOVED from `NodeEditor.Core` to the leaf `NodeEditor.Primitives`, namespace unchanged | reachable from `Fdp.Toolkits` / `Hrot.Common` without ImGui; zero call-site churn (project references are transitive); ⛔ no sixth key enum. `MapKeyboardKey` / Raylib keys map onto `EditorKey` at their adapters (`ImGuiInputSource.cs:194` already maps `EditorKey` ↔ `ImGuiKey`) |
| ② the keymap entry names the design's binding `KeymapEntry` | NodeEdit already owns the name `KeyBinding` for the chord |
| ③ `ActionRef` = `Global(int)` or `Command(string)` | the two id spaces exist today; B-11's catalogue owns the id unification (K5) — K1 binds both, honestly |
| ④ core types in `Fdp.Toolkits.Input`; the router in `Fdp.Presentation` | `Hrot.Common` (action declarations, K2's backend map scope) and `Fdp.Presentation` (ImGui) both reference `Fdp.Toolkits` |
| ⑤ focus read in ONE place: `ManagedWindow` after `Gui.Begin` (`:214`) asks ImGui `IsWindowFocused(RootAndChildWindows)` and notes itself | ⚠ **deviation from Q7's lean** ("ImGui `Shortcut()` routing underneath"): `Shortcut()` must be called inside each window's Begin/End, which would scatter the keymap lookup into every window; reading ImGui's focus once per window keeps ImGui the one truth AND one resolver. Same outcome Q7 wanted (no second focus arbitration) |
| ⑥ map focus = a click on the map with no ImGui window hovered → `ImGui.SetWindowFocus(null)` and `MapHasKeyboard = true`; any focused window clears it | the inventory's finding G1: "the map has the keyboard" does not exist today |
| ⑦ the Global scope keeps today's shell keys working (Ctrl+S regardless of focus — rail `SaveCommandsTests.Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow`) | K1 keeps every key working |

*Rejected:* **a new chord struct** — a sixth key type beside five · **string-only ids now** — B-11's call, not K1's · **ImGui `Shortcut()` per
window** — ⑤ · **the router inside `Hrot.Presentation`** — the window manager (FDP) could not call it, so a host could lack it.

**K1 acceptance (rails):** ① a text field focused ⇒ no keymap action fires · ② two graph canvases drawn, one focused ⇒ Ctrl+Z / Delete / Esc
reach ONLY the focused one (CE-4282) · ③ a focused panel ⇒ the map's key drain gets nothing; a click on the map ⇒ it gets the keys · ④ Ctrl+S
fires from any focus (the existing rail stays green) · ⑤ `Keymap.Validate` refuses the same chord twice in overlapping scopes · ⑥ every graph
command's `DefaultKey` appears in the keymap (one declaration source).

### 5a. ⭐ K1 as built *(ui, `2026-11-07`)* — what the build changed against the plan above

| | as built | why it differs |
|---|---|---|
| map focus | **a click decides it**: the router reads, once a frame, whether a mouse button went down and whether it landed on ImGui (`io.WantCaptureMouse`) — on the map ⇒ `KeyboardFocus.MapHasKeyboard = true`, on a window ⇒ false; a locked cursor (first person) takes it too | ImGui already clears its own focus on a click into the void (the dockspace's pass-through hole), so ⑥'s explicit `SetWindowFocus(null)` is not needed; and keying on the CLICK — not on "no window focused" — means a window that merely APPEARS (focus-on-appearing) does not steal the map's keys |
| `KeyboardFocus` | a process-wide static in `Fdp.Presentation.Input`, starting `true` | it complements ImGui's focus, and ImGui's context is process-wide; a host that drew no frame keeps today's behaviour |
| the map's drain | `MapCanvas.ProcessInputPipeline` runs the key drain BEFORE the mouse gate; `RaylibInputProvider.IsKeyboardCaptured` = `WantCaptureKeyboard \|\| (!CursorLocked && !MapHasKeyboard)` | the keyboard follows focus, the mouse gate stays for the mouse (CE-4282) |
| a disabled binding | falls through to the outer scope (the first ENABLED binding wins) | a canvas command that is off must not swallow Ctrl+S |
| removed | `EditorHotkeyDispatcher` (AiShared) — the canvas pump and the editor-only shell pump | ruling 9 — one implementation; the router does both, on every host. Its rails moved to the router (`BcpBatch02FixCanvasTests`, `SaveCommandsTests`) |
| moved | `ImGuiInputSource` → `Fdp.Presentation.Input` | the router (every host's `WindowManager`) reads keys through it |
| also gated | the audio layer's F6–F10 keys read only while the map has the keyboard | CE-4282 named F8 / F9 colliding with the graph editors |
| not yet | `ActionRef.Global` bindings dispatch through `KeyRouter.GlobalActions`, unwired until K2 (no K1 row uses it) · ⛔ SUPERSEDED by K4 (§6g): the player's F3 performance overlay is a keymap row · cinema mode skips the router | K2 / K3 |

**Rails:** `KeyRouterTests` (Fdp.Presentation.Tests — conflicts, scope chain, map-focus state, zero allocation, and a REAL ImGui frame through
`WindowManager.Render`: only the focused window gets Ctrl+F; a click on the void gives the map the keys) · `MapCanvasTests.B10_K1_…` (keys
follow focus, not the mouse) · `BcpBatch02FixCanvasTests.Router_*` (two canvases, one focused; typing; popups; the keymap declares the
canvas commands) · `SaveCommandsTests.Hotkey_CtrlS_…` (Global from any focus).

## 6. ⭐ K2 — the map scope *(ui, `2026-11-07`; ⭐ BUILT `2026-11-08` — the hat and camera scopes, declared letters; as built below)*

INVENTORY: `scripts/cbm.sh search_graph .*Hat.*` (Class) ⇒ **10, none a hat** (case-insensitive: Chat / That) — ⛔ no hat type exists; the
`HatArbiter` is [seats-and-hats](DESIGN_Seats_And_Hats.md) **H3** (B-16, after B-11). The map's letters today: `MenuShortcutKeyLayer`
(Hrot.Presentation, a map layer fed by the canvas's key drain) → `MapShortcutKeyEvent` → `MenuShortcuts` (backend system) resolves the
letter against the primary unit's menu (`ContextMenuProjectorGizmo.MenuJsonFor`) — ⭐ already the dumb-terminal split §2.4 asks for.

```mermaid
graph TD
  DRAIN["MapCanvas key drain — only while the map has the keyboard (K1)"] --> LAYER["MenuShortcutKeyLayer.HandleKeyInput"]
  LAYER -->|"① HAT scope: HatArbiter.Active = Embodied or Direct (R-4169)"| X1["stands down — the hat owns the keyboard"]
  LAYER -->|"② CAMERA scope: MapCamera3D.OwnsKey(key) — a follow's W A S D Esc · a flying free camera's W A S D E Q · a driver's own"| X2["stands down for THAT key only"]
  LAYER -->|"otherwise"| EV["MapShortcutKeyEvent (the terminal's raw key)"]
  EV --> MS["MenuShortcuts (backend) — the unit menu's DECLARED letter → GizmoMenuActionEvent"]
  HAT["HatArbiter (H3, built)"] -.->|"Func Hat"| LAYER
  CAM["MapCamera3D.Driver : ICameraDriver.OwnsKey"] -.->|"camera scope"| LAYER
```
*What it shows (as built): the two scopes in FRONT of the map scope are different things — a HAT owns the whole keyboard (read from the arbiter, never guessed from the camera), a CAMERA claims only the keys it reads. A follow camera is not a hat, so Center / Select … stay on while following.*

| piece | state |
|---|---|
| hat scope before the map scope | ✅ **built** — `MenuShortcutKeyLayer(…, hat)` reads `HatArbiter.Active` (the pack passes it); Embodied and Direct stand the letters down; Command (RTS) declares no key of its own, so the letters stay. ⛔ SUPERSEDES "stand down for ANY camera driver" |
| camera scope in front of the map scope (R-4169) | ✅ **built** — `ICameraDriver.OwnsKey` (default: every key — first person, fall, film director) · `FollowCameraDriver` overrides: Esc, W, A, S, D only · `MapCamera3D.OwnsKey`: the free camera owns W A S D E Q while it flies (RMB held). ⛔ SUPERSEDES the CE-4281 blanket stand-down (every key while flying) — it now claims only the keys the camera reads |
| the map letters declared on the actions (no auto-derived letters) | ✅ **built** — an action's letter is its `ActionDecl.Shortcut`; the ORDER letters are `MapMenuActions.OrderShortcuts` (Move M · Attack A · Hold H · Strike S · ObserveFire O). An order with no entry shows NO letter. ⛔ SUPERSEDES the letter derived from the order name (`OrderItem`). Rail `K2_OrderLettersAreDeclaredAndDistinct` names a new order that forgot its letter, and a collision with the unit menu |
| `MenuShortcuts` as the router's map scope | ✅ already the shape §2.4 wants: the backend resolves the terminal's raw key |

⭐ **Deviation, stated:** the plan said "declared on the actions" for orders too; an order's DTO is a behaviours-lane asset (the generator emits its `[Order]`), so its letter is declared in the MAP's table beside the other action letters — one place, checked, not on the DTO. ⏭ Moving it onto `[Order(Shortcut = …)]` is the behaviours lane's call.

*Rejected:* **a second hat state in the router** — the HatArbiter owns it · **stopping the camera reading W A S D while a unit is selected** — free-fly would stop working with a selection · **keeping the blanket stand-down for any driver** — R-4169: following is not a hat.

⛔ **HISTORY (SUPERSEDED `2026-11-08`):** the first K2 slice (`bc626e089`) stood the letters down for any camera driver and for every key while the free camera flew; the hat scope waited for the HatArbiter, the letters waited for B-11.

### 6c. ⭐ K3 — ONE Esc goes ONE level out *(ui, `2026-11-08`; built — ⚠ as ownership, not as "gizmos stop reading Esc")*

INVENTORY: `scripts/cbm.sh search_graph .*(Escape|Esc).*` Class ⇒ none a resolver; `search_code KeyboardKey.Escape` ⇒ 53 results — **16 gizmos** (`EntityPlacement` · `EntityRotator` · `Measure` · `MissionPlanEdit` · `Paste` · `RouteWaypoint` · `VertexEdit` · `ModalBoxSelection` · `ObstaclePlacement` · `RtsCommand` in Hrot; `AreaShape` · `PointSequence` · `EntityPicker` · `FdpLocationPicker` · `BoundingBoxPicker` in FDP; `DebugGizmoLayer` + ExtDeps examples), the camera drivers (`FollowCameraDriver:114`, `FpsCameraDriver.HandleInput:1760`, `FallScreen:72`) and the panels' own Esc. Before K3 ONE press could move TWO levels: a follow camera let go on the same Esc that cancelled a tool.

```mermaid
classDiagram
  direction LR
  class EscapeLevel { <<NEW enum>> Tool · Camera · Hat · RtsStops · Nothing }
  class EscapeChain { <<NEW static>> Resolve(toolArmed, cameraOwnsEscape, hat) EscapeLevel }
  class HatArbiter { <<existing>> Active · ToolHoldsEscape NEW · EscapeLevel(cameraOwnsEscape) NEW }
  class MapCamera3D { <<existing>> EscapeFree : Func bool NEW · EscapeIsFree NEW }
  class FollowCameraDriver { <<existing>> HandleInput: Esc lets go only if camera.EscapeIsFree }
  class ToolController { <<existing>> ActiveModal · PushModal }
  HatArbiter ..> EscapeChain : Resolve
  HatArbiter --> ToolController : ToolHoldsEscape = a modal tool that is not RTS
  MapCamera3D ..> HatArbiter : EscapeFree = not ToolHoldsEscape (the pack)
  FollowCameraDriver ..> MapCamera3D : EscapeIsFree
```
*What the picture shows that prose hid: there is no new Esc system — the ONE resolver is a pure function over three facts the station already has (an armed tool · a camera that hands back · the hat), and the one reader that could jump a level (the follow camera) asks it. The gizmos' own Esc handlers stay: they are the tool's cleanup.*

```mermaid
sequenceDiagram
  participant T as terminal (raw Esc)
  participant G as the armed gizmo (a pick / placement / edit)
  participant F as FollowCameraDriver
  participant A as HatArbiter
  T->>G: Esc — cancel + cleanup (a pick completes its awaiting task)
  T->>F: HandleInput — Esc
  F->>A: camera.EscapeIsFree ⇒ not ToolHoldsEscape
  A-->>F: false (a tool is armed) — the follow STAYS
  Note over T,A: next press, no tool: the follow lets go · next: the hat (Embodied: the driver releases) · RTS: Esc drops a half-done press and STOPS
```

| level, innermost first | who reads Esc | as built |
|---|---|---|
| a modal tool (a pick, a placement, an edit — **not** RTS) | its gizmo's `OnKeyEvent` | unchanged: cancel + cleanup |
| the camera scope | `FollowCameraDriver` | ✅ now yields while a tool holds Esc |
| the hat | Embodied: `FpsCameraDriver` / `FallScreen` release; Direct: the director | unchanged (the hat owns the keyboard — K2) |
| RTS | `RtsCommandGizmo` | unchanged: drops a half-done right press, stops (R-4163) |

⛔ **Deviation, with the measurement — "gizmos stop reading Esc" is NOT done.** The handoff and the stage table said so; §2.4 already SUPERSEDED it and Tool Model Q27-E says *"centralize for modal tools, keep local where necessary"*. Measured why: `EntityPickerGizmo.OnKeyEvent` runs `_onRemove(); _onCancelled()` — it COMPLETES its awaiting task as cancelled — while `ToolController.Cancel()` reaches only the gizmo's `OnCancel()`, which is empty there; a central cancel would leave every awaiting pick hanging. And `DebugGizmoLayer`/FDP pickers run on hosts with no `ToolController`. ⇒ the central part is the OWNERSHIP of the press, not its delivery. ⏭ needs the user: a `Cancel()` contract that completes pick tasks would let the gizmos drop their handlers.

### 6d. ⭐ K7 — mouse gestures are rows of the same keymap *(ui, `2026-11-08`; built — R-4163)*

INVENTORY: `scripts/cbm.sh search_graph .*(Gesture|MapGesture|InputBinding|Keymap|KeymapEntry).*` Class ⇒ the K1 `Keymap` / `KeymapEntry` (keys only) and unrelated gesture types (`Combat.Gestures`, `VariableEditGesture`, `TrackGesture`); the map's mouse today = `SelectionInteractionSystem` (left / right click rules, `S-4b`), the terminal's `DebugGizmoLayer.HandleInput` (opens the menu on a right release) and `RtsCommandGizmo.OnMouseEvent` (hand-written Left / Right branches). `InputBinding` (§6b's name for the grown `KeyBinding`) was NOT created: `KeyBinding` is NodeEdit's chord (a leaf assembly), so a gesture is its own record in `Fdp.Toolkit.Input`, one table with the keys.

```mermaid
classDiagram
  direction LR
  class Keymap { <<existing, Fdp.Toolkit.Input>> Declare key rows · DeclareGesture NEW · TryFindGesture NEW · ValidateGestures NEW }
  class GestureEntry { <<NEW record>> Gesture · Scope · Qualifier : the hat name · Action : ActionRef }
  class MapGesture { <<NEW record>> Button · Kind Click or Drag · Modifiers · Target mask }
  class GestureTarget { <<NEW flags>> Ground · Entity · Selected · Hostile }
  class MapGestureBindings { <<NEW static, Hrot.Presentation>> Declare keymap: the map scope + the RTS scope rows }
  class MapGestureRouter { <<NEW>> Shared · Resolve(hat, gesture) hat scope then map · ResolveMap }
  class SelectionInteractionSystem { <<existing>> runs the map scope's selection row }
  class RtsCommandGizmo { <<existing>> classify, resolve, run the rts row }
  class RtsCommands { <<existing>> TargetAt NEW · Move NEW · Attack NEW · Command = Attack }
  Keymap "1" o-- "*" GestureEntry
  GestureEntry --> MapGesture
  MapGesture --> GestureTarget
  MapGestureBindings ..> Keymap : declares
  MapGestureRouter --> Keymap
  SelectionInteractionSystem --> MapGestureRouter : ResolveMap
  RtsCommandGizmo --> MapGestureRouter : Resolve(Command, gesture)
  RtsCommandGizmo --> RtsCommands : the row's verb
```
*What the picture shows that prose hid: the gesture bindings are DATA (rows) and the two executors only look them up — the selection system for the map scope, the RTS gizmo for its scope. Rebinding is a keymap change, not a code change (rail `K7_TheGizmoRunsWhatItsScopeBinds…`).*

```mermaid
sequenceDiagram
  participant U as a right click on a hostile
  participant G as RtsCommandGizmo (Command hat)
  participant C as RtsCommands
  participant R as MapGestureRouter
  participant K as Keymap
  U->>G: OnMouseEvent press · release (a still click, CE-1077)
  G->>C: TargetAt(point) — Ground · Entity · Hostile
  G->>R: Resolve(Command, Right Click Hostile)
  R->>K: hat scope "Command" first
  K-->>R: rts.attack
  R-->>G: the row (scope Hat)
  G->>C: Attack(point)
  Note over U,K: the SAME click, hat Observe: Resolve finds no Command row, falls to the MAP scope ⇒ map.menu.entity — the TERMINAL opens the entity's menu (DebugGizmoLayer, unchanged)
```

| scope | gesture | row (action id) |
|---|---|---|
| **map** | left click · any entity (selected or not) | `map.select` — it becomes the selection alone (a member narrows) |
| | left click · ground | `map.clear` — clears (a rubber band's tiny drag is this click) |
| | left DRAG · ground | `map.band` — the rubber band |
| | right click · a SELECTED entity | `map.menu.selection` — its menu for the whole selection; the selection unchanged |
| | right click · an entity outside the selection | `map.menu.entity` — it becomes the selection alone; its menu |
| | right click · ground | `map.menu.empty` — the empty-space menu ONLY (R-4163) |
| **RTS** (Command hat) | left click · anything | `rts.select`; **Shift** → `rts.select.add` |
| | right click · ground / a non-hostile unit | `rts.move` |
| | right click · a hostile | `rts.attack` |
| | **Ctrl** + right click · any unit | `rts.attack.force` (CE-1132) |
| every other gesture and **every key** | — | not declared by RTS ⇒ falls through to the map scope / the global scope: 2-D / 3-D (Ctrl+M), Ctrl+S, the camera and the letters work in RTS by construction |

**As built, against the plan (§6b / the handoff):**

| | as built | why |
|---|---|---|
| the type | `MapGesture` + `GestureEntry` in `Fdp.Toolkit.Input`, rows in the SAME `Keymap`; ⛔ no `InputBinding` | §6d inventory: the chord type is a leaf assembly's |
| RTS right click on a friend / neutral | **Move** (RTS declares Ground + non-hostile entity) | the handoff said RTS overrides only "ground / hostile"; Entity Control §8a and rail `CE3171_…OrAFriendOrANeutral_MovesThere` say a friend is a move to its position — a friend falling to the map's menu would pop a menu in RTS. ⏭ needs the user if the menu is wanted there |
| the menu verbs (`map.menu.*`) | declared and resolved, but the MENU is still opened by the terminal (`DebugGizmoLayer`) and the selection effect by `SelectionInteractionSystem` | the dumb terminal (§2.4) opens a menu locally from the pick it made; moving the open onto a backend row is a wire change (⏭ the gizmo-input-focus design) |
| `Shared` router | one process keymap for the map's backend side; the ImGui `KeyRouter` kept its own `Keymap` — ⛔ SUPERSEDED by K6 (§6f): both are `Keymap.Process` now | the key router lives per `WindowManager`; K6 joined them |

### 6b. ⭐ The scope covers ALL input — keys AND mouse gestures; RTS is one scope *(R-4163, user `2026-11-07`)*

🔒 *"the scope definitely needs to cover both key presses and mouse gestures. It's not just something about key presses, it's about all user inputs."* · *"If it is selected, then there is no escape that could come out of it. You would need to go to the same menu and uncheck the item. So the RTS mode redefines the meaning of the gestures like mouse clicks and some keys, and keeping, for example, the map 2D / 3D switching"*

| rule | |
|---|---|
| **a binding is any user input** | a key, a chord, a mouse button on a TARGET (an entity — selected or not —, ground, a gizmo handle), a drag, with modifiers — one `KeyBinding` shape grown to an `InputBinding` (K7) |
| **RTS = a scope, switched ONLY by its checked menu item** (*View ▸ RTS command mode*, action 254) | stays checked across Edit → Preview → Live; active only in Preview / Live (no effect in Edit); ⛔ **Esc never leaves it** — Esc cancels a half-done gesture inside it (a target pick) and stops; possessing a unit overrides it for the duration, release returns to it |
| **override, never duplicate** | the RTS scope declares only what it changes (left click = select, right click on ground = Move, on a hostile = Attack, its letters); everything else (2-D / 3-D, camera, Ctrl+S) falls through to the map / global scopes by the existing first-enabled-wins rule |

```mermaid
graph TD
  IN["a key or a mouse gesture on the map"] --> RTS{"RTS scope (checked AND Preview / Live)"}
  RTS -->|"bound: RMB ground = Move, RMB hostile = Attack"| ACT["the action"]
  RTS -->|"not bound here"| MAP{"map scope: RMB = the menu, LMB = select, M = 2-D / 3-D"}
  MAP -->|bound| ACT
  MAP -->|"not bound here"| GLOB["global scope: Ctrl+S ..."]
  GLOB --> ACT
```
*What it shows: RTS changes the meaning of a click by sitting IN FRONT of the map scope — the map's own bindings are never copied, and keys and mouse share one chain.*

⛔ SUPERSEDED: Entity Control §8a's *"Esc leaves"* RTS tool and Seats and Hats Q6's *Esc → out of Command*.

⭐ **R-4169 (user, `2026-11-08`): letters stay on while following** — 🔒 *"Yes, letters stay on while following."* The map letters stand down only for the Embodied (first person) and Direct (director) hats, never for the follow camera (Observe); a follow camera's own keys, if any, are a camera scope in front of the map scope (R-4163).

### 6e. ⭐ K5 — a menu item that runs code NAMES AN ACTION *(ui, `2026-11-08`; built)*

INVENTORY: `scripts/cbm.sh search_code RegisterItem(` + grep ⇒ **9** production `GlobalMenu.RegisterItem(path, closure)` calls (`WindowManager` ×1 · `LocalWindowController` ×2 · `ReplayBrowserSubsystem` ×4 · `EditorSubsystem` ×3 — the inventory's "10" counted the adapter's own call) and **2** `RegisterCheckableItem(path, get, set)` closures (`LocalWindowController`); `RegisterContributor` ×5 (id-based, already actions); `IShellCommandRegistrar.RegisterShortcut` — **0 callers**. Buttons: **409** raw `ImGui.Button / MenuItem / SmallButton` sites in `Hrot/` + `FDP/` — ⛔ NOT converted: a panel-local button (Apply, OK, a row's trash can) is not an application command; what K5 converts is every command a MENU or KEY reaches. Keys that call methods (the Player's Ctrl+Z / Ctrl+Y / F3) are **K4** (held / debug keys onto bindings), not K5.

```mermaid
classDiagram
  direction LR
  class ShellEditorCommands { <<existing, Fdp.Presentation>> ONE catalogue of shell commands: Register · Get · Invoke }
  class EditorCommandDescriptor { <<existing>> Id · DisplayName · DefaultKey · IsEnabled · IsChecked · DynamicDisplayName }
  class MenuCommandAdapter { <<existing>> Register(menu, commands, id, path) · RegisterAction NEW · RegisterToggle NEW }
  class GlobalMenuRegistry { <<existing>> RegisterItem(path, closure) — no production caller any more }
  class KeyRouter { <<existing>> Global scope reads the shell commands' DefaultKey }
  ShellEditorCommands "1" o-- "*" EditorCommandDescriptor
  MenuCommandAdapter ..> ShellEditorCommands : declares the command
  MenuCommandAdapter ..> GlobalMenuRegistry : binds the item to Invoke(id)
  KeyRouter ..> ShellEditorCommands : Invoke(id) on the chord
```
*What the picture shows that prose hid: the closure did not go away — it became the HANDLER of a command in the ONE catalogue the keymap already reads, so a key, a menu click, a toolbar slot and a remote call reach the same code; nothing had to be invented, `ShellEditorCommands` was under-adopted.*

```mermaid
sequenceDiagram
  participant H as a host (Editor, Replay Browser, Player)
  participant A as MenuCommandAdapter
  participant C as ShellEditorCommands
  participant M as GlobalMenuRegistry
  H->>A: RegisterAction(menu, commands, "file.new-macro", path, run)
  A->>C: Register(descriptor "file.new-macro", handler = run)
  A->>M: RegisterItem(path, click → commands.Invoke("file.new-macro"))
  Note over C,M: a click, Ctrl+key (DefaultKey) or Invoke(id) all run `run`
```

| | as built |
|---|---|
| the 9 + 2 sites | converted to `RegisterAction` / `RegisterToggle`: `shell.ui-settings` · `shell.exit` · `settings.layout.save-default` (its enabled / label now the command's) · `settings.layout.reset-on-start` · `settings.sound-3d-camera` · `replay.recording-info.<perspective>` · `replay.open-exercise.<p>` · `replay.load-fdp.<p>` · `replay.json-export.<p>` · `debug.reset-all-unit-settings` · `file.new-macro` · `file.save-selection-as-macro` |
| the guard | rail `NoProductionMenuItemIsAClosureOnly` scans the production sources: a `GlobalMenu.RegisterItem(` / `RegisterCheckableItem(` outside the registry and the adapter fails |
| the dead seam | `IShellCommandRegistrar.RegisterShortcut` deleted (interface, capture, mock) |
| ⏭ | the map's `GlobalActionRegistry` ints and the shell's string ids stay two id spaces (`ActionRef.Global` / `Command`) — folding them is the catalogue's call (B-11), not K5's |

### 6f. ⭐ K6 — the Keyboard shortcuts window; ONE keymap *(ui, `2026-11-08`; built)*

INVENTORY: `scripts/cbm.sh search_graph .*(Shortcut|Keymap|KeyRouter).*` Class ⇒ `Keymap` · `KeymapEntry` · `KeyRouter` · `StructTableFocus` (reads the router's keymap) · `MenuShortcuts`; **two** `Keymap` instances in a process: each `WindowManager`'s `KeyRouter` made its own, and the map backend's `MapGestureRouter.Shared` another (§6d ⏭). Window precedent: `ManagedWindow` + `RegisterWindow` (e.g. `JsonExportWindow`).

```mermaid
classDiagram
  direction LR
  class Keymap { <<existing>> Process : the ONE instance NEW · Version NEW · SetLabel / LabelOf NEW · key rows · gesture rows }
  class KeyRouter { <<existing>> declares the shell + focused-window commands into the keymap it is given }
  class MapGestureBindings { <<existing>> Declare: gestures + the unit menu's letters NEW }
  class ShortcutRows { <<NEW, Fdp.Presentation>> Build(keymap, filter) cached by keymap.Version · Row(scope, chord, action label) }
  class KeyboardShortcutsWindow { <<NEW ManagedWindow>> search box · rows by scope · Ctrl+K }
  KeyRouter --> Keymap : Keymap.Process
  MapGestureBindings ..> Keymap : declares into Process
  KeyboardShortcutsWindow --> ShortcutRows
  ShortcutRows --> Keymap : reads rows + labels
```
*What the picture shows that prose hid: the window owns no list — it is a VIEW of the one keymap, so a binding that is not declared cannot be shown wrong, and one that is declared cannot be missing.*

```mermaid
sequenceDiagram
  participant U as the user (Ctrl+K · Edit ▸ Keyboard shortcuts…)
  participant K as KeyRouter (Global scope)
  participant W as KeyboardShortcutsWindow
  participant R as ShortcutRows
  participant M as Keymap.Process
  U->>K: Ctrl+K
  K->>W: command shell.keyboard-shortcuts → Show
  W->>R: Rows(filter) — only when keymap.Version or the filter changed
  R->>M: key rows · gesture rows · labels
  W-->>U: table: scope · chord / gesture · action (steady frame: 0 B)
```

| | as built |
|---|---|
| ONE keymap | `Keymap.Process` — `WindowManager` passes it to its `KeyRouter`; the map backend's `MapGestureRouter.Shared` is built over it. ⛔ SUPERSEDES the "two keymaps, ⏭ K6" row of §6d |
| what is listed | key rows (Global · focused-window kinds · the map's menu letters per target class — unit / area / zone / route — declared by `MapMenuActions.DeclareKeys` from the action catalogue + `OrderShortcuts`; the same letter on two classes is not a conflict, twice on one is) and the K7 gesture rows (map scope, RTS scope) — each with its action's label (`Keymap.SetLabel`, set by whoever declares) |
| not listed | a panel's own item-level keys (Enter / Esc in a text field) — not declared in the keymap. ⛔ SUPERSEDED for held keys: WASD, sprint and the rest are Hold / Axis rows since K4 (§6g), listed with their kind |
| user overrides file, rebind UI | ⏭ **not built**: Q6's "optional overrides file" has no stored-state home decided (settings location, per-host) — an architect question, not a build item; the window is read-only |
| the window | `KeyboardShortcutsWindow` (`Edit ▸ Keyboard shortcuts…`, command `shell.keyboard-shortcuts`, **Ctrl+K** — the mockup's key; ⛔ SUPERSEDES §2.2's "F1"): a search box over a three-column table (Where 130 · Shortcut 270 · Does), the scope named once per group, the chord in the accent colour; model `ShortcutRows` (rebuilt only when `Keymap.Version` or the filter moved). Mockup: `menus-visual.svg` panel F (`scripts/gen-menus-mockup.py`) |
| fidelity, measured | rail `OnARealFrame_CtrlKOpensTheWindow…` reads the drawn positions on a real ImGui frame: the search box above the first row, one x for every row's chord and action column, rows going down; ⚠ no pixel render was made (no SVG rasteriser on this machine) — the layout is asserted, the picture is the SVG |
| rails | `ShortcutsWindowTests` (rows by scope + filter, 0 B steady, real frame + Ctrl+K + measured layout) · `MenuOrdersTests.K6_TheMapLettersAndGestures…` — each red-proved by a compiling mutation |

⭐ **R-4176 (user-approved, `2026-11-08`) — where the item lives:** *Edit ▸ Keyboard shortcuts…* where the host's Edit menu offers anything else, else
*Help ▸ Keyboard shortcuts…* — ONE command, two paths, decided per frame by `WindowManager.EditMenuHoldsOtherItems` (`SetLayout` visibility); a host with no
Edit menu (Player, Replay Browser, SimHost …) gets none just for it. ⛔ SUPERSEDED: K6's "every host gets an Edit menu". `WindowManager.BarMenuShows` is the
draw loop's one "header shows" test, shared with the rails. Rail `TopLevelMenuOrderTests.R4176_…` (red-proved). Per-user overrides + *Rebind…*: PARKED (R-4176 #4).

### 6g. ⭐ K4 — held inputs are READ through the keymap: Hold and Axis rows *(ui, `2026-11-08`; ⭐ BUILT the same day — as built at the end of this section)*

INVENTORY: `scripts/cbm.sh search_graph .*(Keymap|KeyRouter|Binding|Gesture|Axis|InputAction|CameraDriver|FirstPerson|Fps|Possession|Debug.*Key).*` Class ⇒ the K1 `Keymap` /
`KeymapEntry` (`Trigger` already names `Hold` / `Axis`, **nothing reads them**), `FpsCameraDriver` · `FollowCameraDriver` · `FpsZoom` · `FpsCompass` (the FPS station), `TimelineAxis`
(a time axis, not an input one); same pattern, `label: Interface` ⇒ **0** (no input-state interface exists beside `IInputProvider` / `IInputSource`). `search_code` with limit 300:
`Raylib.IsKeyDown` **43** matches · `IsKeyPressed` **247** · `IsMouseButtonDown` **54** · `GetMouseWheelMove` **6** · `ImGui.IsKeyDown` **6** (⚠ coverage not checkable through the CLI).
Production raw reads, classified (the demos, `Stride/` and `FDP/Examples` are other apps and untouched):

| surface | raw reads today | ⇒ K4 |
|---|---|---|
| **FPS station** | `PossessionStation.FpsCameraDriver` 30 reads through `IInputProvider` (W A S D, Shift, Ctrl, Q E Z C Space, T wheel, B, Tab, F, 1–4, R L N O I X V J G M, Esc, RMB, LMB, the wheel, the mouse look) · `FallScreen` Esc / J / K | rows of the **Embodied hat** scope |
| **timeline** | `TimelinePanel.Keys` 17 `ImGui.IsKeyPressed` + the wheel / Shift / Ctrl / middle-drag reads in `PanAnywhere`, the ruler and the lanes | rows of the **Timeline** window scope |
| **Player map** | `PlayerApplication.HandleCameraInput` raw Raylib arrows + `= + −` · `HandleGizmoUndoInput` Ctrl+Z / Y | pan / zoom rows of the **Map** scope; ⛔ undo — see below |
| **debug F-keys** | `PerformanceOverlay` F3 · `AudioLayer` F6–F10 and its panel's arrows | rows of the **Global** scope, qualifier `debug` |
| *stays raw, by design* | a widget's own text editing and its `Enter` / `Esc` (the 41 Esc readers, K3) · `DebugGizmoLayer` (the dumb terminal's raw forward, §2.4) · `MenuShortcuts`' Ctrl / Alt flags (the terminal's raw modifiers) · a gizmo's own cleanup (Tool Model Q27-E) · `MapCamera3D` free-fly and `FollowCameraDriver` (camera scope, K2 `OwnsKey`; ⏭ the same rows) · `MapCanvas` / `MapCamera` mouse wheel and middle drag (the shared 2-D map, K7) | not K4's four surfaces |

⛔ **STOPPED, R-106 — the Player's Ctrl+Z / Ctrl+Y.** `PlayerApplication._gizmoUndoStack` is built (`:812`) and cleared on a world reset (`:1047`) but **nothing ever pushes onto it**
(`DataDrivenGizmoSystem`, the only recorder, takes it by constructor and the pack builds its own without one) — the keys call `Undo()` on a stack that is always empty. R-4120: *"undo does
not apply in Live or Preview"*, and the Player has no authoring; [Undo design](DESIGN_Edit_Mode_Undo.md) Q6: *"the IG's map undo goes with the Player seat (no authoring there)"*. ⇒ the
design says it should NOT exist; binding it would put a dead action in the keymap. **Left as is, not bound, reported (needs-user: delete the dead path).**

```mermaid
classDiagram
  direction LR
  class Keymap { <<existing>> Declare(row) · rows keyed by scope, qualifier, action, chord, trigger }
  class KeymapEntry { <<existing>> Chord · Scope · WindowKind : the qualifier · Action · Trigger : Press Hold Axis · Scale NEW }
  class EditorKey { <<existing, NodeEditor.Primitives>> keys + NEW KpAdd KpSubtract MouseLeft MouseRight MouseMiddle MouseWheel MouseWheelH MouseDeltaX MouseDeltaY }
  class IChordInput { <<NEW interface>> Modifiers · IsDown · IsPressed(allowRepeat) · Value }
  class KeymapReader { <<NEW, Fdp.Toolkits.Input>> scope + qualifier · Pressed · Held · Axis · rows cached per Keymap.Version }
  class ProviderChordInput { <<NEW>> over IInputProvider — the map's device }
  class ImGuiChordInput { <<NEW>> over ImGui — a panel's device }
  class ManualChordInput { <<NEW>> a rail's hand }
  class EmbodiedKeys { <<NEW, Hrot.Presentation>> Declare Embodied hat rows · action ids }
  class TimelineKeys { <<NEW, Fdp.Presentation>> Declare Timeline window rows }
  class PlayerKeys { <<NEW, Hrot.Player>> pan zoom rows, Map scope }
  class DebugKeys { <<NEW, Hrot.Presentation>> F3 and audio F6-F10 rows, Global scope }
  class FpsCameraDriver { <<existing>> HandleInput reads Keys }
  class TimelinePanel { <<existing>> Keys PanAnywhere read Keys }
  class AudioLayer { <<existing>> Keys reads rows }
  class ShortcutRows { <<existing K6>> shows the kind of every row }
  Keymap "1" o-- "*" KeymapEntry
  KeymapEntry --> EditorKey
  KeymapReader --> Keymap
  KeymapReader --> IChordInput
  ProviderChordInput ..|> IChordInput
  ImGuiChordInput ..|> IChordInput
  ManualChordInput ..|> IChordInput
  EmbodiedKeys ..> Keymap : declares
  TimelineKeys ..> Keymap : declares
  PlayerKeys ..> Keymap : declares
  DebugKeys ..> Keymap : declares
  FpsCameraDriver --> KeymapReader : Hat Embodied
  TimelinePanel --> KeymapReader : FocusedWindow Timeline
  AudioLayer --> KeymapReader : Global debug
  ShortcutRows --> Keymap : Press, Hold or Axis
```
*What the picture shows that prose hid: nothing new is a second table — `Keymap`, `KeymapEntry` and `Trigger` already held Hold and Axis and nothing read them; K4 adds the READER (one class, one
device interface) and the chord's missing sources (a mouse button, a mouse value). Each surface declares its rows beside the code that owns the effect and reads them through the same class.*

```mermaid
sequenceDiagram
  participant D as the device (Raylib / ImGui)
  participant I as IChordInput adapter
  participant R as KeymapReader (Embodied hat)
  participant K as Keymap.Process
  participant F as FpsCameraDriver.HandleInput
  F->>R: Axis(fps.move.forward)
  R->>K: rows of that action in (Hat, Embodied) — cached until Keymap.Version moves
  loop each row
    R->>I: IsDown(W) / IsDown(S) — modifiers named by the row held? shadowed by a row naming more?
    I->>D: key state
  end
  R-->>F: +1 − 0 (any positive minus any negative)
  F->>R: Held(fps.sprint) · Pressed(fps.map) · Axis(fps.look.x)
  Note over F,K: rebind W to another key in the keymap ⇒ the next frame walks on that key — no code change
```

```mermaid
graph TD
  WM["a host's frame"] -->|"every frame, locked cursor"| CAM["MapCamera3D.ICameraDriver.HandleInput ⇒ FpsCameraDriver ⇒ Embodied reader"]
  WM2["WindowManager.Render ⇒ the timeline's host window"] -->|"drawn and focused"| TL["TimelinePanel.Keys ⇒ Timeline reader"]
  PA["PlayerApplication.FrameInput"] -->|"active map owner, mouse free"| PAN["HandleCameraInput ⇒ Map reader"]
  PA2["PlayerApplication / AudioLayer.Update"] -->|"each frame"| DBG["F3 overlay · F6-F10 ⇒ Global debug reader"]
  KR["KeyRouter.Dispatch (K1)"] -.->|"runs Press rows of the Global + focused-window scopes only"| X["Hold and Axis rows are NEVER dispatched — a surface reads them"]
```
*What it shows: who asks, each frame — the camera driver, the focused panel, the Player's frame. ⚠ The K1 router stays the only DISPATCHER and still runs Press rows of Global and the focused
window's kind only; the Embodied / Timeline / Map-view / debug rows carry kinds it does not look at, so no key can fire twice.*

| rule | |
|---|---|
| a row fires when the modifiers it names are held | Shift+W still walks; ⭐ the row naming the MOST modifiers for a key wins — `J` and `Shift+J` are two actions and Shift+J is never also J |
| a chord with no key and only modifiers | a Hold row: `Shift` = sprint, `Ctrl` = crouch held |
| Axis | keys: any positive row down − any negative row down (Q and Space both held still climb once); a mouse value: multiplied by the row's `Scale` and added |
| rebinding | a row's chord is data; ⏭ the overrides file / UI stays K6's open question |

**As built, against the plan above (ui, `2026-11-08`):**

| surface | as built | rails |
|---|---|---|
| shared | `KeymapEntry.Scale`; `Keymap.Declare` is idempotent per (scope, qualifier, action, chord, trigger) — one action may have several chords (`Keymap.IsDeclared` stays per action); `Keymap.Unbind` (a rebind = unbind + declare; the action stays declared); `IChordInput` + `KeymapReader` (`Fdp.Toolkits.Input`); `RowDeclarer` (a pass keeps an action's chords together and never puts a default back over a rebind); adapters `ProviderChordInput` (the map's `IInputProvider`) / `ImGuiChordInput` / `ManualChordInput` (a rail's hand); `RaylibKeyCodes`; `EditorKey` gained the keypad +/−, three mouse buttons, the wheel, the horizontal wheel and the mouse movement; the Keyboard shortcuts window shows the kind (`Shift (hold)`, `W (axis)`, `S (axis −1)`) | `KeymapReaderTests` (9), `ShortcutsWindowTests.K4_…` |
| **FPS station** | `EmbodiedKeys` (Hat scope, qualifier `Embodied`): 17 Press + 10 Hold + 6 Axis actions; `FpsCameraDriver.HandleInput` / `UseKey` / `CommandWheelInput` and `FallScreen` read them; the 14 `KeyboardKey` constants are gone; the `AIM … (L: switch)` hint follows the keymap | `PossessionStationTests.Keymap` (declared / no conflict · today's keys + a rebind of an Axis, a modifier Hold, a mouse-button Hold and a Press · look inversion by `Scale` · Shift+J ≠ J · 0 B) |
| **timeline** | `TimelineKeys` (FocusedWindow, kind `Timeline`): Press rows (the zoom, arrows and undo repeat), 5 Axis (wheel · Ctrl+wheel · Shift+wheel · horizontal wheel · drag X), 1 Hold (the pan button); `TimelinePanel.Keys` / `PanFromInput` read them, no ImGui frame needed | `TimelineKeysTests` (5) + the scan |
| **Player map** | `PlayerKeys` (Map scope, qualifier `view`): arrows = 2 Axis actions, `=` / keypad `+` and `−` / keypad `−` = Press; `PlayerApplication.HandleCameraInput` reads them | `PlayerKeysTests` (3) |
| **debug F-keys** | `DebugKeys` (Global scope, qualifier `debug`, shown as *Debug keys*): F3 (Player overlay), F6–F10 and the audio panel's arrows; `PerformanceOverlay` / `AudioLayer` read them. ⚠ The K1 router dispatches only the Global rows with NO qualifier, so these are never double-fired | `AudioTests.K4_…`, `PlayerKeysTests.F3…` |
| the guard | `NoRawKeyReadsTests` scans the FPS station, the fall screen, the audio layer, the Player's frame code, the overlay and the timeline for a raw device read | red under one put back |

⛔ **Deviations, stated:**
- **Two matching rules, on purpose.** The K1 router fires a Press row only when its modifiers are EXACTLY the ones held (`Ctrl+S` is not `S`); a `KeymapReader` row fires when the modifiers it names are held and the row naming MORE wins. The FPS station needs it (Shift+W walks, C toggles crouch while Ctrl is held), and `J` / `Shift+J` are shadowed by the same rule.
- **"behind Expert" is not built.** No Expert switch exists for keys (the catalogue's Level belongs to menu items); the debug rows are only read by the surfaces that host them. ⏭ needs the user: tie the debug group to an Expert level when the catalogue's Level reaches keys.
- **The HUD key hints are still text.** Only the fire-mode hint follows the keymap; the FPS help line and the use / scoreboard hints name the default keys by hand (`PossessionStation.Hud`). ⏭ a follow-up: build them from `KeymapReader.ChordOf`.
- **Not converted, not K4's four:** the free-fly camera (`MapCamera3D`) and `FollowCameraDriver` keys (camera scope, K2's `OwnsKey`), `MapCanvas` / `MapCamera` wheel and middle drag (the shared 2-D map, K7), `MapViewModeLayer`'s Ctrl+M, and the timeline's pointer gestures (click, drag, double click, the Shift that turns a drag into a range). ⏭ the same rows, when each gets its turn.
- **Shift+wheel over the timeline's overview** only pans now; before, it panned AND zoomed (the plain-wheel row yields to the Shift row).
- **The Player's Ctrl+Z / Ctrl+Y** is NOT bound — §6g STOPPED. ⏭ needs the user: delete the dead path (`PlayerApplication._gizmoUndoStack`, `HandleGizmoUndoInput`) — lean yes: nothing records onto it and R-4120 / Undo Q6 say there is no undo there.
- **The overrides file / rebind UI** (Q6) is still not built; `Keymap.Unbind` + `Declare` is the primitive it will use.
