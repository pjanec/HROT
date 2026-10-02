# Scenario-Authoring UX — Task Tracker

> **Source of truth for status.** Full detail for every ID:
> **[UX_Tasks_Detail.md](UX_Tasks_Detail.md)** (every row deep-links to its entry, `#uxt-nn`).
> Scope: [UX_Requirements.md](UX_Requirements.md) · Design: [UX_Design.md](UX_Design.md).
>
> 📌 **Resuming this programme?** Start with **[UX_RESUME.md](UX_RESUME.md)** — goals, way of working,
> progress, next task. This tracker stays the source of truth; if the two disagree, the tracker wins.

**Complexity:** `WIRING` = call existing code, no new logic · `RW-L` = real work, low (≲150 lines) ·
`RW-M` = real work, medium (new panel/component, some design) · `RW-H` = real work, high (new
subsystem or architect decision first). 🔴 = correctness / data-loss / trust defect, not an enhancement.

**Status:** ☐ open · ▣ in progress · ☑ done · ⊘ refuted on verification ·
🔒 blocked (design decision `OPEN`, or architect round pending).

## Counts

| Complexity | Open | Done |
|---|---:|---:|
| `WIRING` | — | — |
| `RW-L` | — | — |
| `RW-M` | — | — |
| `RW-H` | — | — |
| **Total** | **0** | **0** |

