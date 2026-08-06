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

> **The register is deliberately empty.** Tasks are cut from the **golden-path walk**, not from the
> audit — the audit says what is broken in the code, the walk says what stops an author, and only the
> second is a task list. See [UX_RESUME.md](UX_RESUME.md#next-up).

---

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

*Scenario undo, entity templates. Closes [UXR-15](UX_Requirements.md#uxr-15),
[UXR-16](UX_Requirements.md#uxr-16), [UXR-17](UX_Requirements.md#uxr-17).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| 🔒 | | *blocked on [UXD-02](UX_Design.md#uxd-02) / [UXD-04](UX_Design.md#uxd-04) — architect round (Q25)* | `RW-H` | | |

## Milestone 6 — Prove the round-trip

*Save/reload/run regression + the author walkthrough. Closes
[UXR-61](UX_Requirements.md#uxr-61), [UXR-62](UX_Requirements.md#uxr-62),
[UXR-X6](UX_Requirements.md#uxr-x6).*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| | | *no tasks cut yet* | | | |

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
