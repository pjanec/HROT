# UX Programme Briefing — read this first

> **Every handoff doc in [handoffs/](handoffs/) links here.** If you are an implementation session
> that has just been handed a task, read this page, then your handoff doc, then the `UXT-nn` entry in
> [UX_Tasks_Detail.md](UX_Tasks_Detail.md). Nothing else is required reading.

---

## 1. What this programme is

HROT is a distributed military simulation platform with a mature runtime and, inside the graph
editors, a genuinely strong debugging story (conditional/data breakpoints, step-back, in-memory hot
reload with embedded PDBs, jump-to-C#-source). It was built **bottom-up**, and it shows: the
infrastructure is solid, the front door is not.

**Two audiences, two surfaces, two different bars** — settled with the user 2026-08-06. Full statement:
[Who we are building for](UX_Requirements.md#who-we-are-building-for).

| | **Path A — Authoring** | **Path B — Runtime intervention** |
|---|---|---|
| Surface | the editor (`--mode editor`, offline) | distributed **ExCon**, live exercise |
| Audience | **engineers / advanced military SME** | **ordinary SME people** |
| Job | build a scenario; author and debug behaviors | run an authored scenario and interfere live — add entities, retask units |
| Bar | learnable; no tribal knowledge needed | **walk-up usable**; no engine vocabulary |

Path A may assume competence — but *knowing which window to open* is clairvoyance, not competence, and
stays a defect. Path B has the **higher** bar over a **smaller** surface, and both are served by the
**same shared panels** — differences come from presentation and defaults, never from forked panels.

**The single sentence that defines success:** both audiences walk their path end to end without being
told which window to open.

### The golden path

```
Path A:  new scenario → place entity → assign behavior (mission plan w/ tasks) → run it
           → author a new behavior (BTree / Blueprint) → run it → debug it → hot-reload it
           → iterate until working → save scenario → reload it → run → behaviors still attached

Path B:  know it is live → find a unit → retask it / add a unit → see the effect
```

**The step-by-step specification is [UX_Golden_Path.md](UX_Golden_Path.md)** — the programme's
acceptance test and the source of its task register. Every requirement in
[UX_Requirements.md](UX_Requirements.md) exists to make one step of it walkable. **If a change does not
make a step easier, safer, or more discoverable, it is out of scope.**

## 2. Why the work is where it is

The blueprint programme (`docs/blueprints/`) spent 17 batches on the **inner loop** — the graph
canvas. Copy/paste, align/distribute, minimap, one undo stack, item CRUD, graph switching, function
returns. Inside a blueprint, HROT is now decent.

Nobody has worked on the **outer loop** — the scenario shell. The audit that opened this programme
found, verified against code:

- the scene outliner (`EditorOrbatPanel`) is **27 lines** that print `• [entityId]`;
- there is **no undo and no autosave anywhere** on the scenario side (place, drag, mission edit are all
  one-way, with no net under them);
- the toolbar is **6 unlabeled text buttons** with no active-mode feedback and no shortcuts;
- `New Scenario` produces a void with no next-step affordance;
- behavior assignment has **two unrelated mental models**, one of which exposes blackboard tiers,
  slot bytes and `OverCeiling` to the author;
- there is **no problems/error-list panel** — the editor's default answer to "why isn't my unit
  moving?" is silence.

**The corollary that shapes every design choice here:** with no usable outliner, *choosing a window
becomes the interaction model*. That is the root cause of "requires lots of knowledge of what panel to
open in what sequence" — not a documentation gap.

> ⚠ **CORRECTED 2026-08-10.** This paragraph used to add *"and no right-click affordances"*. **That was
> false** — a repo scan found ~26 production context-menu sites, including 5 handlers registered by the
> Editor. It held only for the 27-line `EditorOrbatPanel`. The affordances exist and are **attached to
> the wrong surfaces**; treat this as placement/discoverability, not absence. Full evidence:
> [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md). Do not reintroduce the old wording.

## 3. The frame we design against

An editor feels approachable when it answers five questions **continuously, without the user opening
anything**:

| # | Question | Answered by |
|---|---|---|
| 1 | Where am I? What am I editing? | Title/breadcrumb, dirty marker, unmistakable edit-vs-play chrome |
| 2 | What is in my world? | The outliner — **the spine of the whole editor** |
| 3 | What is this thing? | One inspector, always in the same place |
| 4 | What can I do right now? | Contextual toolbar + right-click on the object + searchable palette |
| 5 | Did it work? If not, why? | Problems panel, status pill, toasts, validation |

Use this frame when scoping a task: name which question the task improves. A task that improves none
of them is probably infrastructure, not UX.

## 4. Engine patterns we are copying (and two we are not)

Proven over 15+ years in Unity / Unreal / Godot. Prefer these over invention:

- **Fixed default layout, not discoverable windows.** Nobody opens a window to start working.
- **The object is the menu.** Right-click an entity → *Assign Behavior… / Author New Behavior… /
  Open Behavior / Save as Template*. This is what kills the sequencing problem.
- **Prefab + instance override.** The biggest productivity lever in scenario authoring.
- **The Compile pill.** Unreal's persistent green/yellow/red status + a message log that jumps to the
  offending node. Converts silence into a state you can trust.
- **Loud play-mode chrome.** Unity tints the whole editor in play mode. HROT already has correct
  snapshot/rewind semantics — it just never says so visually.
- **Console with clickable origin.** One list where validation failures, commit conflicts and
  compiler diagnostics all land, each clickable to its source.
- **Content templates.** `New Scenario` offers terrain + a unit + a camera, not a void.
- **Command palette.** Cheap, and the escape hatch that makes a dozens-of-windows editor survivable
  while the layout is being fixed.

**Not copying:** Unreal's Blueprint-for-everything sprawl (HROT already has three graph languages —
resist a fourth), and Unity's historical modal-wizard-per-asset-type (one New… dialog with a kind
picker is better).

