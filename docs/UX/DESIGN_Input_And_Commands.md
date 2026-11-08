<!--STATUS
state: LIVE
build-state: DESIGN — top-level; §5 the BUILD design for K1 (ui, 2026-11-07) — K1 BUILT, as-built in §5a; K2 · K3 · K7 BUILT (§6, §6c, §6d, ui 2026-11-08); ✅ approved by the user 2026-11-06 with the dumb-terminal split and hard-coded defaults (R-4132); the building lanes detail it (R-4123)
updated: 2026-11-08 (K2 rest + K3 + K7 BUILT — §6 / §6c / §6d, ui Z-5) · 2026-11-06 (§2.4 the map is a dumb terminal: map keys resolved on the backend, ImGui routing for the ImGui side only; hard-coded defaults complete, the overrides file optional — R-4132) · 2026-11-06 (first version — coordinator; editing-UX design queue item 4b: E14)
current-answer: §2 the model · §3 stages · §4 questions with leans · §5 the K1 build design (homes, the chord, the id, the per-frame call) · §6 / §6c / §6d the map: hat + camera scopes, one-level Esc, the mouse gestures (as built)
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
| **K4** | the FPS station, the timeline, IG pan / zoom / undo, debug F-keys onto bindings (Hold / Axis kinds for the held ones) |
| **K5** | `IEditorCommands` and `RegisterItem` closures as actions; buttons and keys call action ids; the dead seam deleted |
| **K7** ✅ *(R-4163; BUILT §6d)* | **every user input, not only keys**: mouse gestures on the map (left / right click on an entity, on ground, a drag, Shift / Ctrl variants) become bindings in the same scope chain; the map scope binds the selection rules (Action Catalogue §8) and *right click = the menu*; the **RTS scope** overrides only what it changes (§6b); `RtsCommandGizmo`'s hand-written mouse handling becomes its bindings | a rail per scope: the same click resolves to the menu in the map scope and to Move / Attack in the RTS scope; an unbound key (2-D / 3-D, Ctrl+S) still works in RTS |
| **K6** | the Keyboard shortcuts window; user overrides in a settings file; rebind UI later |

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
| not yet | `ActionRef.Global` bindings dispatch through `KeyRouter.GlobalActions`, unwired until K2 (no K1 row uses it) · the player's F3 performance overlay still reads raw Raylib (the player hosts no graph editor) · cinema mode skips the router | K2 / K3 |

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
| `Shared` router | one process keymap for the map's backend side; the ImGui `KeyRouter` keeps its own `Keymap` | the key router lives per `WindowManager`; ⏭ K6 (the shortcuts window) joins them |

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
