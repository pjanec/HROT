# RESUME / HANDOFF — Scenario-Authoring UX programme

> **rev 1 · 2026-08-06 · branch `claude/reset-working-branch-qd1qpv` · HEAD at write `31e7ddd`**
>
> 📌 **This file exists so a session that has lost its context can resume without re-deriving
> anything.** Read §0 and §1 before doing anything else. If this file and
> [UX_Task_Tracker.md](UX_Task_Tracker.md) disagree about status, **the tracker wins**.

---

## 0. Why this programme exists — never forget this

HROT's authoring **infrastructure works**. The authoring **experience does not**. The user's own words:

> *"all that exists but it is not user friendly at all, not straightforward, not bulletproof, not
> intuitive, requires lots of knowledge of what imgui panel/window to open, in what sequence to use it.
> It is working to a large extent infrastructure-wise, but is practically unusable because of very bad
> UX. HROT was built bottom-up, this might need an inverted approach."*

**The goal:** an ordinary scenario author — not an engine developer — can walk the golden path end to
end without being told which window to open.

```
new scenario → place entity → assign behavior (mission plan w/ tasks) → run it
  → author a new behavior (BTree / Blueprint) → run it → debug it → hot-reload it
  → iterate until working → save scenario → reload it → run → behaviors still attached
```

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

---

## 2. Status

**Programme opened 2026-08-06. Nothing implemented yet — by design.**

| Artefact | State |
|---|---|
| [UX_Requirements.md](UX_Requirements.md) | ✅ v1 baseline — 40 requirements (`UXR-01`…`UXR-X6`), 6 golden-path groups + cross-cutting, non-goals, 4 open questions |
| [UX_Design.md](UX_Design.md) | ▣ **base only** — thesis, five-questions frame, target layout, 17 decisions registered. Most are `OPEN`/`LEAN`. **Do not implement from an `OPEN` decision** |
| [UX_Tasks_Detail.md](UX_Tasks_Detail.md) | ▣ base — template, rules, complexity scale, baseline evidence index. **Register empty** |
| [UX_Task_Tracker.md](UX_Task_Tracker.md) | ▣ base — 6 milestones, all empty |
| [handoffs/](handoffs/) | ▣ template only |
| Architect question Q25 | ☐ not drafted |
| Golden-path walk | ☐ not performed |

**The opening audit is done** and its findings are recorded in two places: the `Now` column of each
requirement, and the [baseline evidence index](UX_Tasks_Detail.md#baseline-evidence-index). The
headline verified findings:

- outliner is a **27-line stub** printing `• [entityId]`;
- **no undo exists on the scenario side at all** 🔴;
- toolbar is 6 text buttons, no state / shortcuts / tooltips, mixing authoring with dev plumbing;
- `New Scenario` yields a void;
- **two unrelated behavior-assignment models**, one exposing blackboard tiers/bytes/`OverCeiling`;
- behavior params fall back to raw JSON, stored as escaped JSON-in-JSON;
- **no problems panel** — the editor's answer to "why isn't my unit moving?" is silence;
- ✅ one genuinely good bone: `EditorPreviewAdapter` does correct ECS snapshot-on-play /
  rewind-on-stop. Build on it.

---

## 3. Next up

<a id="next-up"></a>

**In this order. Do not skip 1 — the task register is deliberately empty and step 1 is what fills it.**

### 1. The golden-path walk *(next action)*

Write the golden path as a **numbered, keystroke-level walkthrough** — every step with its acceptance
criterion (A1: ≤2 clicks from the previous state, no window-opening detour; A2: outcome stated in the
UI). Then **walk it in the running editor** (`--mode editor`) and log every deviation.

Each deviation becomes a `UXT-nn` entry. **This is the task register's source** — the audit says what is
broken in the code, the walk says what stops an author, and only the second is a task list.

> Output: `docs/UX/UX_Golden_Path.md` (the spec) + populated
> [UX_Tasks_Detail.md](UX_Tasks_Detail.md) / [UX_Task_Tracker.md](UX_Task_Tracker.md).
>
> ⚠ **Blocker to check first:** whether the editor can actually be launched and driven in this
> environment. It is a Windows/Raylib ImGui app (`run_Editor.bat`); this is a Linux cloud container. If
> it cannot be run here, the walk must either be done by the user, or replaced by a code-derived
> dry-walk that is **explicitly labelled as unverified** — do not silently downgrade it.

### 2. Architect question Q25

Draft `docs/UX/Architect_Question_25_Scenario_Authoring_Golden_Path.md`, batching the `OPEN` structural
decisions into one relayed round: [UXD-02](UX_Design.md#uxd-02) (scenario mutation/undo model — the
biggest), [UXD-03](UX_Design.md#uxd-03) (Behavior as a first-class concept),
[UXD-04](UX_Design.md#uxd-04) (entity templates), [UXD-06](UX_Design.md#uxd-06) (offline vs cluster
surface split). A/B/C/D form, Claude's lean per sub-question, reuse-vs-build tradeoff each.

### 3. Milestone 1 — make the editor honest

Independent of Q25 and of the walk's outcome, so it can start in parallel:
[UXD-11](UX_Design.md#uxd-11) (no-dead-control enforcement) and
[UXD-10](UX_Design.md#uxd-10) (diagnostic bus / problems panel). Cheap, high trust yield, and every
later task's verification depends on controls not lying.

### Not yet

Milestones 2–6. Cutting tasks for them before the walk is exactly the ad-hoc work this programme exists
to avoid.

---

## 4. Open questions

<a id="open-questions"></a>

**Asked of the user 2026-08-06; unanswered.** Recorded in
[UX_Requirements.md](UX_Requirements.md#open-questions-blocking-requirements) too.

| # | Question | Blocks | Answer |
|---|---|---|---|
| **OQ-1** | Who is the "ordinary author" — a military SME with no programming background, or an engineer? Does blueprint authoring belong on the golden path at all, or behind a *designer* mode? | All of G4; [UXR-40](UX_Requirements.md#uxr-40), [UXR-21](UX_Requirements.md#uxr-21) | — |
| **OQ-2** | Is the golden path **editor-only** (`--mode editor`, offline, snapshot/rewind), or must it also hold in the distributed ExCon/CGF path? The OCC machinery in `MissionPanel` only makes sense for the latter and is much of what makes assignment feel heavy offline | [UXR-26](UX_Requirements.md#uxr-26), [UXR-20](UX_Requirements.md#uxr-20), [UXD-06](UX_Design.md#uxd-06) | — |
| **OQ-3** | Scenario undo: pay for a command-based mutation model, or buy safety cheaply first (confirm-destructive + autosave + revert-to-saved)? | [UXR-15](UX_Requirements.md#uxr-15), [UXR-17](UX_Requirements.md#uxr-17), [UXD-02](UX_Design.md#uxd-02) | — |
| **OQ-4** | Entity templates: a new asset kind, or scenario-embedded? | [UXR-16](UX_Requirements.md#uxr-16), [UXD-04](UX_Design.md#uxd-04) | — |

**OQ-1 and OQ-2 shape the requirements themselves**, not just the design — worth chasing before
Milestone 4. OQ-3 and OQ-4 are architect-round material (Q25).

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