## 5. Work habits — non-negotiable

### 5.1 Model delegation (token thrift)

Keep the strong model for orchestration, design, and hard review. **Delegate to a Sonnet subagent**
anything that does not need top-tier intelligence:

- mirror-an-existing-pattern slices (a second panel shaped like the first);
- mechanical edits (renames, adding tooltips/icons across call sites, test scaffolding);
- broad searches and evidence-gathering sweeps.

Do **hands-on** (no delegation): novel design, anything touching the ECS mutation/undo model, the
compiler/scheduler, and the final diff review. **The orchestrating session reviews the real diff and
re-runs the gates itself** — never accept a subagent's "all green" without seeing the output.

### 5.2 The architect gate

No non-trivial capability starts without a design, and no non-trivial design ships without an
**architect pass**. The "architect" is the user's NotebookLM system holding the engine design docs —
**Claude cannot reach it; the user relays.**

For each non-trivial task: draft `docs/UX/Architect_Question_NN_*.md` mirroring the existing
`docs/blueprints/Architect_Question_*` docs — decision-shaped sub-questions A/B/C/D, Claude's
recommended lean per sub-question, and the reuse-vs-build tradeoff. The user runs it past the
architect, the answers are recorded in that doc, **then** build.

Numbering continues the global architect sequence (the blueprint programme reached **Q24**), so UX
questions start at **Q25**. They live in `docs/UX/` rather than `docs/blueprints/`.

Trivial mirror-pattern work with a documented recipe may proceed on a short in-repo design note.

### 5.3 Verify before you build

**The blueprint audit register was wrong ten times.** Never build against a claim in a doc — including
these docs — without re-deriving it from code first. If the claim is wrong, fix the doc in the same
commit.

### 5.4 Assert the effect, never the report

`BlueprintCommandSink.Apply`'s `default:` arm returns **success** for commands it has no case for.
This shape — a feature fully built, fully wired, silently doing nothing while reporting that it
worked — accounts for three shipped blueprint defects. In this programme:

