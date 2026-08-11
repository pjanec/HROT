# Scenario-Authoring UX — Requirements (`UXR`)

> **Status:** **v3**, 2026-08-06. **Source of truth for scope.**
> A task that does not trace to a `UXR-nn` does not belong in this programme.
> Journey spec: [UX_Golden_Path.md](UX_Golden_Path.md) · Design: [UX_Design.md](UX_Design.md) ·
> Tasks: [UX_Task_Tracker.md](UX_Task_Tracker.md) · Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

## How to read this

Each requirement is written so it can be **failed by a person in front of the running editor**. The
`Acceptance` column is the test; if it needs interpretation, the requirement is badly written — fix it.

`Now` records the **verified** current state at baseline. Entries marked ⚠ are *unverified* — they were
inferred and must be re-derived from code before any task builds on them
(see [Briefing §5.3](UX_Programme_Briefing.md#53-verify-before-you-build)).

**Priority:** `P0` = golden-path blocker (an ordinary author cannot finish the step) · `P1` = major
friction or a trust/data-loss risk · `P2` = polish.

### The two acceptance principles

Applied to every step of the golden path, in addition to the per-requirement acceptance:

- **A1 — Reachability.** Each step is reachable in **≤2 clicks** from the state the previous step left
  the editor in, with **zero window-opening detours**.
- **A2 — Legibility of outcome.** After every author gesture, the UI states what happened. Silence is
  a defect, not a neutral outcome.

## Who we are building for

**Two audiences, two surfaces, two different usability bars.** Answered by the user 2026-08-06 (OQ-1,
OQ-2); this split governs the whole programme.

| | **Path A — Authoring** | **Path B — Runtime intervention** |
|---|---|---|
| **Surface** | The editor (`--mode editor`, offline) | Distributed **ExCon** against a running exercise |
| **Audience** | **Engineers and advanced military SME** | **Ordinary SME people** |
| **Job** | Build a scenario from nothing; author and debug behaviors | Run an already-authored scenario and interfere with it live — add entities, assign mission plans |
| **Gestures** | Wide — the full golden path | **Narrow** — a handful, but they must be near-flawless |
| **Usability bar** | Learnable. A competent engineer should not need tribal knowledge | **Walk-up usable.** No engine vocabulary, no sequencing knowledge, no recoverable-by-expert-only states |
| **Scope** | G0–G6 below | **G7** below |

**Consequences that must not be forgotten:**

1. **Path A may assume competence, not clairvoyance.** Blueprint/BTree authoring stays on Path A —
   it does not need hiding behind a "designer mode". But *knowing which window to open* is
   clairvoyance, not competence, and remains a defect.
2. **Path B has the higher bar over a smaller surface.** Fewer requirements, stricter acceptance.
3. **Path B is where the distributed protocol is real.** OCC versioning, commit acks and conflicts
   genuinely exist there — so they must be **handled**, not exposed. An ordinary SME must never read
   the words "version conflict" or press "Force Commit".
4. **Both paths share panel code.** `MissionPanel`, the entity inspector and the ORBAT panel serve
   both. Requirements that differ between paths must be met by **presentation and defaults**, not by
   forking the panels.

---

## G0 — Front door & orientation

*"Where am I? What am I editing?"*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-01"></a>**UXR-01** | Launching the editor presents a **start surface** offering New Scenario, Open Recent, and Open… | On first frame after launch, an author who has never used HROT can begin a scenario without using the menu bar | No start screen; editor opens into a bare workspace | P0 |
| <a id="uxr-02"></a>**UXR-02** | **New Scenario is never a void.** It produces a scenario that can be run immediately — terrain resolved, camera framed, and either a starter unit or an explicit "place your first unit" affordance | Press New Scenario → press Play → the sim runs without an error | `NewScenario()` clears the world and nulls the name; nothing else (`EditorApplication.cs:138`) | P0 |
| <a id="uxr-03"></a>**UXR-03** | The **currently-edited scenario and its dirty state are always visible** without opening a menu | Window title or a persistent header shows `name*` at all times | Scenario name appears only inside the `Workspace` dynamic submenu (`WorkspaceMenuBuilder`) | P0 |
| <a id="uxr-04"></a>**UXR-04** | The editor's **default layout is a working layout.** The panels needed for the golden path are docked and visible on a fresh profile | Delete the layout profile, launch, walk the golden path — no window is opened manually | Perspective machinery exists (`PerspectiveWorkspaceRegistrar`); the Editor perspective's spine panels are stubs or unregistered | P0 |
| <a id="uxr-05"></a>**UXR-05** | A **searchable command palette** reaches every command and every window by name | One keystroke, type three letters of any command, invoke it | Absent — no palette/quick-open anywhere in the codebase | P1 |
| <a id="uxr-06"></a>**UXR-06** | Author-facing surfaces are **separated from developer plumbing** | No author-facing toolbar or default panel exposes cluster/assembly/allocator controls | `Go External` and `Reload BTrees` sit in the main authoring toolbar (`EditorToolbarPanel.cs:43-47`) | P1 |

## G1 — World composition

*"What is in my world? What is this thing?"*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-10"></a>**UXR-10** | A **real outliner**: every entity listed by name and type, in its ORBAT hierarchy, with icons | Open a scenario with 8 entities → identify each unit and its parent without clicking anything | `EditorOrbatPanel.DrawContent` prints `• [entityId]` — 27 lines, no names, no hierarchy | P0 |
| <a id="uxr-11"></a>**UXR-11** | **Selection is global and bidirectional** — outliner ↔ map ↔ inspector always agree | Click a unit on the map → it highlights in the outliner and fills the inspector, and vice versa | Map selection exists; outliner has no selection at all | P0 |
| <a id="uxr-12"></a>**UXR-12** | **Place an entity** by choosing what to place from a browsable catalog, then clicking the map | Place a tank without typing a type id or editing JSON | `Place Entity` activates a placement gizmo using `LastSelectedTkbType` (defaults to `Tank_M1Abrams`); ⚠ catalog-picker UX not yet traced | P0 |
| <a id="uxr-13"></a>**UXR-13** | **Rename, duplicate and delete** an entity from the outliner | Right-click a unit in the outliner → all three available and effective | No outliner interactions of any kind | P0 |
| <a id="uxr-14"></a>**UXR-14** | **One inspector** shows everything about the selected entity — identity, transform, components, behaviors — with engine-internal detail behind an *Advanced* disclosure | Select a unit → answer "what is this and what will it do?" from one panel | Split across Entity Property Inspector, Mission Panel, Entity Blueprints, StructEdit facets | P1 |
| <a id="uxr-15"></a>**UXR-15** | **No authoring gesture can silently destroy work.** Destructive actions confirm; the scenario autosaves; *Revert to Saved* always exists — see the rationale note below | Delete a platoon → confirmed first. Kill the editor mid-session → reopen and lose ≤1 autosave interval. *Revert to Saved* restores the last save exactly | **Nothing exists.** Zero `Undo` references in `Hrot.Editor` / `Hrot.Presentation`; ⚠ autosave and confirm-on-destructive not traced | P0 🔴 |
| <a id="uxr-16"></a>**UXR-16** | **Reusable entity templates** — save a configured unit (or group) as a template, place instances, override per instance | Configure a tank, save as template, place 3, change one without affecting the others | No scenario-level template/prefab concept. TKB types are the only reuse unit; a scenario entity is a verbatim ~20-component bag — **which is also why this may be cheap: the saved form already is the template** | P1 |
| <a id="uxr-17"></a>**UXR-17** | **Ctrl+Z works for the last authoring gesture where it is semantically sound** — single-step undo of placement, drag, rotate and delete, as a bounded scope | Place a unit, press Ctrl+Z → it is gone. Drag it, Ctrl+Z → it is back at the previous position | Absent. **Deliberately scoped to single-step; see the rationale note** | P1 |

| <a id="uxr-18"></a>**UXR-18** | **Map commands operate on the part of the map the user can see.** Centre-on-entity, frame-all, fit-selection, zoom-to-cursor and hit-testing all use the **effective viewport** — the un-occluded central region — not the whole OS window | Dock panels on the left and right, then *Centre on this entity* → the entity ends up in the **middle of the visible map**, not behind a panel | 🔴 **Nothing is occlusion-aware.** `MapCamera.Offset` is the anchor that would fix this, and the editor **never sets it** — the ctor leaves it `Vector2.Zero` (`MapCamera.cs:62`), so `FocusOn` should place the target at the window's top-left, under the docked panels. Other hosts use full-window or hardcoded centres (`IgApplication.cs:617`, `CgfSubsystem.cs:577`, `SimHostVisualization.cs:226`). ⚠ Code-derived — **confirm on the walk** | P0 🔴 |

> ### 📌 The map is a full-window Raylib layer, not a panel
>
> The 2D symbolic map is rendered by **Raylib across the whole OS window, behind ImGui**, which runs a
> dockspace with `PassthruCentralNode` (`Program.cs:347-349`) so the central node is transparent and the
> map shows through. **ImGui windows dock along the screen edges; the map is visible only where they are
> not.** In the BTree/HSM/Blueprint perspectives the map is not visible at all.
>
> 🔒 **It stays Raylib, for speed** — see [non-goal 5](#non-goals). Therefore the map's *screen* extent
> and its *visible* extent are two different rectangles, and [UXR-18](#uxr-18) exists because everything
> that reasons about "where the map is" must use the second one.

> ### 📌 Why there is no full undo requirement
>
> **Ruled by the user, 2026-08-06 (OQ-3): cheap safety first, not a general undo stack.** The reason is
> architectural, not budgetary: **the same editor code is reused inside the simulation runtime host**,
> and there a general undo is not merely expensive but *semantically impossible* — you cannot un-send a
> command to a running distributed simulation, nor un-run the frames it produced.
>
> A general undo model would therefore be an editor-only fiction layered over shared code, and would
> diverge the two paths exactly where [G7](#g7--runtime-intervention-excon) needs them to agree.
>
> So: **recoverability** ([UXR-15](#uxr-15)) is the P0 contract, single-step undo ([UXR-17](#uxr-17)) is
> a bounded convenience, and a general undo stack is a [non-goal](#non-goals). The equivalent safety on
> Path B is confirmation plus an unmistakable "this is live" affordance ([UXR-72](#uxr-72)).

## G2 — Behavior assignment

*"Tell this unit what to do."*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-20"></a>**UXR-20** | **One mental model for "what will this unit do."** Mission tasks and attached behavior graphs are presented in one place, in one vocabulary | Select a unit → see everything that drives it in a single section | Two unrelated models: MissionPlan tasks (`MissionPanel`, OCC commit) and blueprint attachment (`EntityBlueprints`) | P0 |
| <a id="uxr-21"></a>**UXR-21** | **No allocator internals in the author's face.** Blackboard tiers, slot bytes, `OverCeiling`, Reality/Staging are Advanced-only, and the default path picks a working allocation | Attach a behavior without reading the words "tier" or "bytes" | `EntityBlueprintsEditModel` surfaces `Projection(Slots, Bytes, Tier, Status)`, `UsageStatus.OverCeiling`, `UpgradeToTier`, Reality/Staging as the primary UI | P0 |
| <a id="uxr-22"></a>**UXR-22** | **The behavior list is correct** — it offers exactly the behaviors that can run on the selected unit, and always includes the author's own newly-created ones | Author a behavior, select an incompatible unit → it is absent or explained; select a compatible one → it is present | `GetAvailableBehaviors` = curated TKB list ∩ live registry, then **all** BrainTierBTree assets appended for **every** entity type, with `TODO (option c)` to gate by affinity (`EditorMissionService.cs:96`) | P0 |
| <a id="uxr-23"></a>**UXR-23** | **Behavior parameters are edited as typed fields, never as raw JSON**, and spatial parameters are picked on the map | Assign a behavior with a location and a target → both set by clicking the map | `BehaviorUiCompiler` auto-generates typed forms from `[BehaviorContract]` DTOs (good); anything unregistered falls to `DrawRawJsonEditor`. Map-pick special-cased for 3 behaviors only | P0 |
| <a id="uxr-24"></a>**UXR-24** | **Behavior params are stored structurally**, not as a JSON string nested inside the scenario JSON | Inspect a saved scenario → params are first-class JSON, diffable and machine-checkable | `behaviorParams` is an escaped JSON *string* inside `scenario.json` (see `scenarios/hill-attack/scenario.json`) | P1 |
| <a id="uxr-25"></a>**UXR-25** | **Assignment is discoverable from the object.** Right-clicking a unit offers Assign Behavior / Author New Behavior / Open Behavior | Right-click a unit → reach behavior authoring without knowing a panel name | ⚠ context menus exist (`JsonEntityContextMenuHandler`, `ContextMenuLogic`) — contents not yet traced | P0 |
| <a id="uxr-26"></a>**UXR-26** | Offline authoring does not pay distributed-protocol costs — no version-conflict dialogs when there is no cluster | Assign a behavior in `--mode editor` → no OCC/Force-Commit surface appears | `MissionPanel` runs OCC commit with conflict modal + Force Commit unconditionally | P1 |

## G3 — Run it

*"Press play."*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-30"></a>**UXR-30** | **One obvious Play control**, with Pause, Step and Stop, always in the same place | Find and press Play within 5 seconds of first launch | Transport exists in the status bar (`TimeControlStatusBarSection` → `ClusterTimeControlStatusBarSection`): play/pause, step, stop, sim time, rate | P1 |
| <a id="uxr-31"></a>**UXR-31** | **Play mode is unmistakable.** The editor's chrome makes running-vs-editing impossible to confuse | Glance at a screenshot → say whether it is running | Preview mode has no distinguishing chrome; "Preview" is also weaker language than "Play" | P0 |
| <a id="uxr-32"></a>**UXR-32** | **Stop restores the authored state exactly.** Nothing authored is lost by running | Place a unit, Play, watch it move, Stop → it is back where it was placed | ✅ **Correct already** — `EditorPreviewAdapter` snapshots the ECS on enter and rewinds on exit. Best bone in the shell; build on it | — |
| <a id="uxr-33"></a>**UXR-33** | **Edits during play are handled predictably** — either blocked with an explanation, or applied with a stated lifetime | Try to move a unit while running → the UI says what will happen to that change | ⚠ not traced (`ScenarioEditorState.OperatingPreview` gates authoring interactions; behaviour on edit unknown) | P1 |
| <a id="uxr-34"></a>**UXR-34** | **"Nothing is happening" is diagnosable.** When a unit has no behavior, an invalid plan, or a failed compile, the editor says so | Play a scenario with an unassigned unit → the editor tells you which unit and why | No problems panel; `NextError`/`PrevError` declared but never registered | P0 |

## G4 — Author a new behavior

*"Make a new behavior and use it."*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-40"></a>**UXR-40** | **One New Behavior entry point** that asks what the behavior should do, not which of three graph technologies to use | Create a working behavior without first knowing what a BTree, an HSM and a Blueprint are | Three separate asset kinds, three perspectives, three New… paths | P1 |
| <a id="uxr-41"></a>**UXR-41** | A newly-authored behavior **appears in the assignment list without a restart** | Author it, switch to the unit, assign it — in one session | ⚠ partially: BTree assets are appended to the list (interim, ungated); Blueprint path is separate. Re-derive before building | P0 |
| <a id="uxr-42"></a>**UXR-42** | **Every graph canvas has a compile/validate status pill** — green/yellow/red, always visible, click to see why | Break a graph → the pill goes red before you press Play | Inline compiler diagnostics exist in the blueprint canvas; no persistent status affordance | P1 |
| <a id="uxr-43"></a>**UXR-43** | **Getting from a unit to its behavior's graph, and back, is one gesture each way** | Select unit → open its behavior graph → return to the map, without the menu bar | ⚠ not traced; document/perspective switching exists (`AiDocumentManager`, `WindowManagerPerspectiveSwitcher`) | P1 |

## G5 — Debug & iterate

*"Watch it, find the bug, fix it live."*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-50"></a>**UXR-50** | **Debugging the selected unit's behavior is reachable from the unit** | Select a misbehaving unit → reach its live graph with the executing node highlighted, in ≤2 clicks | Live debug overlay, breakpoints, step, step-back and watches all exist and are strong; entry point from the *unit* is ⚠ not traced | P1 |
| <a id="uxr-51"></a>**UXR-51** | **Hot reload is a visible, trustworthy event.** The author is told it happened, whether it succeeded, and what state was preserved | Change a behavior, reload → a clear success/failure notice naming the classification | `AiHotReloadCoordinator` + Quick Reload exist with a Cosmetic/Soft/Hard classifier and a Hot Reload Log window; author-facing feedback ⚠ not traced | P1 |
| <a id="uxr-52"></a>**UXR-52** | **The iterate loop needs no restart.** Edit → reload → observe, repeatedly, in one session | Perform five edit-reload-observe cycles without relaunching | ⚠ believed working (this is HROT's strength) — confirm on the golden-path walk | P1 |
| <a id="uxr-53"></a>**UXR-53** | **Watch values are human-readable** | Watch a variable → see a typed value, not raw bytes | `WatchPanelWindow` renders raw hex bytes (per blueprint audit; ⚠ re-verify — may have shipped since) | P2 |

## G6 — Save, reload, run

*"Persist it and trust it."*

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-60"></a>**UXR-60** | **Save/Save As/Load are conventional** — Ctrl+S, a dirty marker, an overwrite confirm, and a save prompt on close | Ctrl+S saves; closing with unsaved work prompts | Commands exist (`ScenarioMenuCommands`); Save falls back to Save-As when unnamed. ⚠ shortcuts/prompt not traced | P1 |
| <a id="uxr-61"></a>**UXR-61** | **Round-trip fidelity is total.** Save → reload → the scenario is behaviorally identical: mission plans, attached blueprints, params, hierarchy, routes and zones all intact | Save, relaunch, load, Play → identical behavior to before the save | Translators exist per concern (`MissionPlanTranslator`, `BlueprintStateTranslator`, `UnitSubordinateTranslator`, …). **This is the user's specifically-named doubt — it gets an explicit regression test, not an assumption** | P0 🔴 |
| <a id="uxr-62"></a>**UXR-62** | **Load failures are legible and partial-safe** — a scenario that cannot fully load says exactly what was dropped | Load a scenario referencing a deleted behavior → named, actionable diagnostic | `_alertManager.OnScenarioLoaded(LastLoadResult)` exists; ⚠ surfaced detail not traced | P1 |
| <a id="uxr-63"></a>**UXR-63** | **The scenario file stays reviewable** — a human can read a diff and see what an author changed | Diff two saves after one behavior change → the diff is small and readable | Fights [UXR-24](#uxr-24): escaped JSON-in-JSON makes params diff as one opaque line | P2 |

## G7 — Runtime intervention (ExCon)

<a id="g7--runtime-intervention-excon"></a>

*"Run the authored scenario and interfere with it live."* **Path B — ordinary SME. Narrow surface,
strictest bar.** These requirements are met by presentation and defaults over the **same** panel code
Path A uses (see [Who we are building for](#who-we-are-building-for), consequence 4).

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-70"></a>**UXR-70** | **Add an entity to a running exercise** without engine vocabulary — pick what, click where, it exists | An SME with 5 minutes of instruction adds a unit mid-exercise, unaided | ⚠ path exists (`SpawnEntityCommand` via the ExCon/CGF route) — the *SME-facing* gesture is not traced | P0 |
| <a id="uxr-71"></a>**UXR-71** | **Assign or change a mission plan on a live entity** — pick the unit, pick what it should do, set params, confirm | Same SME retasks a unit mid-exercise, unaided | `MissionPanel` does this, but through OCC commit + version + Force Commit + raw-JSON params | P0 |
| <a id="uxr-72"></a>**UXR-72** | **"This is live" is unmistakable, and every live change is confirmed before it takes effect** | Glance at the screen → know changes affect a running exercise. Every commit is confirmed and acknowledged | ⚠ not traced. This is Path B's substitute for undo — nothing can be taken back once sent | P0 🔴 |
| <a id="uxr-73"></a>**UXR-73** | **Distributed-protocol mechanics are handled, never exposed.** Version conflicts are resolved or explained in plain language; the word "OCC" and a "Force Commit" button never reach an SME | Provoke a version conflict → the SME sees a plain-language message and a safe next step | `MissionPanel` surfaces a conflict modal and a **Force Commit** button unconditionally | P0 |
| <a id="uxr-74"></a>**UXR-74** | **The consequence of a live change is visible** — the SME sees the unit accept the new task, or sees it fail | Retask a unit → observe within seconds that it took effect | Task-state glyphs exist in `MissionPanel` (`GetTaskIcon`); ⚠ end-to-end legibility not traced | P1 |
| <a id="uxr-75"></a>**UXR-75** | **An SME cannot reach authoring-only or developer surfaces** from the ExCon console | Survey every reachable control in ExCon → none opens a graph canvas, allocator or cluster control | ⚠ not traced | P1 |

## G8 — Shared surfaces, per-mode difference

<a id="g8--shared-surfaces-per-mode-difference"></a>

*User statement, 2026-08-10.* **The same UI surfaces serve several ClusterRunner modes and must stay
mostly shared while letting each mode differ.** Stated concretely for the pair that matters most:

> *"SimHost and Editor share the map layer and many map tools. They are supposed to be **mostly shared
> but allowing for differences**. The Editor might need different tooling and a different entity context
> menu (although mostly shared), as it supports **scenario preparation** while SimHost supports a
> **running exercise**."*

**Why this is a requirement and not a preference:** the difference is a difference in *task*, not in
taste. Preparation edits a scenario that is not running; a running exercise cannot be edited the same
way. A surface that cannot express that difference will be forked — which is exactly what already
happened to ORBAT. See [the seam law](UX_Current_UI_Architecture.md).

⚠ **The main-menu, map-tool and context-menu surfaces are the ones that most define what a user can do**,
so they are the ones that most need to differ per mode (user, 2026-08-10).

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-80"></a>**UXR-80** | **A shared panel exposes a contribution seam.** A host adds, removes or replaces items without editing the panel | Add a mode-specific item to a shared surface with **zero** edits to shared code | Exists for entity context menus, map draw layers, the inspector, time transport; **absent** for the main menu, ORBAT rows, map camera, spawn | P0 |
| <a id="uxr-81"></a>**UXR-81** | **The map tool set is per mode.** The Editor's preparation tools and SimHost's exercise tools are drawn from one implementation pool, each host choosing its own set | Editor and SimHost show different tool sets over the same map code | 🔴 **No pool exists — "a tool" is not a thing in this codebase.** No `ITool`/registry/current-tool state; the active tool *is* whichever gizmo happens to be registered. **Four** uncoordinated activation idioms, two of them used for the same tools inside one class. SimHost has 3 tools (Select/Drag/Rotate); the rest are absent because nobody wrote the wiring, not because it opted out. The interaction core *is* shared 3 ways, so the gap is one level up. [Detail](UX_Current_UI_Architecture.md#6b-map-tools-and-the-editor--simhost-relationship) | P0 |
| <a id="uxr-82"></a>**UXR-82** | **The entity context menu is mostly shared, partly per mode** — common items declared once, mode-specific items contributed by the host | Compare Editor and SimHost menus: common items come from one place, differences from host registration | ⚠ **Half met.** The seam exists and is used (Editor 4 handlers, SimHost 1, IG 1) — but every item is a hand-written lambda, so *"Center on entity"* and *"Delete"* are **reimplemented three times with different behaviour** (Editor publishes `DestroyEntityCommand`; SimHost branches on `NetworkIdentity`, falls back to `_repo.DestroyEntity`, clears selection + inspector state). **Having a seam ≠ using it well** | P1 |
| <a id="uxr-85"></a>**UXR-85** | **The same entity offers the same actions on every surface.** What you can do to a unit is a property of the unit and the mode, not of which panel you happened to right-click in | Right-click one entity on the map, in the inspector and in ORBAT → the same action set, minus any a surface genuinely cannot offer | 🔴 **Three unrelated pipelines**: inspector = `IEntityContextMenuHandler` lambdas; map = `ContextMenuState.MenuJson` → gizmo popup; ORBAT = hardcoded at the call site. User requirement, 2026-08-10 | P0 |
| <a id="uxr-86"></a>**UXR-86** | **The action set varies by perspective** — cgf / editor / simhost / ig each present their own, from one shared pool | Switch perspective → the entity menu and main menu change accordingly | 🔴 **No menu consults perspective anywhere.** The toolbar has the filter; the menu registry does not. User requirement, 2026-08-10 | P0 |
| <a id="uxr-87"></a>**UXR-87** | **A remote party can define a mode's menu over the network.** IG stands in for a 3D IG whose context menu is configured remotely by ExCon | Change the menu definition on the ExCon side → IG's entity menu changes without a rebuild | ✅ works today via `ContextMenuState.MenuJson`; ⚠ **must survive any unification** — it is a requirement, not legacy (user, 2026-08-10) | P0 |
| <a id="uxr-84"></a>**UXR-84** | **A tool control reflects tool state.** The active tool is visible, and a control that does nothing does not exist | Activate each tool → the toolbar shows which is active; no button is inert | 🔴 **Not implementable today.** `EditorToolbarPanel` is stateless `ImGui.Button` calls and `IEditorLogic` exposes no current-tool property. `EditorTool.Select` is literally `case Select: break;` (`EditorSubsystem.cs:3814-3816`) — a dead control. Falls out of UXR-81's tool descriptor for free | P1 |
| <a id="uxr-83"></a>**UXR-83** | **Forking a shared panel is a last resort, and visible when it happens.** A mode that cannot express its difference through a seam is a defect in the seam | No shared UI role has two implementations | 🔴 ORBAT has three; spawn UI has four | P1 |

## Cross-cutting

<a id="cross-cutting"></a>

| ID | Requirement | Acceptance | Now | Pri |
|---|---|---|---|:--:|
| <a id="uxr-x1"></a>**UXR-X1** | **No dead controls.** Every control that renders either works or is visibly disabled with a stated reason. Enforced mechanically, not by review | A test asserts every registered command id has a handler and every menu path resolves; `default:` arms throw in debug builds | The blueprint programme's dominant defect shape — 13 of 14 panel commands once rendered, clicked, and did nothing | P0 🔴 |
| <a id="uxr-x2"></a>**UXR-X2** | **One problems panel.** Validation failures, compile diagnostics, load warnings and runtime faults land in one list, each clickable to its source | Break three different things → all three appear in one list; clicking each navigates to it | Absent | P0 |
| <a id="uxr-x3"></a>**UXR-X3** | **Every author gesture is acknowledged** — success confirmed, failure explained. No silent no-ops anywhere | Perform 20 gestures → 20 observable outcomes | Failure is routinely silent (`InvokeCreate` discards `EditorCommandResult`; `Invoke` returns an unread `"Unknown command"`) | P0 |
| <a id="uxr-x4"></a>**UXR-X4** | **Keyboard conventions match the industry** — Ctrl+S/Z/Y/C/V/D, Del, F2, and Q/W/E/R tool modes; discoverable in tooltips | Tooltip on any tool shows its shortcut; the shortcut works | Toolbar has no shortcuts, no tooltips, no active-mode highlight (`EditorToolbarPanel`, 50 lines) | P1 |
| <a id="uxr-x5"></a>**UXR-X5** | **Empty states teach.** Every panel with nothing in it says what to do next | Fresh scenario → each visible panel's empty state names the next action | Partially good already: `"No entity selected. Select an entity on the map to edit its blueprints."` is the right shape — generalise it | P2 |
| <a id="uxr-x6"></a>**UXR-X6** | **The golden path is documented as a walkthrough** an author can follow unaided, and it stays true | A newcomer completes it from the doc alone, with no verbal help | Absent. README §11.4 describes capabilities, not a path — and claims "ORBAT drag-and-drop unit hierarchy", which is the ExCon panel, not the editor's | P1 |

---

## Non-goals

Explicitly **out of scope** for this programme, to keep it finishable:

1. **A general undo/redo stack for scenario authoring.** Ruled out on architectural grounds — see the
   [rationale note](#uxr-17) above. [UXR-15](#uxr-15) (recoverability) and [UXR-17](#uxr-17)
   (single-step) are what we build instead.
2. **A fourth graph language**, or unifying BTree/HSM/Blueprint *implementations*. [UXR-40](#uxr-40) is
   about one *entry point*, not one engine.
3. **Reworking the distributed protocol.** [UXR-73](#uxr-73) asks that OCC be *handled and hidden* from
   an SME — not that it be removed. [UXR-26](#uxr-26) likewise asks only that offline authoring not
   *show* it.
4. **Moving the map into an ImGui window.** No render-to-texture, no image blit, no ImGui-hosted canvas.
   The map is Raylib for speed and stays that way; the new shell is designed around that
   ([UXD-30](UX_Design.md#uxd-30)).
5. **Forking shared panels per audience.** The two paths differ by presentation and defaults, not by
   duplicated panels ([consequence 4](#who-we-are-building-for)).
6. **Runtime/compiler capability.** New node kinds, scheduler work, EQS, waves — all belong to the
   blueprint programme's register.
7. **Rendering/terrain quality**, map projection, and 3D presentation.
8. **Multi-user concurrent authoring.**
9. **Localisation** and accessibility beyond keyboard-reachability.

## Answered questions

<a id="answered-questions"></a>

**All four opening questions answered by the user 2026-08-06.** Recorded here because they changed the
requirements above, not merely the design. Also tracked in [UX_RESUME.md](UX_RESUME.md#open-questions).

| # | Question | Answer | Effect on this doc |
|---|---|---|---|
| **OQ-1** | Who is the author? Does blueprint authoring belong on the golden path? | **Engineers and advanced military SME.** Focus on the editor. Blueprint/BTree authoring **stays on the path** — no designer-mode hiding | New [audience section](#who-we-are-building-for); [UXR-40](#uxr-40) is one entry point, not a simplification |
| **OQ-2** | Editor-only, or must the path hold in the distributed ExCon/CGF surface? | **Both, as two distinct paths.** Authoring = editor. ExCon = running an already-authored scenario and interfering at runtime (add entities, assign mission plans live) — **and that must be usable by ordinary SME** | Added **[G7](#g7--runtime-intervention-excon)**; refined [UXR-26](#uxr-26) |
| **OQ-3** | Scenario undo — full model, or cheap safety first? | **Cheap first.** Rationale from the user: the same editor code is reused in the simulation runtime, where real undo is not feasible anyway | [UXR-15](#uxr-15)/[UXR-17](#uxr-17) rewritten; general undo became [non-goal 1](#non-goals) |
| **OQ-4** | Entity templates — new asset kind, or scenario-embedded? | **Interesting and wanted.** User's lean: build on **what the scenario format already saves**, which may make it relatively easy | [UXR-16](#uxr-16) `Now` column notes the format is already template-shaped; representation is [UXD-04](UX_Design.md#uxd-04), in Q25 |

**No open questions block the requirements.** Remaining decisions are design-level and go to the
architect round — see [Architect_Question_25_Scenario_Authoring_Golden_Path.md](Architect_Question_25_Scenario_Authoring_Golden_Path.md).
