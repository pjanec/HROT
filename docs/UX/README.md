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
| 2 | [UX_Requirements.md](UX_Requirements.md) | `UXR-nn` — what "approachable" means, as testable statements. The contract. |
| 3 | [UX_Design.md](UX_Design.md) | `UXD-nn` — how we intend to satisfy the requirements, and which decisions are still open. |
| 4 | [UX_Task_Tracker.md](UX_Task_Tracker.md) | `UXT-nn` checklist. Live status. |
| 5 | [UX_Tasks_Detail.md](UX_Tasks_Detail.md) | Per-task evidence, scope, acceptance, `DONE` notes. |
| 6 | [handoffs/](handoffs/) | One doc per implementation session. Template: [HANDOFF_TEMPLATE.md](handoffs/HANDOFF_TEMPLATE.md). |

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
That work is largely done and its lessons (the nine traps) carry over verbatim.

This programme fixes the **outer loop** — the scenario shell around those canvases. Different
surface, same method: verified claims, deep-linked register, architect gate before non-trivial
builds.

See [UX_Programme_Briefing.md](UX_Programme_Briefing.md#6-inherited-traps) for what carries over.
