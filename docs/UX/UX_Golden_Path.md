# The Golden Path — executable specification

> **Status: v0.3, 2026-08-06. LIVING DOCUMENT — expected to change.**
> This is the programme's **specification**: the journey is the requirement, panels are implementation
> detail. It will be revised as the walk digs deeper and the requirements adjust. **Revise it rather
> than working around it** — a step that turns out to be wrong is a finding, not an obstacle.
>
> Requirements: [UX_Requirements.md](UX_Requirements.md) · Design: [UX_Design.md](UX_Design.md) ·
> Tasks: [UX_Task_Tracker.md](UX_Task_Tracker.md) · Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

## What this doc is for

Three things, and it must keep being good at all of them:

1. **The acceptance test for the whole programme.** When both paths walk clean, the programme is done.
2. **The build order.** ⭐ Since the editor is getting a **new shell** ([UXD-08](UX_Design.md#uxd-08)),
   step *N* of this document is what ships next: the new shell starts near-empty and **each step earns
   its surface**. Nothing enters the shell without a step that requires it.
3. **The source of the task register.** Every deviation observed on a walk becomes a `UXT-nn`.
   [UX_Task_Tracker.md](UX_Task_Tracker.md) is deliberately empty until the first walk fills it —
   *the audit says what is broken in the code; the walk says what stops a person, and only the second
   is a task list.*

> ### ⭐ The first walk is reconnaissance, not a repair list
>
> The editor's shell is being **replaced**, not repaired ([UXD-08](UX_Design.md#uxd-08)) — so a walk of
> the *current* app would otherwise produce a defect list for a shell we are about to discard.
>
> **Walk it anyway, once, reframed.** It is the only way to turn this document's ~20 predictions into
> facts about what the existing panels *actually do* — and the new shell has to compose those same
> panels ([UXD-09](UX_Design.md#uxd-09)). So the walk's output is:
>
> - a **capability inventory** — which panel really answers which step, and how well;
> - **corrected predictions** — logged in [Corrections](UX_Tasks_Detail.md#corrections);
> - **the build order** — per step, what the new shell must compose, and where a view-model seam is
>   missing (that last one is where estimates will be wrong);
> - **only then** a defect list, for behaviour that is wrong *inside* a panel rather than in the shell.
>
> Record a shell-level annoyance as *"the new shell must not reproduce this"*, not as a bug to fix in
> the old one. **Do not spend effort fixing the old shell.**
>
> 🔒 **And note what the build order may do at each step.** Under
> [UXD-10b](UX_Design.md#uxd-10b) the rule is **place, do not edit**: a step that can be satisfied by
> *placing* an existing window into the layout ships early; a step needing a change *inside* a window or
> to a **shared menu** waits for a consult via [SHARED_SURFACES.md](SHARED_SURFACES.md), because the
> blueprint programme is developing against those same panels in parallel. So the capability-inventory
> column *"logic reachable without its ImGui?"* decides not just cost but **ordering**.

## The two paths

Per [Who we are building for](UX_Requirements.md#who-we-are-building-for):

| | Walked by | Surface | Steps |
|---|---|---|---|
| **Path A — Authoring** | An engineer or advanced military SME, new to HROT | editor (`--mode editor`, offline) | [A1–A12](#path-a--authoring) |
| **Path B — Runtime intervention** | An **ordinary SME**, ~5 minutes of instruction | distributed **ExCon** against a running exercise | [B1–B5](#path-b--runtime-intervention) |

Path B is shorter and has the **stricter** bar. Walk A first — B depends on a scenario A produced.

## The two acceptance principles

Applied to **every** step, on top of each step's own criterion
([UX_Requirements.md](UX_Requirements.md#the-two-acceptance-principles)):

- **A1 — Reachability.** ≤2 clicks from the state the previous step left the editor in, **zero
  window-opening detours**. Opening a window to continue **is** the defect.
- **A2 — Legibility of outcome.** The UI states what happened. Silence is a defect.

## How to walk it

**This session (Linux, coordinator) cannot run the editor** — it is a Windows/Raylib ImGui app. The
walk is performed by a **Windows implementation session or the user**. See
[Briefing §5.10](UX_Programme_Briefing.md#510-session-topology).

1. `run_Editor.bat` (or `Hrot.ClusterRunner.exe --mode editor`). **Delete or rename the layout state
   first** — a walk against a hand-tuned layout proves nothing about a new user's experience.

   > 🔴 **Corrected 2026-08-10 — there are two files, and neither is the repo-root `imgui.ini`.** That
   > one is committed and never read by the app; deleting it does nothing and silently invalidates the
   > walk. Delete both of:
   >
   > - **`%LocalAppData%\HROT\imgui.ini`** — the ImGui docking layout. Path is hardcoded in
   >   `RaylibPresentationShell.SetupImGui()` with no override seam.
   > - **`fdp_windows.json`**, next to the executable — window open/pinned state, active perspective and
   >   UI scale (`WindowManager.cs:437-438`).
2. Walk the steps **in order**, without using knowledge not present in the UI. When you must consult
   the codebase or ask someone to continue, **that is a deviation** — record it and then continue with
   whatever knowledge you need.
3. For every step record: **clicks used**, **windows opened**, **what the UI said**, **what actually
   happened**, verdict `PASS` / `FAIL` / `BLOCKED`.
4. **Also record, for each step: which panel(s) actually served it, and whether their logic is reachable
   without their ImGui** (a `Handle*` method, an edit model, a view-model) — this is what the new shell
   will compose, and a missing seam is the finding that matters most.
5. Take a screenshot at every `FAIL`. Attach to the deviation row.
6. Fill in the [deviation log](#deviation-log) as you go, then cut `UXT` entries from it.

**Do not fix anything during the walk** — and in particular **do not fix the old shell**, which is being
replaced. A walk that stops to fix things produces neither a clean result nor a complete list.

### 🗺 How the map works — read before walking A3–A7

The **2D symbolic map is Raylib, drawn across the whole OS window, behind ImGui** (for speed). ImGui runs
a `PassthruCentralNode` dockspace, so the transparent central node is where you see the map; ImGui windows
dock along the **screen edges**. The **Scenario** perspective shows the map; the BTree/HSM/Blueprint
perspectives do not — their central window is the graph.

⇒ **The map's visible rectangle is not the window.** When walking any map gesture, record **where on
screen the result landed**, not just whether it happened — that distinction is the whole of
[UXR-18](UX_Requirements.md#uxr-18).

**Specific check, worth doing early:** dock panels left and right, select a unit, invoke *centre on
entity*. **Prediction: the entity lands at or near the window's top-left corner, hidden under a docked
panel** — because the editor never sets `MapCamera.Offset` and the ctor leaves it `Vector2.Zero`. If that
is what you see, [UXR-18](UX_Requirements.md#uxr-18) is confirmed 🔴 and
[UXD-30](UX_Design.md#uxd-30) is the fix.

### Prediction columns are predictions

Each step carries a **Prediction** — a code-derived guess at what will happen, from the opening audit.
They are **unverified against a running editor** and the sibling blueprint audit was wrong ten times.
Where the walk contradicts a prediction, the walk wins: record it in
[Corrections](UX_Tasks_Detail.md#corrections).

---

# Path A — Authoring

<a id="path-a--authoring"></a>

## A1 — Launch and begin

| | |
|---|---|
| **Intent** | Get from a cold start to "I am authoring a scenario" |
| **Gesture** | Launch the editor. Begin a new scenario |
| **Required outcome** | A start surface offers New / Open Recent / Open. Choosing New yields a scenario that is *runnable immediately* — terrain resolved, camera framed, and either a starter unit or an explicit "place your first unit" prompt |
| **Requirements** | [UXR-01](UX_Requirements.md#uxr-01), [UXR-02](UX_Requirements.md#uxr-02), [UXR-04](UX_Requirements.md#uxr-04) |
| **Prediction** | 🔴 No start screen. `New Scenario` lives under `File → Scenario`, and `EditorApplication.NewScenario` clears the world and nulls the name — a void. Terrain/camera state after the clear is **unknown and must be observed** |

## A2 — Know where you are

| | |
|---|---|
| **Intent** | At any moment, know what is loaded and whether it is saved |
| **Gesture** | Look at the screen. Do not open a menu |
| **Required outcome** | Scenario name and dirty marker are visible in a fixed place |
| **Requirements** | [UXR-03](UX_Requirements.md#uxr-03) |
| **Prediction** | 🔴 Name appears only inside the `Workspace` dynamic submenu (`WorkspaceMenuBuilder.cs:112`). Whether the OS window title carries it is **unverified — check it** |

## A3 — Place an entity

| | |
|---|---|
| **Intent** | Put a specific kind of unit at a specific place |
| **Gesture** | Choose what to place from a browsable catalog → click the map |
| **Required outcome** | The catalog is browsable by name (not by type id). After the click the unit exists, is selected, and the UI says so |
| **Requirements** | [UXR-12](UX_Requirements.md#uxr-12), [UXR-X3](UX_Requirements.md#uxr-x3), [UXR-X4](UX_Requirements.md#uxr-x4) |
| **Prediction** | `Place Entity` in the toolbar activates a placement gizmo, but `EditorSpawnAdapter.StartPlacementMode` takes a `tkbType` and `LastSelectedTkbType` **defaults to `Tank_M1Abrams`** — so whether a *catalog picker* is reachable at all is the key unknown of this step. No tool-state highlight, no shortcut, no tooltip |

## A4 — See what is in the world

| | |
|---|---|
| **Intent** | Read the world's contents; select things by name |
| **Gesture** | Look at the outliner. Click a unit there → it selects on the map. Click a unit on the map → it highlights in the outliner |
| **Required outcome** | Every entity by **name and type**, in ORBAT hierarchy. Selection agrees in all three of outliner / map / inspector |
| **Requirements** | [UXR-10](UX_Requirements.md#uxr-10), [UXR-11](UX_Requirements.md#uxr-11) |
| **Prediction** | 🔴 **The single worst step.** `EditorOrbatPanel.DrawContent` is 27 lines printing `• [entityId]` — no names, no hierarchy, no selection, no interaction. Also check *whether it is even in the default layout* |
| **Also record** | 🗺 When selecting from the map, note **where in the visible map area** the selected unit sits, and whether docked panels cover part of the map. Feeds [UXR-18](UX_Requirements.md#uxr-18) |

## A5 — Inspect and adjust

| | |
|---|---|
| **Intent** | Understand the selected unit and change something about it |
| **Gesture** | With a unit selected, read its identity/position/state; rename it; move it; then press Ctrl+Z |
| **Required outcome** | One panel answers "what is this?". Rename via F2. Ctrl+Z reverts the last gesture. Nothing is destroyed without confirmation |
| **Requirements** | [UXR-13](UX_Requirements.md#uxr-13), [UXR-14](UX_Requirements.md#uxr-14), [UXR-15](UX_Requirements.md#uxr-15), [UXR-17](UX_Requirements.md#uxr-17) |
| **Prediction** | 🔴 Inspection is split across ≥4 panels. **Ctrl+Z does nothing — there is no scenario-side undo at all.** Whether *delete* confirms is unverified — check it, it is the data-loss case |

## A6 — Assign a behavior

| | |
|---|---|
| **Intent** | Tell the unit what to do |
| **Gesture** | From the selected unit, assign a mission plan: add a task, choose a behavior, set its parameters (including picking a location on the map), set the completion trigger |
| **Required outcome** | Reached **from the unit** (right-click or an inspector section), not by hunting a panel. Params are typed fields; spatial params are map-picked. Only behaviors that can run on this unit are offered. The assignment is acknowledged |
| **Requirements** | [UXR-20](UX_Requirements.md#uxr-20)…[UXR-23](UX_Requirements.md#uxr-23), [UXR-25](UX_Requirements.md#uxr-25), [UXR-26](UX_Requirements.md#uxr-26) |
| **Prediction** | `MissionPanel` does all of this mechanically — task add/delete/reorder, behavior combo, triggers, OCC commit — but: params fall back to a **raw JSON textbox** unless the behavior has a `[BehaviorContract]` DTO; map-pick is special-cased to 3 behaviors; the list appends **all** BTree assets for every entity type; and an **OCC/Force-Commit surface appears even offline**. Reachability from the unit is the key unknown |

## A7 — Run it

| | |
|---|---|
| **Intent** | See the authored scenario execute |
| **Gesture** | Press Play. Watch. Pause. Step. Stop |
| **Required outcome** | Play is findable in seconds. Running-vs-editing is unmistakable. Stop restores the authored state **exactly** |
| **Requirements** | [UXR-30](UX_Requirements.md#uxr-30)…[UXR-32](UX_Requirements.md#uxr-32) |
| **Prediction** | Transport exists in the **status bar** (play/pause, step, stop, sim time, rate) — findability is the unknown. ✅ `EditorPreviewAdapter` snapshots on enter and rewinds on exit, so A7→A8 should be safe. 🔴 No play-mode chrome |

## A8 — Diagnose "nothing is happening"

| | |
|---|---|
| **Intent** | The unit does not do what you expected. Find out why |
| **Gesture** | Look for what the editor is telling you |
| **Required outcome** | One problems list naming the unit and the reason (no behavior, invalid params, failed compile, unmet trigger), each entry clickable to its source |
| **Requirements** | [UXR-34](UX_Requirements.md#uxr-34), [UXR-X2](UX_Requirements.md#uxr-x2) |
| **Prediction** | 🔴 Nothing. No problems panel; `NextError`/`PrevError` are declared and never registered. **This step is why an author gives up** — expect it to be the highest-value finding of the walk |

## A9 — Author a new behavior

| | |
|---|---|
| **Intent** | The stock behaviors are not enough. Make one |
| **Gesture** | Create a new behavior, author something simple that visibly does something, save it |
| **Required outcome** | One New Behavior entry point. A persistent validate/compile status. Getting back to the scenario is one gesture |
| **Requirements** | [UXR-40](UX_Requirements.md#uxr-40), [UXR-42](UX_Requirements.md#uxr-42), [UXR-43](UX_Requirements.md#uxr-43) |
| **Prediction** | Three separate kinds (BTree / HSM / Blueprint), three New… paths, three perspectives. **Inside** a canvas the experience is good — 17 batches of work. Expect the friction at the boundaries: getting in, knowing which kind to pick, getting out |

## A10 — Assign the new behavior and run it

| | |
|---|---|
| **Intent** | Close the authoring loop |
| **Gesture** | Return to the map, select the unit, assign the behavior you just made, Play |
| **Required outcome** | It appears in the list **without a restart**, with typed params, and it runs |
| **Requirements** | [UXR-41](UX_Requirements.md#uxr-41), [UXR-22](UX_Requirements.md#uxr-22) |
| **Prediction** | ⚠ **The most important unknown in the whole walk.** BTree assets are appended to the list by an explicitly interim path (`EditorMissionService.AppendEditorBTreeBehaviors`, `TODO (option c)`); the Blueprint attachment route is entirely separate (`EntityBlueprints`, with tiers/slots/bytes). Whether a newly-authored behavior of *each* kind reaches a unit must be established by observation, per kind |

## A11 — Debug, hot-reload, iterate

| | |
|---|---|
| **Intent** | It runs but it is wrong. Fix it without restarting |
| **Gesture** | From the misbehaving unit, open its behavior with live execution visible. Set a breakpoint. Inspect values. Change the behavior. Reload. Observe. Repeat five times |
| **Required outcome** | Debugging reachable **from the unit**. Hot reload is a visible, trustworthy event stating success/failure and what state survived. Five cycles, no restart |
| **Requirements** | [UXR-50](UX_Requirements.md#uxr-50)…[UXR-53](UX_Requirements.md#uxr-53) |
| **Prediction** | HROT's strongest area — live overlay, breakpoints, step, step-back, watches, Quick Reload with a Cosmetic/Soft/Hard classifier and a Hot Reload Log. Expect two gaps: entry **from the unit**, and whether the reload outcome is stated where the author is looking. Watch values may still render as raw hex (⚠ may have shipped since — re-verify) |

## A12 — Save, reload, run

| | |
|---|---|
| **Intent** | Trust the file |
| **Gesture** | Ctrl+S. Quit. Relaunch. Load the scenario. Play |
| **Required outcome** | Ctrl+S saves with a name prompt on first save. After reload the scenario is **behaviorally identical** — mission plans, attached behaviors, params, hierarchy, routes, zones. Play produces the same behavior as before the save |
| **Requirements** | [UXR-60](UX_Requirements.md#uxr-60)…[UXR-62](UX_Requirements.md#uxr-62) |
| **Prediction** | Per-concern translators exist (`MissionPlanTranslator`, `BlueprintStateTranslator`, `UnitSubordinateTranslator`, …), so the mechanism is there. **The user specifically doubts this step** ("see the behaviours stay assigned to entity") — it gets an explicit regression test, not an assumption. Verify **per behavior kind**: mission-plan behaviors and attached blueprints persist by *different* translators |

---

# Path B — Runtime intervention

<a id="path-b--runtime-intervention"></a>

> **Walked by an ordinary SME with ~5 minutes of instruction, against a running exercise, using the
> scenario Path A produced.** Narrow surface, strictest bar. Nothing here may require engine
> vocabulary or knowledge of panel sequencing. Nothing here can be undone — so everything is
> confirmed. See [G7](UX_Requirements.md#g7--runtime-intervention-excon).

## B1 — Know that this is live

| | |
|---|---|
| **Intent** | Never confuse a running exercise with an editable draft |
| **Gesture** | Look at the console |
| **Required outcome** | "This affects a running exercise" is unmistakable at a glance |
| **Requirements** | [UXR-72](UX_Requirements.md#uxr-72) |
| **Prediction** | ⚠ Not traced. Establish what an ExCon operator actually sees |

## B2 — Find a unit and read its orders

| | |
|---|---|
| **Intent** | Locate a unit and see what it is currently doing |
| **Gesture** | Find the unit (map or ORBAT), read its current mission plan and which task is active |
| **Required outcome** | Current orders and active task are legible without interpretation |
| **Requirements** | [UXR-74](UX_Requirements.md#uxr-74) |
| **Prediction** | ExCon's `OrbatPanel` (434 lines) is the richer one and does have hierarchy; `MissionPanel` shows task state glyphs (`GetTaskIcon`) and the active task id. Plausibly the closest-to-working step on either path |

## B3 — Retask it live

| | |
|---|---|
| **Intent** | Change what a unit is doing, mid-exercise |
| **Gesture** | Choose a new behavior, set its params (map-pick where spatial), confirm |
| **Required outcome** | Typed params, map-pick, an explicit confirm, then an acknowledgement. **No JSON. No "version". No "Force Commit."** Failure is explained in plain language with a safe next step |
| **Requirements** | [UXR-71](UX_Requirements.md#uxr-71), [UXR-73](UX_Requirements.md#uxr-73) |
| **Prediction** | 🔴 The machinery is right and the presentation is wrong: `CommitMissionAsync` with `baseVersion`, an `ERR_VERSION_CONFLICT` modal, and a **Force Commit** button — all reachable by an SME. Raw-JSON param fallback applies here too |

## B4 — Add a unit live

| | |
|---|---|
| **Intent** | Inject a new entity into the running exercise |
| **Gesture** | Choose what to add → click where → confirm |
| **Required outcome** | Same catalog legibility as [A3](#a3--place-an-entity), plus a confirm, plus an acknowledgement that it exists in the exercise |
| **Requirements** | [UXR-70](UX_Requirements.md#uxr-70) |
| **Prediction** | ⚠ The spawn route exists (`SpawnEntityCommand` → CGF). The SME-facing gesture is untraced |

## B5 — Verify the effect

| | |
|---|---|
| **Intent** | Confirm the intervention did what was intended |
| **Gesture** | Watch the unit |
| **Required outcome** | Within seconds, visible evidence that the change took effect — or a plain-language statement that it did not |
| **Requirements** | [UXR-74](UX_Requirements.md#uxr-74) |
| **Prediction** | ⚠ Not traced |

---

## Deviation log

<a id="deviation-log"></a>

**Fill one row per deviation. Then cut `UXT` entries from it.** A step can produce several rows; a row
can serve several steps.

| # | Step | What I did | What I expected | What happened | Clicks / windows opened | Verdict | → `UXT` |
|---|---|---|---|---|---|---|---|
| | | *no walk performed yet* | | | | | |

### Capability inventory

The reconnaissance half of the walk ([above](#what-this-doc-is-for)). One row per step: what already
serves it, and whether the new shell can compose that logic without its current window.

| Step | Panel(s) that actually served it | Logic reachable headlessly? | What the new shell composes |
|---|---|---|---|
| | *no walk performed yet* | | |

### Walk record

| Walk | Date | Walker | Environment | Path | Result |
|---|---|---|---|---|---|
| — | — | — | — | — | *not yet walked* |

## Revision log

| Rev | Date | Change |
|---|---|---|
| v0.3 | 2026-08-06 | 🗺 Added the **map architecture** section — the map is a full-OS-window **Raylib** layer behind ImGui, visible only through the `PassthruCentralNode` central node, absent from the graph perspectives. Walkers must record *where on screen* a map gesture landed, and there is a specific early check for the centre-on-entity prediction ([UXR-18](UX_Requirements.md#uxr-18)) |
| v0.2 | 2026-08-06 | ⭐ **The editor is getting a new shell** ([UXD-08](UX_Design.md#uxd-08)), so this doc gains a third job: it is now the **build order** — the new shell starts near-empty and each step earns its surface. The first walk is reframed as **reconnaissance** (capability inventory + corrected predictions + build order), not a repair list for a shell being discarded. Added the capability-inventory table and a walk step for recording whether each panel's logic is reachable without its ImGui |
| v0.1 | 2026-08-06 | Created. Path A (A1–A12) and Path B (B1–B5) from the user's stated journey, with code-derived predictions from the opening audit. **No walk performed** — every prediction is unverified |
