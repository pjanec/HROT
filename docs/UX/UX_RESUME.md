# RESUME / HANDOFF — Scenario-Authoring UX programme

> **rev 10 · 2026-08-10 · branch `claude/ux-session-resume-i2le7f`**
>
> ⚠ **The branch changed.** This session was started on `claude/ux-session-resume-i2le7f`, which was
> fast-forwarded from `claude/reset-working-branch-qd1qpv` (tip `ab2c91a`) — same history, new name.
> The old branch is still on origin and is **not** being force-moved. Registry:
> [SESSION_SYNC.md](../SESSION_SYNC.md#the-sessions).
>
> 📌 **This is the entry document for this programme.** A fresh session — or one that has lost its
> context to compaction — reads §0 and §1, then §3 for the single next action. Nothing else is required.
> If this file and [UX_Task_Tracker.md](UX_Task_Tracker.md) disagree about status, **the tracker wins**.
>
> **This session is the COORDINATOR** (Linux cloud). Implementation and testing happen in **separate
> Windows sessions**. The coordinator cannot run the editor — see [§1.10](#110-session-topology).
>
> 🔀 **A parallel session is porting the MCP server** ([mcp-port/MCP_PORT_RESUME.md](../mcp-port/MCP_PORT_RESUME.md)).
> You share one file with it and exchange updates both ways — **read
> [SESSION_SYNC.md](../SESSION_SYNC.md) before starting work**, and see [§1.11](#111-the-parallel-mcp-session).

## Starting a fresh session? Paste this

```
Read docs/UX/UX_RESUME.md and continue the scenario-authoring UX programme.
Branch: claude/ux-session-resume-i2le7f
First: git fetch origin, then follow docs/SESSION_SYNC.md — merge the MCP session's
branch if it has moved. Then §3 "Next up".
```

---

## 0. Why this programme exists — never forget this

HROT's authoring **infrastructure works**. The authoring **experience does not**. The user's own words:

> *"all that exists but it is not user friendly at all, not straightforward, not bulletproof, not
> intuitive, requires lots of knowledge of what imgui panel/window to open, in what sequence to use it.
> It is working to a large extent infrastructure-wise, but is practically unusable because of very bad
> UX. HROT was built bottom-up, this might need an inverted approach."*

**The goal:** both audiences walk their path end to end without being told which window to open.

```
Path A — authoring (editor; engineers / advanced military SME)
  new scenario → place entity → assign behavior (mission plan w/ tasks) → run it
    → author a new behavior (BTree / Blueprint) → run it → debug it → hot-reload it
    → iterate until working → save scenario → reload it → run → behaviors still attached

Path B — runtime intervention (distributed ExCon; ORDINARY SME, strictest bar)
  know it is live → find a unit → retask it / add a unit → see the effect
```

**Two audiences, settled 2026-08-06.** Authoring is for engineers and advanced SME — competence may be
assumed. **ExCon runtime intervention must be usable by ordinary SME people**: a narrower surface with a
*higher* bar. Both run on the **same shared panels**; differences are presentation and defaults, never
forked panels. Full statement: [UX_Requirements.md](UX_Requirements.md#who-we-are-building-for).

> ### ⚠ CORRECTED 2026-08-10 — the old "core insight" was refuted by code
>
> This file used to say: *"with no outliner and no right-click affordances on objects, choosing a
> window becomes the interaction model — that is the root cause."* **The second half is false.**
> A repo-wide scan found **~26 production context-menu sites**; the Editor alone registers **5**
> `IEntityContextMenuHandler`s (Center / Rename / Edit Shape / Edit Route / Rotate / Delete / Mark
> Target / AI-trace / Inspect…) plus map menus that vary by entity state, and the graph canvases have
> the richest context-menu system in the repo.
>
> **True only of `EditorOrbatPanel`** — the 27-line stub the claim was generalised from.
>
> ⇒ **Restated:** the affordances *exist and are attached to the wrong surfaces*. That is a
> **discoverability and placement** problem, not an absent-feature problem — and it is far cheaper than
> what this document previously assumed. Do not re-derive the old version.

**The core insight that replaced it — [the seam law](UX_Current_UI_Architecture.md):**

> **Every UI surface that exposes a contribution seam is shared successfully across modes.
> Every surface that does not has been forked.** Five scans, no counter-example.

⇒ The question is never *"share or duplicate?"* but *"does this surface have a seam?"* `SharedOrbatPanel`
has one hardcoded item and no extension point — so ExCon forked a **434-line** replacement. Over-sharing
without a seam is what *causes* the duplication.

### ⭐ And the cause behind that cause

**The editor's UI was never designed.** It is produced by `LocalWindowController.OpenLocalWindow()` —
~60 lines in the *cluster host* that loop over subsystems asking each to dump its windows into one
manager, then pick the default perspective as literally `_subsystems.Skip(1).FirstOrDefault()?.Name`.
Perspectives are hardcoded cluster roles; the window is titled "HROT Cluster Runner"; `ScanForSubsystems`
builds a DDS participant for *every* subsystem before filtering to the requested ones.

**The bag-of-windows is not a defect in the editor — it is the correct output of a generic cluster-node
window aggregator.**

**User ruling 2026-08-06 ([UXD-08](UX_Design.md#uxd-08)):** the editor becomes its **own application
with a purpose-built shell** — *fully-fledged feature-wise, with a very much shared init path so all the
internal machinery still runs*, and the UI grown **step by step** by composing what mostly exists.

Consequences that must not be lost:

- **[UX_Golden_Path.md](UX_Golden_Path.md) is now the build order**, not only the acceptance test. The
  shell starts near-empty; each step earns its surface.
- **The first walk is reconnaissance**, not a repair list — we are replacing the shell, so do **not**
  spend effort fixing the old one.
- ⚠ **"Standalone" ≠ "no cluster machinery".** Scenario load publishes a `TransitionStateIntent` and
  waits for `ClusterState.Idle`; Play/Stop goes through `PreviewClusterOpHandler`.
  ⚡ **CORRECTED 2026-08-10 — but none of that comes from the host.** It lives inside `Hrot.Editor`,
  which builds its **own in-process `ClusterMaster`** on its own bus (`EditorSubsystem.cs:1352`,
  `Mandatory = Array.Empty<string>()`) and ticks it per frame; the intents are published from
  `EditorApplication.cs`. `Program.cs` contributes only **generic hosting** — logging, CLI, the
  orchestrator driver, window, render loop. So the machinery is real and must keep running, but
  **it is not an argument for sharing init** — the editor already carries it.
- The host is only **2,217 lines** and **no subsystem project depends on it** (only `InternalsVisibleTo`
  test attributes), so this is cheap. The seam is [Q25-F](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell).

### 🔒 Three hard constraints on the new app (user, 2026-08-06)

**These are not preferences. An answer that violates one is wrong regardless of its other merits.**

1. **`ClusterRunner` stays fully operational, continuously** — blueprint development runs against it in
   **parallel sessions**. No "will fix after the refactor" states.
2. **The construction kit survives** — the system must keep composing network-distributed variants exactly
   as today (`--mode orchestrator,simhost,cgf`, `ig`, `excon`, `all`). **The editor app is one preset of
   the kit, not a replacement for it.**
3. **Place, do not edit.** The new UI must be careful about changing window *content* — prefer **placing**
   existing windows into the designed layout. Any in-window or **shared-menu** change must be
   **synchronised/consulted** with the parallel sessions via
   **[SHARED_SURFACES.md](SHARED_SURFACES.md)** ([UXD-10b](UX_Design.md#uxd-10b)).

⚠ **Constraint 3 invalidated one of Claude's earlier leans.** Q25-F-iii originally recommended *re-host
the view-models* as the rule; that means touching panel internals. **Revised: layout first** — H0 for
everything layout can satisfy, view-model reuse only where the seam already exists, section-extraction
deferred. [UXR-14](UX_Requirements.md#uxr-14) (one inspector) and
[UXR-20](UX_Requirements.md#uxr-20) (one behaviors section) are therefore **re-sequenced behind the
consult protocol** — they are the first things to do once in-window change is affordable, not the first
things overall.

**The upside that makes this workable:** the new shell is a **greenfield project** — new files, new
`.csproj` — so it is **collision-free by construction**. This is an additional argument for the new app
over repairing the old shell in place, which would have collided continuously.

### 🗺 The map — architecture, and a 🔴 defect it implies

**User, 2026-08-06, confirmed in code:** the 2D symbolic map is **Raylib, rendered across the whole OS
window, behind ImGui** — kept that way **for speed**. ImGui runs a `PassthruCentralNode` dockspace
(`Program.cs:347-349`) so the central node is transparent and the map shows through; **ImGui windows dock
along the screen edges**; the map is visible only where they are not. The BTree/HSM/Blueprint perspectives
show **no map** — their central window is the graph. 🔒 **Moving the map into ImGui is a
[non-goal](UX_Requirements.md#non-goals)** — the new shell is designed around this.

⇒ **The map's screen extent and its visible extent are two different rectangles**, and everything that
reasons about "where the map is" — centre-on-entity, frame-all, fit-selection, zoom-to-cursor, hit-testing,
gizmo placement — must use the **effective viewport**. New [UXR-18](UX_Requirements.md#uxr-18) 🔴 +
[UXD-30](UX_Design.md#uxd-30) + [Q25-F-vi](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-vi--the-effective-map-viewport).

**The good news: the mechanism already exists and the fix is one assignment.** `MapCamera.Offset` is the
screen point that `Camera.Target` maps to (`MapCamera.cs:23-33`) — set it to the centre of the effective
viewport and `FocusOn()` becomes correct, with **zero** rendering change and no perf cost.

🔴 **The bad news, and a prediction to confirm on the walk:** the editor **never sets `Offset`**, and the
ctor leaves it `Vector2.Zero` (`MapCamera.cs:62`) — so *centre on entity* should be putting the entity at
the window's **top-left, under a docked panel**. Other hosts use full-window or **hardcoded** centres
(`IgApplication.cs:617`; `CgfSubsystem.cs:577` and `SimHostVisualization.cs:226` both hardcode 1280×720).
**Nothing anywhere is occlusion-aware.** ⚠ Code-derived — the coordinator cannot run the editor.

⚠ **Correction recorded:** an earlier revision of the target layout in
[UX_Design §2](UX_Design.md#2-the-five-questions--the-target-layout) drew the map as a **docked centre
panel**. That was wrong and is fixed. Do not reintroduce it.

⚠ **Also corrected:** there is **no "Home" perspective**. The id is `"Editor"` and it is **already
relabelled "Scenario"** in the Perspective menu (`EditorSubsystem.cs:3449`,
`RegisterPerspectiveLabel("Editor", "Scenario")`). The *id* is load-bearing — `CurrentPerspective ==
"Editor"` gates `isScenarioContext` (`:2592`, `:2598`) and every window registration uses it as
`owningPerspective` — so renaming the id is a wide mechanical change in a co-owned file, and the display
name is already right.

### The editor's architectural identity — verified

**Networkless, all-in-one, in-process by design, and already enforced in code:**

```csharp
// EditorSubsystem.cs:180
private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();
// EditorSubsystem.cs:557
public EditorSubsystem( INetworkFactory _ )      // ← the injected factory is DISCARDED
```

So the DDS participant `Program.cs:194` creates for the editor is **built and thrown away**; the editor
preset can drop network composition entirely. ⚠ But note `EditorSubsystem( INetworkFactory _ )` is a
**dependency that looks injected and is not** — do not let a future session "helpfully" wire it and give
the editor a network.

⚡ **Sharpened 2026-08-10** — `_networkFactory` is declared at `:180` and **never read**. The class is
`sealed` and not `partial` (`:165`), so that is the whole story: the editor consumes **no**
`INetworkFactory`, and `:180` is a **dead field** rather than a used offline default. ⇒ the seam question
in [Q25-F-i](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-i--where-does-the-seam-between-shared-init-and-new-shell-go)
is answered: shared init keeps **zero** network composition for the editor preset.

⚠ **"Networkless" means no DDS / no cluster transport — not "no sockets".** The MCP port (below) gives
the editor a **loopback `HttpListener`**. Do not architect the shell so that hosting it later reopens it.

⚠ **CORRECTED 2026-08-06 — the MCP server exists, on a stranded branch.** An earlier note in this file
said it "does not exist yet"; that was true of *our line* only. It was **developed on
`origin/feat/ai-debug-api`** (tip `d7b2a6e1`) in **34 commits over two days, 2026-06-14 → 15** — 16
batches, **49 MCP tools**, ~3.2k lines of production C#. **Recent and compact work.** It is a loopback
HTTP control plane
(`DebugApiHost`, `HttpListener` on `http://localhost:{port}/`) inside `Hrot.Editor`, plus an external
Node MCP server that proxies it. **The user requires it merged and kept operational as infrastructure.**

🔴 **It cannot be merged — `feat/ai-debug-api` has NO common ancestor with `main` or our branch.** That
branch carries the *original* project history (2137 commits, roots to 2025-12-30) while the trunk was
re-created around 2026-07-16 (120 commits, 3 roots). ⚠ **2137 is the old history, NOT the size of the MCP
work** — git merely sees all of it as foreign and would try to reconcile the whole disjoint history
instead of the 34 commits anyone wants. **The topology is the obstacle; the work is small.** Port it
forward as files, never merge. Full
description, inventory and plan: **[MCP_PORT_PLAN.md](MCP_PORT_PLAN.md)**.

**Why this programme cares:** the API is a **headless harness for most of Path A's mechanics** (partly
lifting the coordinator's can't-run-the-editor limit), an independent inventory of editor capability, and
— most usefully — `DebugApiService.cs` (2140 lines) has *already answered* much of the
"is this logic reachable without ImGui?" question that [UXD-09](UX_Design.md#uxd-09) needs. Groups H/M
(`checkpoint`, `restore_checkpoint`, `diff_state`, `focus_entity`) also feed
[Q25-A](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-a--how-do-we-spend-a-cheap-recoverability-budget).
⚠ All of that is claimed by the branch's own docs and **not verified against its code**.

**The inversion, concretely:** the golden path is the specification. Panels are implementation detail.
When a design question arises, the tiebreaker is *"which answer makes the author's walkthrough
shorter"* — never *"which fits the current window layout"*.

### The one-line summary of where the work is

The blueprint programme (`docs/blueprints/`, 17 batches, ~76 issues) fixed the **inner loop** — editing
inside a graph canvas. It is largely done and it succeeded. **Nobody has touched the outer loop** — the
scenario shell. That is this programme.

---

## 1. Way of working — the standing agreement

**Full detail: [UX_Programme_Briefing.md](UX_Programme_Briefing.md). That doc is linked from every
handoff and is the canonical statement.** Condensed here so a compacted session recovers immediately:

1. **No ad-hoc work.** Requirement → design decision → task → handoff → visual verification →
   tracker update. A change with no `UXR` behind it does not ship.
2. **Architect gate.** Non-trivial capability needs an `Architect_Question_NN` doc (A/B/C/D
   decision-shaped sub-questions + Claude's lean + reuse-vs-build tradeoff) relayed by the user to
   their NotebookLM architect. **Claude cannot reach the architect.** UX questions start at **Q25**
   (the blueprint programme reached Q24) and live in `docs/UX/`.
3. 🔒 **Place, do not edit.** Two programmes share this repo and the blueprint one is **active**.
   Additive at the shell boundary; in-window and shared-menu changes go through
   [SHARED_SURFACES.md](SHARED_SURFACES.md) first. `ClusterRunner` must stay operational and the
   construction kit must survive.
4. **Delegate to Sonnet** for mirror-a-pattern slices, mechanical edits and broad searches. Keep the
   strong model for design, the ECS/undo model, and the final diff review. **The orchestrating session
   reviews the real diff and re-runs the gates itself** — never trust a subagent's "all green".
5. **Verify before building.** The blueprint audit register was wrong ten times. Re-derive every claim
   from code — including claims in *these* docs — and fix the doc in the same commit if it was wrong.
6. **Assert the effect, never the report.** `default:`-returns-success has silently killed four
   shipped features in this codebase.
7. **Revert to watch it go red.** Required, not optional. A test written for a fix once passed against
   the bug.
8. **Visual verification is mandatory.** This is a UX programme — a green suite proves nothing about
   whether the thing feels usable. Perform the designer's gesture in the running editor
   (`--mode editor`) and record what you saw in the task's `DONE` note.
9. **Docs stay short.** Terse tables, hand-authored SVG for non-trivial diagrams, deep-link everything.
10. **Ask questions in plain chat prose** — never the multiple-choice widget.

### 1.10 Session topology

<a id="110-session-topology"></a>

| Role | Where | Does |
|---|---|---|
| **Coordinator** (this session) | Linux cloud | requirements, design, architect questions, task cutting, handoffs, diff review, doc upkeep |
| **Implementers** | Windows local sessions | build, run the editor, walk the path, verify visually, report back |

**The coordinator cannot run the editor** — it is a Windows/Raylib ImGui app (`run_Editor.bat`).
Therefore **every coordinator statement about running behaviour is a code-derived prediction and must
be labelled as one.** The golden-path predictions in [UX_Golden_Path.md](UX_Golden_Path.md) are exactly
that. Where a walk contradicts a prediction, **the walk wins** — record it in
[UX_Tasks_Detail.md](UX_Tasks_Detail.md#corrections).

### 1.11 The parallel MCP session

<a id="111-the-parallel-mcp-session"></a>

**Protocol: [SESSION_SYNC.md](../SESSION_SYNC.md) — canonical, owned by neither side. Read it before
starting work and before pushing.** Condensed:

| | |
|---|---|
| **Its entry doc** | [mcp-port/MCP_PORT_RESUME.md](../mcp-port/MCP_PORT_RESUME.md) |
| **Its branch** | ⚠ **TBD — the user will supply it.** Record it in [SESSION_SYNC.md](../SESSION_SYNC.md#the-sessions) when you learn it |
| **What it is doing** | Porting the AI Debug API + MCP server forward from `feat/ai-debug-api` — see [MCP_PORT_PLAN.md](MCP_PORT_PLAN.md) |
| **The one shared file** | 🔴 **`EditorSubsystem.cs`.** It adds ~10 lines of `DebugApiHost` wiring; we eventually add the new shell's composition |

> ### 🔴 Sequencing rule — treat `EditorSubsystem.cs` as read-only until the port lands
>
> The MCP port's wiring is small, known and already designed. It goes in **first**. Doing it after our
> shell work means resolving it against a moving target and wiring it twice. **Until the port has landed,
> the UX programme does not touch that file.**

**On every session start:** `git fetch origin`, check whether the MCP branch moved, and merge it *before*
doing your own work. The two branches share history (both descend from `main`), so it is an ordinary
merge — unlike `feat/ai-debug-api`, which shares no ancestor with anything.

**On a conflict in `EditorSubsystem.cs`: keep both additions.** Two features are being wired into one
composition root; if the merge looks like a choice between them, you have misread it.

**After you push:** say in your final message that the MCP session should pull. Claude cannot notify it.

**Why this programme cares about the outcome** (detail in
[MCP_PORT_PLAN.md](MCP_PORT_PLAN.md#why-the-ux-programme-cares)): the API is a headless harness for most
of Path A's mechanics — partly lifting the coordinator's cannot-run-the-editor limit — and
`DebugApiService.cs` has *already answered* much of the "is this logic reachable without ImGui?" question
that [UXD-09](UX_Design.md#uxd-09) needs.

Handoffs are the interface: an implementer receives the
[Briefing](UX_Programme_Briefing.md) + its handoff + the task entry, and nothing else. If a handoff
needed knowledge it did not carry, that is a coordinator defect.

---

## 2. Status

**Programme opened 2026-08-06. Nothing implemented yet — by design.**

| Artefact | State |
|---|---|
| [UX_Requirements.md](UX_Requirements.md) | ✅ **v2** — 46 requirements (`UXR-01`…`UXR-X6`), 7 groups + cross-cutting. Two-audience section added; `G7` added for Path B; `UXR-15`/`17` rewritten to the cheap-recoverability ruling; all 4 opening questions **answered** |
| [UX_Golden_Path.md](UX_Golden_Path.md) | ✅ **v0.2 — the specification *and* the build order.** Path A (A1–A12) + Path B (B1–B5), each step with intent / gesture / required outcome / requirement links / **code-derived prediction**. First walk reframed as **reconnaissance**; capability-inventory table added. **Living document — revise it as we dig** |
| [Architect_Question_25_…](Architect_Question_25_Scenario_Authoring_Golden_Path.md) | ✅ **drafted, awaiting the architect.** **Six** decision-shaped questions (A–**F**) + 7 sub-questions, each with options, reuse-vs-build tradeoff and Claude's lean. **Q25-F (new editor app / shell seam) is flagged to be answered FIRST** — it reframes D. **Answers table empty** |
| [UX_Design.md](UX_Design.md) | ▣ base, v0.3 — thesis, two-paths constraint, "a new shell not a repaired one", five-questions frame, target layout, **20** decisions. `UXD-02` and `UXD-08` are `RULED`; six are routed into Q25. Sequencing gains a Milestone 0 (stand up the shell) |
| [UX_Tasks_Detail.md](UX_Tasks_Detail.md) | ▣ base — template, rules, complexity scale, baseline evidence index. **Register empty** |
| [UX_Task_Tracker.md](UX_Task_Tracker.md) | ▣ base — 6 milestones, all empty |
| [SHARED_SURFACES.md](SHARED_SURFACES.md) | ✅ **new** — co-ownership list (9 shared surfaces), consult-before-touch rule, consult log, and the re-sequencing this constraint forces. ⚠ **The blueprint programme's RESUME does not link to it yet** (that edit is itself a co-owned change) |
| [handoffs/](handoffs/) | ▣ template only |
| **Golden-path walk** | ☐ **not performed** — needs a Windows session. Every prediction is unverified |
| **Milestone 0 pre-seam check** | ✅ **done 2026-08-10** (Linux, code only) — both questions answered; found one 🔴. See [above](#added-2026-08-10--the-pre-seam-check-is-done) |
| ⭐ [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md) | ✅ **new 2026-08-10 — read this before any UI work.** Five-scan assessment of what is shared across the 5 modes, what was forked, and why. Establishes [the seam law](#0-why-this-programme-exists--never-forget-this), the full seam inventory, the duplication/rigidity registers, ~1.8k lines of dead UI, and the 3-tier gap plan. **Re-sequences the programme** — see §3 |

**The opening audit is done** and its findings are recorded in two places: the `Now` column of each
requirement, and the [baseline evidence index](UX_Tasks_Detail.md#baseline-evidence-index). The
headline verified findings:

- outliner is a **27-line stub** printing `• [entityId]`;
- **no undo and no autosave on the scenario side at all** 🔴;
- toolbar is 6 text buttons, no state / shortcuts / tooltips, mixing authoring with dev plumbing;
- `New Scenario` yields a void;
- **two unrelated behavior-assignment models**, one exposing blackboard tiers/bytes/`OverCeiling`;
- behavior params fall back to raw JSON, stored as escaped JSON-in-JSON;
- **no problems panel** — the editor's answer to "why isn't my unit moving?" is silence;
- **no role/mode/expert-mode concept exists anywhere** — relevant to how Path A and Path B differ;
- ⚡ **the sharpest finding, made while drafting Q25:** the behavior-affinity mechanism *already
  exists* — `BehaviorContractAttribute(name, BehaviorCategory)` → `BehaviorCatalog.GetValidBehaviors(tkbType)`
  → the mission list, and `BehaviorUiCompiler.Compile<TDto>()` → the typed param form. But
  `BehaviorCatalog` reflects **one assembly in a static ctor**, so an **asset-authored** behavior can
  never declare affinity or params. That single cause explains the ungated
  `AppendEditorBTreeBehaviors` (`TODO (option c)`), the raw-JSON param fallback, **and** a latent
  staleness bug: a static reflection snapshot cannot see hot-reloaded behaviors. It is
  [Q25-C](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-c--where-does-an-asset-authored-behavior-declare-its-affinity-and-its-parameters);
- ✅ one genuinely good bone: `EditorPreviewAdapter` does correct ECS snapshot-on-play /
  rewind-on-stop. Build on it.

### Added 2026-08-10 — the pre-seam check is done

[§3.5](#next-up) flagged two things to *establish before cutting the seam*. Both are now answered from
code (no Windows needed), and the second turned up a defect. Full citations:
[baseline evidence index](UX_Tasks_Detail.md#baseline-evidence-index).

1. **Does `--mode editor` use the injected DDS factory or its own offline one? Neither.**
   `_networkFactory` is a **dead field** — declared at `EditorSubsystem.cs:180`, never read, in a
   `sealed` non-`partial` class. ⇒ shared init keeps **zero** network composition for the editor preset.
   The host still builds a participant for it, because `Program.cs:184-207` runs for *every* discovered
   subsystem **before** the requested-subsystem filter at `:213`.
2. 🔴 **Code assuming cluster-role perspectives: found, and it is already biting.** The shell validates a
   restored perspective id against **subsystem names** (`LocalWindowController.cs:83`), but
   `"BTree"`/`"HSM"`/`"Blueprint"` are perspective ids registered by `EditorSubsystem`. They fail the
   check and are **silently discarded**; only `"Editor"` survives, and only because
   `EditorSubsystem.Name => "Editor"`. **Predicted effect: quit inside a blueprint graph, relaunch, and
   you are in Scenario — with no message.** ⚠ code-derived; confirm on the walk.
   🔒 **Shell-level ⇒ do not repair `LocalWindowController`** — it is a *"the new shell must not
   reproduce this"* entry ([§3.3](#next-up)), and it is now evidence for
   [Q25-F-ii](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-ii-perspective-restore) `G2`.

---

## 3. Next up

<a id="next-up"></a>

> ### 🔒 RULED 2026-08-10 — NO new editor executable
>
> *"This all strengthens my guess that we should not start building a new editor exe but rather cleaning
> up inside the existing."* — the user. **[UXD-08](UX_Design.md#uxd-08) is `WITHDRAWN`; Milestone 0 is
> closed, not deferred; Q25-F/F′ are moot and must not be relayed.**
>
> ⇒ **The plan is now [UX_Cleanup_Path.md](UX_Cleanup_Path.md)** — six ordered stages, with the
> structural choices put to the architect as
> **[Q26](Architect_Question_26_Entity_Action_Model.md)**. Stage 0 (delete ~1,800 lines of dead UI,
> including the `Hrot.UI.Common` namespace trap) needs no architect round and should go first.
>
> **Three constraints the user added at the same time**, now [G8](UX_Requirements.md#g8--shared-surfaces-per-mode-difference):
> an entity offers the **same actions in an inspector as on the map** ([UXR-85](UX_Requirements.md#uxr-85));
> the action set **varies by perspective** ([UXR-86](UX_Requirements.md#uxr-86)); and IG's menu stays
> **configurable over the network** ([UXR-87](UX_Requirements.md#uxr-87)) — a requirement, not legacy.
>
> **Mode corrections from the user:** ExCon is **natively mapless by design**, and IG is *"a bit of a
> fake"* — a stand-in for a 3D IG showing entities replicated from **SimHost and CGF**. **The focus is
> the Editor plus SimHost and CGF**, which should share most capabilities and *inherit optionally only
> what is necessary*.

> ### 🔄 RE-SEQUENCED 2026-08-10 — superseded in part by the ruling above
>
> The user challenged the dedicated-exe plan (*"requires maintaining two test paths"*) and redirected to
> **understand the current UI architecture first**. That was done — [the assessment](UX_Current_UI_Architecture.md)
> — and it changed the order of work:
>
> | | Then | Now |
> |---|---|---|
> | **First** | Stand up a new editor exe (Milestone 0) | **Tier-1 seam work** — [§8 of the assessment](UX_Current_UI_Architecture.md#8-the-gap--what-it-takes) |
> | **The exe** | The plan | ⏸ **Deferred, possibly moot.** Every difference the requirement names — layout, menu, map layers, context menus — is a *seam problem inside shared code*, not a hosting problem |
> | **Q25** | Ready to relay | 🔒 **Do not relay yet** — F′ and D must absorb the seam findings first |
>
> **Why Tier 1 wins:** it is smaller than the exe, it benefits **all five modes** rather than one, and it
> needs **no second test path** — which was the user's objection. Afterwards the exe is a packaging
> decision, not an architectural one.
>
> 🔴 **Before any shared-panel work: delete `Hrot.UI.Common`.** It is in no `.csproj` and no `.sln`, yet
> the panels that *do* compile declare its namespace — so navigating by namespace lands in a file that
> builds into nothing, and the copies have already drifted. See [Trap U3](#5-traps).

### 0. Tier-1 seam work — the current recommendation

Each item mirrors a pattern that **already exists in this repo**, so all four are low-risk:

| Work | Pattern to copy | Fixes |
|---|---|---|
| Perspective filter on `GlobalMenuRegistry.RegisterItem` | `MainToolbarManager.RegisterEntry(…, perspective:)` — the toolbar already has it | per-mode main menus |
| Item-provider seam on `SharedOrbatPanel` | `IEntityContextMenuHandler` | lets ExCon's 434-line fork collapse back |
| One camera path reading the effective viewport | `MapCamera.Offset` already *is* the mechanism | 4 stale copies **and** the occlusion defect together |
| Delete `Hrot.UI.Common` + ~600 L of other dead UI | — | removes the namespace trap |

⚠ **Not yet cut into `UXT-nn` tasks** — needs the user's go-ahead on the re-sequencing first, since it
replaces Milestone 0 as the opening move.

### 1. User review — the earlier gate (superseded in part)

The user reads [UX_Golden_Path.md](UX_Golden_Path.md) and
[Q25](Architect_Question_25_Scenario_Authoring_Golden_Path.md). Expect the golden path to change — it is
a **living document** and the user asked for it to be adjusted as we dig. Revise it rather than working
around it.

### 2. Relay Q25 to the architect

The user relays; Claude cannot reach the architect. Answers go into
[Q25's answers table](Architect_Question_25_Scenario_Authoring_Golden_Path.md#answers), then the
matching [UXD rows](UX_Design.md#3-design-decisions-uxd) flip to `DECIDED`, then the affected milestones
unblock in [UX_Task_Tracker.md](UX_Task_Tracker.md).

### 3. Walk Path A as reconnaissance

**Needs a Windows session** ([§1.10](#110-session-topology)). Delete/rename the layout state first — a
walk against a hand-tuned layout proves nothing about a new user's experience. Walk A1–A12 in order and
fill both tables in [UX_Golden_Path.md](UX_Golden_Path.md#deviation-log):

> 🔴 **Delete the RIGHT files — corrected 2026-08-10.** There are **two**, and neither is the `imgui.ini`
> in the repo root (that one is committed, and the app never reads it — deleting it does nothing and
> invalidates the walk):
>
> | File | Path | Overridable? |
> |---|---|---|
> | ImGui docking layout | `%LocalAppData%\HROT\imgui.ini` | ❌ hardcoded in `RaylibPresentationShell.SetupImGui()` — no parameter, no DI seam |
> | Window/perspective state | `fdp_windows.json` **next to the exe** | ✅ `Save/LoadSettings(path)` — but `LocalWindowController.cs:75,94` always call it with no argument |
>
> ⇒ 🔴 **This also blocks [UXR-04](UX_Requirements.md#uxr-04)** ("delete the layout profile, launch, walk
> the path"). `imgui.ini` is keyed to `%LocalAppData%`, **not** to the executable — so the editor app and
> `ClusterRunner` collide on one machine-wide file *no matter which option F′ takes*. Giving
> `SetupImGui()` a path seam is a **prerequisite for the new shell under every option**, not a
> nice-to-have.

- the **deviation log** — clicks used / windows opened / what the UI said / what happened, screenshot
  every `FAIL`;
- the **capability inventory** — per step, which panel actually served it and **whether its logic is
  reachable without its ImGui** (a `Handle*` method, an edit model). That last column is what the new
  shell composes, and a missing seam is the finding that matters most ([UXD-09](UX_Design.md#uxd-09)).

⚠ **Reconnaissance, not repair.** The shell is being replaced, so record shell-level annoyances as
*"the new shell must not reproduce this"* — **do not fix the old shell.** Only behaviour wrong *inside*
a panel becomes a defect task.

Then cut `UXT-nn` entries into [UX_Tasks_Detail.md](UX_Tasks_Detail.md) +
[UX_Task_Tracker.md](UX_Task_Tracker.md). **This is what fills the deliberately-empty register.**

⚠ Also correct the predictions: every `Prediction` row is code-derived and unverified. Where the walk
disagrees, the walk wins — log it in [Corrections](UX_Tasks_Detail.md#corrections).

### 4. Trace Path B, then walk it

All of Path B is **code-inferred** — nobody has established what an ExCon operator sees today. Trace it
before designing ([UXD-07](UX_Design.md#uxd-07)), then walk B1–B5 with an actual SME if possible.

### 5. Milestone 0 — stand up the new shell

Gated on [Q25-F](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell)
(the seam) but not on the rest of Q25. An empty, curated editor shell over the **shared** init path, plus
the default-layout mechanism. Deliberately near-empty: surfaces arrive only as golden-path steps earn
them.

✅ **The two pre-seam checks are done (2026-08-10)** — see
[§2](#added-2026-08-10--the-pre-seam-check-is-done). Answers: shared init keeps **zero** network
composition for the editor preset (the editor reads no `INetworkFactory` at all), and the cluster-role
perspective assumption **was found** — `LocalWindowController.cs:83` — where it is already silently
discarding the author's `BTree`/`HSM`/`Blueprint` perspective on restart. Nothing further blocks this
milestone from the coordinator's side; it is gated only on **Q25-F**.

### 6. Milestone 1 — make the editor honest

Independent of Q25's outcome and of the walk, so it can start in parallel once the user releases the
gate: [UXD-11](UX_Design.md#uxd-11) (no-dead-control enforcement) and
[UXD-10](UX_Design.md#uxd-10)/[Q25-E](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-e--where-does-the-one-problems-list-live)
(problems list). Cheap, high trust yield, and every later task's visual verification depends on controls
not lying. *A new shell inherits no dead controls of its own — but the composed panels bring theirs.*

### Not yet

Milestones 2–6. Cutting tasks for them before the walk is exactly the ad-hoc work this programme exists
to avoid.

---

## 4. Questions — all four answered

<a id="open-questions"></a>

**Answered by the user 2026-08-06.** Full effect on scope:
[UX_Requirements.md](UX_Requirements.md#answered-questions).

| # | Question | Answer | Where it landed |
|---|---|---|---|
| **OQ-1** | Who is the author? Is blueprint authoring on the path? | **Engineers / advanced military SME.** Focus on the editor. Blueprint authoring **stays on the path** — no designer-mode hiding | [Two-audience section](UX_Requirements.md#who-we-are-building-for) |
| **OQ-2** | Editor-only, or must the path hold in ExCon too? | **Both, as two paths.** ExCon = run an authored scenario and interfere live (add entities, assign mission plans) — **and that must be usable by ordinary SME** | New **[G7](UX_Requirements.md#g7--runtime-intervention-excon)**; Path B in [UX_Golden_Path.md](UX_Golden_Path.md#path-b--runtime-intervention); [UXD-07](UX_Design.md#uxd-07) |
| **OQ-3** | Undo — full model or cheap safety? | **Cheap first.** Reason: the same editor code runs in the simulation runtime, where real undo is not feasible anyway | [UXR-15](UX_Requirements.md#uxr-15)/[UXR-17](UX_Requirements.md#uxr-17) rewritten; general undo is now [non-goal 1](UX_Requirements.md#non-goals); shape → [Q25-A](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-a--how-do-we-spend-a-cheap-recoverability-budget) |
| **OQ-4** | Templates — new asset kind or scenario-embedded? | **Wanted, and likely cheap if built on what the scenario format already saves** | [UXD-04](UX_Design.md#uxd-04) `LEAN`; → [Q25-B](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-b--how-is-an-entity-template-prefab-represented) |

> **Do not re-ask these.** OQ-3's reasoning in particular is load-bearing and easy to lose: the
> constraint is *architectural* (shared editor/runtime code), not budgetary — so "we have time now,
> let's build proper undo" would be the wrong conclusion.

**Now open instead:** the five Q25 questions + 4 sub-questions, awaiting the architect. See
[Q25's answers table](Architect_Question_25_Scenario_Authoring_Golden_Path.md#answers).

---

## 5. Traps

Inherited from the blueprint programme — each one cost real time there. Full history:
[`docs/blueprints/Blueprint_Gaps_Programme_RESUME.md`](../blueprints/Blueprint_Gaps_Programme_RESUME.md).

| # | Trap | Why it matters here |
|:--:|---|---|
| 5 | **`default:` returns success** — a command silently accepted and ignored. Bitten four times | This programme's [UXR-X1](UX_Requirements.md#uxr-x1) exists to kill the whole shape |
| 6 | **Asset-scoped features belong at the host**, not inside a vendored command | Applies directly to composing outliner/inspector gestures |
| 8 | **An optional ctor dependency defaulting to an inert value** — tests pass it explicitly, production omits it, feature silently dead. Three instances | **Grep the production construction sites** before believing any panel is wired |
| 9 | **Both halves of a contract test-locked and the feature still unusable**, because no test performed the designer's gesture | Why §1.6 and §1.7 are mandatory |

**New traps for this programme** — add as they are earned:

| # | Trap |
|:--:|---|
| U1 | **Shared panels have multiple hosts.** `MissionPanel`, the entity inspector and the ORBAT panel are consumed by ExCon / IG / CGF as well as the editor. Enumerate every host before editing one, and gate on their suites |
| U2 | **README overstates the editor.** §11.4 claims "ORBAT drag-and-drop unit hierarchy" — that is ExCon's 434-line `OrbatPanel`, not the editor's 27-line stub. Treat the README as marketing, not as status |
| U3 | 🔴 **The namespace lies.** `Hrot.UI.Common` is in **no `.csproj` and no `.sln`** — it never builds — yet the panels that *do* compile (in `Hrot.Presentation/Panels/`) declare `namespace Hrot.UI.Common.Panels`. Navigating by namespace lands in dead code, and the two copies have **drifted**. *"Fix the shared ORBAT panel"* has even odds of editing a file that compiles into nothing. **Check which project a file belongs to before editing any shared panel** |
| U4 | **Absence claims generalised from one file.** The programme asserted "no right-click affordances" from a single 27-line stub; there are ~26 context-menu sites. Before writing *"X does not exist"*, grep the whole repo — and say which surfaces you checked |
| U5 | **A lean argued from plausibility, then written into an architect question as a recommendation.** The F3→F1 staging lean rested on "the extraction is risky", never measured. Measured: 2 types. **Measure before leaning, not after the architect answers** |
| U6 | **Dead code that looks live.** `EditorOrbatPanel` is *constructed* (`EditorSubsystem.cs:1559`) but its window is never registered; `EntityPropertyInspector`, ExCon's `[Obsolete]` inspector pair, and `WorkspaceMenuBuilder` are all reachable-looking and unreachable. **Grep for the registration, not the construction** |

---

## 6. Recovering from context compaction

If you are a compacted session picking this up:

1. Read **§0** (why) and **§1** (how) above — that is the irreducible context. ⚠ §0 contains a
   **corrected** root cause; do not restore the pre-2026-08-10 wording you may find quoted elsewhere.
2. Read **§2 Status** and **§3 Next up** — where the programme is and the single next action. §3 opens
   with a **re-sequencing box**; read it before the numbered list, which is partly superseded.
3. ⭐ **Read [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md) before touching any UI code.**
   It carries the seam law, the seam inventory, the duplication/rigidity registers, and 🔴 the dead
   `Hrot.UI.Common` editing trap. Skipping it is how the old, refuted claims get re-derived.
4. Open [UX_Task_Tracker.md](UX_Task_Tracker.md) for live status. **It wins over this file.**
5. For a specific task, open its [UX_Tasks_Detail.md](UX_Tasks_Detail.md) entry and
   **re-derive its evidence from code before building** (§1.4). Read its
   [Corrections](UX_Tasks_Detail.md#corrections) table too — **7 rows**, each a claim this programme
   asserted and had to withdraw. Assume the next one is in whatever you are about to build on.
6. Check `git log --oneline -15` on `claude/ux-session-resume-i2le7f` against the batch log in the
   tracker — if commits exist that the batch log does not mention, the docs are stale: reconcile them
   first, in their own commit.

**Keep this file current.** Update §2 and §3 at the end of every working session, in the same commit as
the work. A RESUME doc that lags is worse than none — that failure mode is recorded against ~6
blueprint-programme docs that had to be marked actively misleading.
