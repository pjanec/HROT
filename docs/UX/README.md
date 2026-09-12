# Scenario-Authoring UX Programme — document map

> **The problem.** HROT's authoring infrastructure works; the authoring *experience* does not.
> An ordinary scenario author cannot get from "new scenario" to "saved, reloaded, running scenario
> with behaviors still attached" without knowing which ImGui window to open, in what order, and
> without hitting controls that silently do nothing.
>
> **The programme.** Make the golden path — *new scenario → place entity → assign behavior → run →
> author a behavior → debug → hot-reload → iterate → save → reload → run* — walkable by someone who
> has never seen the codebase.

## Read in this order

| # | Doc | What it is |
|---|---|---|
| 0 | **[UX_RESUME.md](UX_RESUME.md)** | 📌 **Start here, always.** Goals, way of working, live progress, next task. Survives context compaction. |
| 1 | [UX_Programme_Briefing.md](UX_Programme_Briefing.md) | Big picture + work habits. **Referenced by every handoff doc** so implementation sessions inherit the context. |
| 2 | [UX_Golden_Path.md](UX_Golden_Path.md) | **The specification.** Path A (A1–A12) + Path B (B1–B5), step by step, with acceptance criteria. The programme's acceptance test and the source of its task register. |
| 3 | [UX_Requirements.md](UX_Requirements.md) | `UXR-nn` — what "approachable" means, as testable statements. The scope contract. |
| 4 | [UX_Design.md](UX_Design.md) | `UXD-nn` — how we intend to satisfy the requirements, and which decisions are still open. |
| 5 | [Architect_Question_25_…](Architect_Question_25_Scenario_Authoring_Golden_Path.md) | **Six** structural decisions, awaiting the architect. **Q25-F (new editor app / shell seam) is flagged to be answered first.** |
| 6 | [UX_Task_Tracker.md](UX_Task_Tracker.md) | `UXT-nn` checklist. Live status. |
| 7 | [UX_Tasks_Detail.md](UX_Tasks_Detail.md) | Per-task evidence, scope, acceptance, `DONE` notes. |
| 8 | [SHARED_SURFACES.md](SHARED_SURFACES.md) | 🔒 **Co-ownership + consult-before-touch.** Two programmes edit this repo; the blueprint one is active. Read before touching any shared panel or menu. |
| 9 | [../SESSION_SYNC.md](../SESSION_SYNC.md) | 🔀 **Cross-session branch registry + merge protocol.** Read before starting work or pushing — a parallel MCP session shares `EditorSubsystem.cs` with this one. |
| 10 | [MCP_PORT_PLAN.md](MCP_PORT_PLAN.md) | Description of the stranded **AI Debug API + MCP server** on `feat/ai-debug-api` and how to port it. Infrastructure, not UX — but it is a headless harness for the golden path and evidence for the shell's seams. |
| 11 | [handoffs/](handoffs/) | One doc per implementation session. Template: [HANDOFF_TEMPLATE.md](handoffs/HANDOFF_TEMPLATE.md). |

## Two audiences

| | Path A — Authoring | Path B — Runtime intervention |
|---|---|---|
| Surface | the editor (`--mode editor`, offline) | distributed **ExCon**, live exercise |
| Audience | engineers / advanced military SME | **ordinary SME people** |
| Bar | learnable, no tribal knowledge | **walk-up usable**, no engine vocabulary |

Same shared panels serve both — differences come from presentation and defaults, never forked panels.
Full statement: [Who we are building for](UX_Requirements.md#who-we-are-building-for).

## Hard constraints

1. 🔒 **`ClusterRunner` stays fully operational** — blueprint development runs against it in parallel
   sessions.
2. 🔒 **The construction kit survives** — the distributed `--mode` variants keep working. The editor app
   is one **preset** of the kit: networkless, all-in-one, in-process.
3. 🔒 **Place, do not edit** — prefer placing existing windows into the designed layout; in-window and
   shared-menu changes go through [SHARED_SURFACES.md](SHARED_SURFACES.md) first.

## Session topology

**Coordinator** = one Linux cloud session (design, docs, task cutting, review) — **cannot run the
editor**. **Implementers** = Windows local sessions (build, run, walk, verify). Every coordinator claim
about running behaviour is a labelled prediction until a walk confirms it.

🔀 **Two other long-running programmes touch the same code.** The **MCP port**
([entry](../mcp-port/MCP_PORT_RESUME.md)) shares `EditorSubsystem.cs` with us and exchanges updates both
ways; the **blueprint programme** ([entry](../blueprints/Blueprint_Gaps_Programme_RESUME.md)) is active in
parallel and must be treated as unreachable. Protocol:
[SESSION_SYNC.md](../SESSION_SYNC.md).

## Source of truth

**[UX_Task_Tracker.md](UX_Task_Tracker.md) + [UX_Tasks_Detail.md](UX_Tasks_Detail.md) are the source
of truth for status.** [UX_RESUME.md](UX_RESUME.md) is orientation — if it disagrees with the
tracker, the tracker wins.

**[UX_Requirements.md](UX_Requirements.md) is the source of truth for scope.** A task that does not
trace to a `UXR-nn` does not belong in this programme.

## ID schemes

| Prefix | Meaning | Lives in |
|---|---|---|
| `UXR-nn` | Requirement | [UX_Requirements.md](UX_Requirements.md) |
| `UXD-nn` | Design decision | [UX_Design.md](UX_Design.md) |
| `UXT-nn` | Task | [UX_Tasks_Detail.md](UX_Tasks_Detail.md) (tracked in [UX_Task_Tracker.md](UX_Task_Tracker.md)) |
| `Q25+` | Architect question | `docs/UX/Architect_Question_NN_*.md` (continues the global architect sequence) |

## Relationship to the blueprint programme

`docs/blueprints/` fixed the **inner loop** — editing inside a graph canvas. 17 batches, ~76 issues.
Its lessons (the nine traps) carry over verbatim.

🔒 **It is still active, in parallel sessions.** `ClusterRunner` must stay operational, and the graph
editor windows are that programme's live surface — **place them, do not touch them.** See
[SHARED_SURFACES.md](SHARED_SURFACES.md).

This programme fixes the **outer loop** — the scenario shell around those canvases. Different
surface, same method: verified claims, deep-linked register, architect gate before non-trivial
builds.

See [UX_Programme_Briefing.md](UX_Programme_Briefing.md#6-inherited-traps) for what carries over.