> **The register is deliberately empty.** Tasks are cut from the
> **[golden-path walk](UX_Golden_Path.md#deviation-log)**, not from the audit — the audit says what is
> broken in the code, the walk says what stops a person, and only the second is a task list.
> **The walk has not been performed** (it needs a Windows session). See
> [UX_RESUME.md](UX_RESUME.md#next-up).

---

## Milestone S — Seam work ⭐ *(new 2026-08-10, now the opening move)*

*Give the shared surfaces that lack one a **contribution seam**, so each mode can differ in menu, ORBAT
items, map composition and camera without forking. Established by
**[UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md)**: every surface with a seam is shared
successfully, every surface without one has been forked — no counter-example in five scans.*

**This replaced Milestone 0** (user ruling, 2026-08-10 — no new editor executable): it is smaller,
benefits **all five modes** instead of one, and needs **no second test path**, which was the objection
that sank the exe plan.

**Staged in [UX_Cleanup_Path.md](UX_Cleanup_Path.md):**

| Stage | What | Gated on |
|---|---|---|
| **0** | Delete ~1,700 L of **superseded** UI incl. the `Hrot.UI.Common` namespace trap. ⚠ **Half-built code is a separate B-list that must NOT be deleted** — `ScenarioEditorModule` (stub for PACK2-E002 *tool migration* = stage 4), `SelectionRenderSystem` (migrated, unwired, test-locked), `WorkspaceMenuBuilder` (model, no renderer) | nothing — [Q26-E](Architect_Question_26_Entity_Action_Model.md) lean is *ship it alone* |
| **1** | Name the vocabulary — `IEntityAction` / provider / context; one core provider replaces the 3 copies of `Delete` | [Q26-A](Architect_Question_26_Entity_Action_Model.md), [Q26-C](Architect_Question_26_Entity_Action_Model.md) |
| **2** | One menu on every surface — map, inspector, ORBAT ([UXR-85](UX_Requirements.md#uxr-85)) | Q26-A, Q26-A′ |
| **3** | Perspective enters the context ([UXR-86](UX_Requirements.md#uxr-86)); menu perspective filter; fix the restore bug | [Q26-B](Architect_Question_26_Entity_Action_Model.md), [Q26-D](Architect_Question_26_Entity_Action_Model.md) |
| **4** | Tools become first-class ([UXR-81](UX_Requirements.md#uxr-81), [UXR-84](UX_Requirements.md#uxr-84)) | Q26-B |
| **5** | Camera + effective viewport — **independent, can run in parallel** | nothing |
| **6** | Consolidate spawn UI ×4, gizmo menu bar ×4, `PanelConstants` | 1–4 |

⚠ **Not yet cut into `UXT-nn` entries.** Stage 0 is ready to cut on the user's word; stages 1–4 wait on Q26.

## Milestone 0 — Stand up the new shell ⊘ CLOSED

*⊘ **CLOSED 2026-08-10 — the user withdrew the plan.** No new editor executable; cleanup happens inside
the existing one. [UXD-08](UX_Design.md#uxd-08) is `WITHDRAWN`, Q25-F/F′ are moot. Kept as a heading so
the closure is visible rather than the milestone silently vanishing.
Replacement: **[UX_Cleanup_Path.md](UX_Cleanup_Path.md)**.*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| ⊘ | | *closed — superseded by the staged cleanup* | — | | |

## Milestone 1 — Make the editor honest

*Nothing downstream is verifiable while controls can lie. Closes
[UXR-X1](UX_Requirements.md#uxr-x1), [UXR-X2](UX_Requirements.md#uxr-x2),
[UXR-X3](UX_Requirements.md#uxr-x3), [UXR-34](UX_Requirements.md#uxr-34).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| | | *no tasks cut yet* | | | |

## Milestone 2 — Build the spine

*Outliner, unified inspector, selection. Closes [UXR-10](UX_Requirements.md#uxr-10)…[UXR-14](UX_Requirements.md#uxr-14),
[UXR-04](UX_Requirements.md#uxr-04).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| | | *no tasks cut yet* | | | |

## Milestone 3 — Make the loop legible

*Play chrome, status pill, tool state, palette, context menus. Closes
[UXR-31](UX_Requirements.md#uxr-31), [UXR-05](UX_Requirements.md#uxr-05),
[UXR-25](UX_Requirements.md#uxr-25), [UXR-42](UX_Requirements.md#uxr-42),
[UXR-X4](UX_Requirements.md#uxr-x4), [UXR-01](UX_Requirements.md#uxr-01)…[UXR-03](UX_Requirements.md#uxr-03).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| | | *no tasks cut yet* | | | |

## Milestone 4 — Fix behavior assignment

*One model, no allocator internals, typed params, correct list. Closes
[UXR-20](UX_Requirements.md#uxr-20)…[UXR-26](UX_Requirements.md#uxr-26),
[UXR-40](UX_Requirements.md#uxr-40)…[UXR-43](UX_Requirements.md#uxr-43).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| | | *no tasks cut yet* | | | |

## Milestone 5 — Structural bets (architect-gated)

*Recoverability net, entity templates. Closes [UXR-15](UX_Requirements.md#uxr-15),
[UXR-16](UX_Requirements.md#uxr-16), [UXR-17](UX_Requirements.md#uxr-17).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| 🔒 | | *blocked on [Q25-A](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-a--how-do-we-spend-a-cheap-recoverability-budget) ([UXD-02](UX_Design.md#uxd-02), `RULED` cheap — shape pending)* | `RW-M` | | |
| 🔒 | | *blocked on [Q25-B](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-b--how-is-an-entity-template-prefab-represented) ([UXD-04](UX_Design.md#uxd-04) — template representation + override semantics)* | `RW-M` | | |

## Milestone 6 — Prove the round-trip

*Save/reload/run regression + the author walkthrough. Closes
[UXR-61](UX_Requirements.md#uxr-61), [UXR-62](UX_Requirements.md#uxr-62),
[UXR-X6](UX_Requirements.md#uxr-x6).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| | | *no tasks cut yet* | | | |

## Milestone 7 — Path B: runtime intervention (ExCon)

*The **ordinary SME** surface — narrow, strictest bar. Closes
[G7](UX_Requirements.md#g7--runtime-intervention-excon) ([UXR-70](UX_Requirements.md#uxr-70)…[UXR-75](UX_Requirements.md#uxr-75)).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| 🔒 | | *blocked twice over: all of Path B is code-inferred (**trace it first**, [UXD-07](UX_Design.md#uxd-07)) and the shared-panel mechanism is [Q25-D](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-d--two-audiences-one-set-of-shared-panels-what-is-the-mechanism)* | | | |

---

## Batch log

Newest first. One entry per shipped batch — what went in, what it exposed, which gates were green.

| Batch | Date | Items | Notes |
|---|---|---|---|
| — | — | — | *nothing shipped yet; programme opened 2026-08-06* |

## Test baseline

To be recorded on the first batch: which suites gate this programme, their green counts, and any known
flakes. The blueprint programme's eight gates (blueprints, breakpoints, NodeEdit core + UI, AiShared,
BTree editor, generators, build) are the starting set — this programme adds the editor-side suites
(`Hrot.Editor.Tests`, `Hrot.Presentation.Tests`, `Hrot.ExCon.Tests`, `Hrot.Editor.AiShared.Tests`) and
must **not** regress the ExCon/IG/CGF hosts of any shared panel.
