# RESUME / HANDOFF — Scenario-Authoring UX programme

> **rev 2 · 2026-08-06 · branch `claude/reset-working-branch-qd1qpv` · HEAD at write `764b06c`**
>
> 📌 **This file exists so a session that has lost its context can resume without re-deriving
> anything.** Read §0 and §1 before doing anything else. If this file and
> [UX_Task_Tracker.md](UX_Task_Tracker.md) disagree about status, **the tracker wins**.
>
> **This session is the COORDINATOR** (Linux cloud). Implementation and testing happen in **separate
> Windows sessions**. The coordinator cannot run the editor — see [§1.10](#110-session-topology).

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

**The core insight that must not be lost:** with no outliner and no right-click affordances on
objects, *choosing a window becomes the interaction model*. That is the root cause of the user's
complaint — it is not a documentation gap, and it will not be fixed by adding features.

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
3. **Delegate to Sonnet** for mirror-a-pattern slices, mechanical edits and broad searches. Keep the
   strong model for design, the ECS/undo model, and the final diff review. **The orchestrating session
   reviews the real diff and re-runs the gates itself** — never trust a subagent's "all green".
4. **Verify before building.** The blueprint audit register was wrong ten times. Re-derive every claim
   from code — including claims in *these* docs — and fix the doc in the same commit if it was wrong.
5. **Assert the effect, never the report.** `default:`-returns-success has silently killed four
   shipped features in this codebase.
6. **Revert to watch it go red.** Required, not optional. A test written for a fix once passed against
   the bug.
7. **Visual verification is mandatory.** This is a UX programme — a green suite proves nothing about
   whether the thing feels usable. Perform the designer's gesture in the running editor
   (`--mode editor`) and record what you saw in the task's `DONE` note.
8. **Docs stay short.** Terse tables, hand-authored SVG for non-trivial diagrams, deep-link everything.
9. **Ask questions in plain chat prose** — never the multiple-choice widget.

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

Handoffs are the interface: an implementer receives the
[Briefing](UX_Programme_Briefing.md) + its handoff + the task entry, and nothing else. If a handoff
needed knowledge it did not carry, that is a coordinator defect.

---

## 2. Status

**Programme opened 2026-08-06. Nothing implemented yet — by design.**

| Artefact | State |
|---|---|
| [UX_Requirements.md](UX_Requirements.md) | ✅ **v2** — 46 requirements (`UXR-01`…`UXR-X6`), 7 groups + cross-cutting. Two-audience section added; `G7` added for Path B; `UXR-15`/`17` rewritten to the cheap-recoverability ruling; all 4 opening questions **answered** |
| [UX_Golden_Path.md](UX_Golden_Path.md) | ✅ **v0.1 — the specification.** Path A (A1–A12) + Path B (B1–B5), each step with intent / gesture / required outcome / requirement links / **code-derived prediction**. Deviation log ready. **Living document — revise it as we dig** |
| [Architect_Question_25_…](Architect_Question_25_Scenario_Authoring_Golden_Path.md) | ✅ **drafted, awaiting the architect.** Five decision-shaped questions (A–E) + 4 sub-questions, each with options, reuse-vs-build tradeoff and Claude's lean. **Answers table empty** |
| [UX_Design.md](UX_Design.md) | ▣ base, v0.2 — thesis, two-paths constraint, five-questions frame, target layout, **18** decisions. `UXD-02` is `RULED`; five are routed into Q25 |
| [UX_Tasks_Detail.md](UX_Tasks_Detail.md) | ▣ base — template, rules, complexity scale, baseline evidence index. **Register empty** |
| [UX_Task_Tracker.md](UX_Task_Tracker.md) | ▣ base — 6 milestones, all empty |
| [handoffs/](handoffs/) | ▣ template only |
| **Golden-path walk** | ☐ **not performed** — needs a Windows session. Every prediction is unverified |

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

---

## 3. Next up

<a id="next-up"></a>

> ⏸ **The user is reading the golden-path spec and Q25 before anything starts. Do not begin
> implementation work until they come back.** *(Their instruction, 2026-08-06: "let me read it before we
> start doing anything.")*

### 1. User review — the current gate

The user reads [UX_Golden_Path.md](UX_Golden_Path.md) and
[Q25](Architect_Question_25_Scenario_Authoring_Golden_Path.md). Expect the golden path to change — it is
a **living document** and the user asked for it to be adjusted as we dig. Revise it rather than working
around it.

### 2. Relay Q25 to the architect

The user relays; Claude cannot reach the architect. Answers go into
[Q25's answers table](Architect_Question_25_Scenario_Authoring_Golden_Path.md#answers), then the
matching [UXD rows](UX_Design.md#3-design-decisions-uxd) flip to `DECIDED`, then the affected milestones
unblock in [UX_Task_Tracker.md](UX_Task_Tracker.md).

### 3. Walk Path A — the task register's source

**Needs a Windows session** ([§1.10](#110-session-topology)). Delete/rename `imgui.ini` first — a walk
against a hand-tuned layout proves nothing about a new user's experience. Walk A1–A12 in order, record
clicks used / windows opened / what the UI said / what happened, screenshot every `FAIL`, and fill the
[deviation log](UX_Golden_Path.md#deviation-log). **Do not fix anything during the walk.**

Then cut one `UXT-nn` per deviation into
[UX_Tasks_Detail.md](UX_Tasks_Detail.md) + [UX_Task_Tracker.md](UX_Task_Tracker.md). **This is what
fills the deliberately-empty register** — the audit says what is broken in the code, the walk says what
stops a person, and only the second is a task list.

⚠ Also correct the predictions: every `Prediction` row in the golden path is code-derived and
unverified. Where the walk disagrees, the walk wins — log it in
[Corrections](UX_Tasks_Detail.md#corrections).

### 4. Trace Path B, then walk it

All of Path B is **code-inferred** — nobody has established what an ExCon operator sees today. Trace it
before designing ([UXD-07](UX_Design.md#uxd-07)), then walk B1–B5 with an actual SME if possible.

### 5. Milestone 1 — make the editor honest

Independent of Q25's outcome and of the walk, so it can start in parallel once the user releases the
gate: [UXD-11](UX_Design.md#uxd-11) (no-dead-control enforcement) and
[UXD-10](UX_Design.md#uxd-10)/[Q25-E](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-e--where-does-the-one-problems-list-live)
(problems list). Cheap, high trust yield, and every later task's visual verification depends on controls
not lying.

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

---

## 6. Recovering from context compaction

If you are a compacted session picking this up:

1. Read **§0** (why) and **§1** (how) above — that is the irreducible context.
2. Read **§2 Status** and **§3 Next up** — where the programme is and the single next action.
3. Open [UX_Task_Tracker.md](UX_Task_Tracker.md) for live status. **It wins over this file.**
4. For a specific task, open its [UX_Tasks_Detail.md](UX_Tasks_Detail.md) entry and
   **re-derive its evidence from code before building** (§1.4).
5. Check `git log --oneline -15` on `claude/reset-working-branch-qd1qpv` against the batch log in the
   tracker — if commits exist that the batch log does not mention, the docs are stale: reconcile them
   first, in their own commit.

**Keep this file current.** Update §2 and §3 at the end of every working session, in the same commit as
the work. A RESUME doc that lags is worse than none — that failure mode is recorded against ~6
blueprint-programme docs that had to be marked actively misleading.
