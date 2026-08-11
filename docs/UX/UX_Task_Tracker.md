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

**Why this precedes Milestone 0:** it is smaller than a new exe, benefits **all five modes** instead of
one, and needs **no second test path** (the user's objection to the exe plan, 2026-08-10).

| ☐ | ID | Task | Cmplx | Pattern to mirror |
|:-:|---|---|:--:|---|
| ☐ | | Delete `Hrot.UI.Common` (1,171 L, builds nowhere) + ~600 L of other dead UI — 🔴 **do this first**, it is a live editing trap ([U3](UX_RESUME.md#5-traps)) | `RW-L` | — |
| ☐ | | Perspective filter on `GlobalMenuRegistry.RegisterItem` | `RW-L` | `MainToolbarManager.RegisterEntry(…, perspective:)` |
| ☐ | | Item-provider seam on `SharedOrbatPanel` → lets ExCon's 434 L fork collapse | `RW-M` | `IEntityContextMenuHandler` |
| ☐ | | One camera path reading the **effective** viewport (fixes 4 stale copies + the occlusion defect) | `RW-M` | `MapCamera.Offset` is already the mechanism |

⚠ **Not yet cut into `UXT-nn` entries** — awaiting the user's go-ahead on the re-sequencing.

## Milestone 0 — Stand up the new shell ⏸

*⏸ **DEFERRED 2026-08-10, possibly moot.** Every difference the requirement names — layout, menu, map
layers, context menus — is a **seam problem inside shared code**, not a hosting problem. After
Milestone S the exe is a packaging decision, not an architectural one.
[Why](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-prime-measured) · [UXD-08](UX_Design.md#uxd-08)
remains `RULED` as a direction.*

| ☐ | ID | Task | Cmplx | Req | Design |
|:-:|---|---|:--:|---|---|
| ⏸ | | *deferred behind Milestone S; also still gated on [Q25-F](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell), which must not be relayed until F′ and D absorb the seam findings* | `RW-M` | | |

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