- never assert on `Success`; assert the observable effect;
- **new rule for this programme: no dead controls.** A control that renders must either work or be
  visibly disabled with a reason. See [UXR-X1](UX_Requirements.md#cross-cutting).

### 5.5 Revert to watch it go red

After fixing something, **revert the fix and confirm the new tests fail.** This is a required step,
not an optional one. In the blueprint programme a test written for the fix *passed against the bug*,
and only the revert caught it.

### 5.6 Build general, not minimal

When a task needs a generic mechanism, implement the whole obvious set rather than the one case in
front of you — and add closely-similar companions that reuse the same machinery. Default toward
completeness. Balance against speculation: if a round-out means a whole new *speculative* vocabulary,
or contradicts an architect ruling, flag it for a nod first.

### 5.7 Documentation style

- **Short.** Lead with visuals and terse tables. Long prose walls go unread.
- **Diagrams: hand-authored SVG** for anything non-trivial. Mermaid only for simple flowcharts, with
  short box labels (it clips text).
- Deep-link everything. Every tracker row links to its detail entry; every task links to its
  requirement.

### 5.8 Interaction style

- Ask questions in **plain chat prose**. Do **not** use the multiple-choice question widget.
- Report outcomes faithfully. If a gate is red, say so with the output. If a step was skipped, say so.

### 5.9 Two programmes, one repo — place, do not edit 🔒

**The blueprint programme is actively developed in parallel sessions, and `ClusterRunner` must stay
fully operational.** That is a hard constraint on how this programme works, not a preference:

| | |
|---|---|
| ✅ **Always allowed** | Add files/projects/windows/registrations/layout. **Place and dock** existing windows. Read shared view-models through seams that already exist |
| ⚠ **Consult first** | Any change to a co-owned window's **internals**; adding a seam to a shared panel; changing a **shared menu**'s structure or an existing command's behaviour |
| ⛔ **Never** | Fork a shared panel; change `ClusterRunner`'s behaviour for the editor's convenience; break the **construction kit** (the distributed `--mode` variants must keep working) |

**Mechanism: [SHARED_SURFACES.md](SHARED_SURFACES.md)** — the co-ownership list and the consult log. Add a
row there **before** touching a co-owned surface.

**Why git does not catch this:** a change that alters a shared panel's *behaviour* can invalidate a
blueprint session's visual verification mid-flight. Branches detect textual collisions, not semantic ones.

**The upside to protect:** the new editor shell is a **greenfield project** — new files, new `.csproj` —
so it is collision-free by construction. When a golden-path step can be satisfied by *placing* a window
rather than editing one, place it.

🔴 **The blueprint/BTree/HSM editor windows are the parallel programme's active surface. Place them; do
not touch them.**

### 5.10 Session topology

| Role | Where | Does |
|---|---|---|
| **Coordinator** | one Linux cloud session | Requirements, design, architect questions, task cutting, handoff authoring, diff review, doc upkeep. **Cannot run the editor** |
| **Implementers** | Windows local sessions, usually | Build, run the editor, walk the golden path, verify visually, report back |

**Consequences that bite if forgotten:**

- **The coordinator must never claim a visual verification it could not perform.** Anything the
  coordinator concludes about *running* behaviour is a code-derived **prediction** and must be labelled
  as such. The editor is a Windows/Raylib ImGui app (`run_Editor.bat`).
- **Handoffs are the interface.** An implementation session gets this briefing + its handoff + the task
  entry, and nothing else. If a handoff needed knowledge it did not carry, that is a coordinator defect
  — the implementer should say so in the report.
- **Findings flow back into the docs, not just into chat.** The implementer updates the task's `DONE`
  note, the tracker row and the RESUME in the same commit as the work.
- **Predictions get corrected.** Where a walk contradicts a prediction, the walk wins: record it in
  [UX_Tasks_Detail.md](UX_Tasks_Detail.md#corrections).

### 5.11 Visual verification is mandatory

This is a **UX** programme: a green test suite proves nothing about whether the thing feels usable.
Every task ends with the implementer actually performing the designer's gesture in the running editor
and recording what they saw in the task's `DONE` note. The blueprint programme's batch-7 visual pass
found one unreachable button and one long-standing 🔴 that ~2800 tests had missed.

## 6. Inherited traps

The nine traps earned by the blueprint programme apply here too. The four most relevant:

| # | Trap |
|---|---|
| 5 | **`default:` returns success.** A command the sink silently accepts and ignores. Bitten four times. |
| 6 | **Asset-scoped features belong at the host**, not inside a vendored command — the single opaque command hides the ids a caller needs for an inverse. |
| 8 | **An optional ctor dependency defaulting to an inert value.** Tests pass it explicitly and prove the logic; every production site omits it; the feature is silently dead. Three confirmed instances. **Grep the production construction sites.** |
| 9 | **A test can be locked on both halves of a contract and the feature still be unusable**, because no test performs the designer's gesture. Hence §5.5 and §5.11. |

Full list and history: [`docs/blueprints/Blueprint_Gaps_Programme_RESUME.md`](../blueprints/Blueprint_Gaps_Programme_RESUME.md).

## 7. Codebase orientation

**Use the Codebase Memory MCP graph tools first** — `list_projects` → `get_architecture` →
`search_graph` / `trace_call_path` / `get_code_snippet`. Only fall back to `read_file` when you need
exact raw content to edit a line. On a fresh cloud VM the graph is not persisted: if `list_projects`
returns empty, call `index_repository` immediately without asking. If the tools are absent entirely,
run the `/cloud-bootstrap` skill (tools connect on the *next* session).

Key surfaces for this programme:

| Surface | Path |
|---|---|
| Editor shell / composition root | `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (~4.2k lines) |
| Scenario commands (New/Load/Save) | `Hrot/Subsystems/Hrot.Editor/ScenarioMenuCommands.cs` |
| Outliner (stub) | `Hrot/Subsystems/Hrot.Editor/UI/EditorOrbatPanel.cs` |
| Toolbar | `Hrot/Subsystems/Hrot.Editor/UI/EditorToolbarPanel.cs` |
| Entity placement | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorSpawnAdapter.cs` |
| Play/preview (snapshot + rewind) | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorPreviewAdapter.cs` |
| Mission/behavior assignment UI | `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` |
| Auto-generated param forms | `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiCompiler.cs` |
| Behavior availability | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMissionService.cs` |
| Blueprint attachment (tiers/slots) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/` |
| Window/perspective manager | `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` |
| Scenario persistence translators | `Hrot/Subsystems/Hrot.SimHost/Serializers/` |

Run the editor: `Hrot.ClusterRunner.exe --mode editor` (or `run_Editor.bat` / `run_Editor.sh`).
