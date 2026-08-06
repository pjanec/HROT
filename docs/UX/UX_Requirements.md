# Scenario-Authoring UX — Requirements (`UXR`)

> **Status:** v1 baseline, 2026-08-06. **Source of truth for scope.**
> A task that does not trace to a `UXR-nn` does not belong in this programme.
> Design: [UX_Design.md](UX_Design.md) · Tasks: [UX_Task_Tracker.md](UX_Task_Tracker.md) ·
> Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

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
| <a id="uxr-15"></a>**UXR-15** | **Undo/redo covers scenario authoring** — placement, drag, rotate, delete, component edit, behavior assignment | Place a unit, drag it, assign a behavior, press Ctrl+Z three times → all three reverted | **No undo exists on the scenario side.** Zero `Undo` references in `Hrot.Editor` / `Hrot.Presentation`. Undo exists only inside graph editors | P0 🔴 |
| <a id="uxr-16"></a>**UXR-16** | **Reusable entity templates** — save a configured unit (or group) as a template, place instances, override per instance | Configure a tank, save as template, place 3, change one without affecting the others | No scenario-level template/prefab concept. TKB types are the only reuse unit; a scenario entity is a verbatim ~20-component bag | P1 |
| <a id="uxr-17"></a>**UXR-17** | **Destructive actions are recoverable or confirmed** — and the scenario autosaves | Delete a platoon, then recover it (undo) or be warned before it happens | ⚠ not traced; no undo backstop exists | P1 🔴 |

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

1. **A fourth graph language**, or unifying BTree/HSM/Blueprint *implementations*. [UXR-40](#uxr-40) is
   about one *entry point*, not one engine.
2. **Reworking the distributed protocol.** [UXR-26](#uxr-26) asks that offline authoring not *show*
   OCC surfaces — not that OCC be removed.
3. **Runtime/compiler capability.** New node kinds, scheduler work, EQS, waves — all belong to the
   blueprint programme's register.
4. **Rendering/terrain quality**, map projection, and 3D presentation.
5. **Multi-user concurrent authoring.**
6. **Localisation** and accessibility beyond keyboard-reachability.

## Open questions blocking requirements

Answers change the shape of the requirements above. Recorded here, tracked in
[UX_RESUME.md](UX_RESUME.md#open-questions).

| # | Question | Blocks |
|---|---|---|
| OQ-1 | Who is the "ordinary author" — a military SME with no programming background, or an engineer? Does blueprint authoring belong on the golden path at all, or behind a *designer* mode? | G4 entirely; [UXR-40](#uxr-40), [UXR-21](#uxr-21) |
| OQ-2 | Is the golden path **editor-only** (`--mode editor`, offline), or must it hold in the distributed ExCon/CGF path too? | [UXR-26](#uxr-26), [UXR-20](#uxr-20) |
| OQ-3 | Scenario undo: pay for a command-based mutation model, or buy safety cheaply first (confirm-destructive + autosave + revert-to-saved)? | [UXR-15](#uxr-15), [UXR-17](#uxr-17) — the largest single item in the programme |
| OQ-4 | Entity templates: a new asset kind, or scenario-embedded? | [UXR-16](#uxr-16) |
