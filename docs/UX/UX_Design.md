# Scenario-Authoring UX — Design (`UXD`)

> **Status: BASE — v0.1, 2026-08-06.** Structure and decision register are in place; most decisions
> are **OPEN** pending the golden-path walk and the architect round (Q25).
> **Do not implement from an `OPEN` decision.**
>
> Requirements: [UX_Requirements.md](UX_Requirements.md) · Tasks: [UX_Task_Tracker.md](UX_Task_Tracker.md) ·
> Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

## 1. Design thesis

HROT's authoring problem is **not** missing capability. Nearly everything the golden path needs exists
somewhere in the codebase. The problem is that the capability is reachable only by knowing the
implementation: which window hosts it, which perspective owns it, which order the panels must be
touched in.

> **Therefore the design is mostly composition, not construction.**
> We are building a *spine* that the existing capability hangs off, plus the feedback layer that makes
> it trustworthy. Two items are genuine construction: scenario undo ([UXR-15](UX_Requirements.md#uxr-15))
> and entity templates ([UXR-16](UX_Requirements.md#uxr-16)).

### The inversion

HROT was built bottom-up: subsystem → panel → menu entry. This programme runs the other way:

```
golden path  →  what the author must see at each step  →  which panel owns it  →  what must be wired
```

Consequence: **the golden-path walkthrough is the specification**, and panels are implementation
detail. When a design question arises, the tiebreaker is "which answer makes the walkthrough shorter
for the author", not "which fits the current window layout".

## 2. The five questions → the target layout

From [Briefing §3](UX_Programme_Briefing.md#3-the-frame-we-design-against). Every panel in the default
layout must own one of the five questions; a panel that owns none does not belong in the default layout.

| Question | Owner | State |
|---|---|---|
| 1. Where am I? | Title/header + play-mode chrome | to build |
| 2. What is in my world? | **Outliner** | stub — the spine |
| 3. What is this thing? | **Inspector** (with Behaviors section) | fragmented across 4 panels |
| 4. What can I do now? | Toolbar + object context menu + palette | weak / absent |
| 5. Did it work? | **Problems panel** + status pill + toasts | absent |

Target default layout for the **Editor** perspective — *proposed, see [UXD-01](#uxd-01)*:

```
┌──────────────────────────────────────────────────────────────────────┐
│ menu bar · scenario name* · [PLAY ▶] [⏸] [⏭] [⏹] · mode chrome      │
├────────────┬────────────────────────────────────────┬────────────────┤
│            │                                        │                │
│ OUTLINER   │              MAP / VIEWPORT            │   INSPECTOR    │
│ (Q2)       │                                        │   (Q3)         │
│            │           tool overlay (Q4)            │   ├ Identity   │
│ ORBAT tree │                                        │   ├ Transform  │
│ names+icons│                                        │   ├ Behaviors  │
│            │                                        │   └ Components │
├────────────┴────────────────────────────────────────┴────────────────┤
│ PROBLEMS (Q5)  │  Asset Browser  │  Hot Reload Log                   │
└──────────────────────────────────────────────────────────────────────┘
```

The graph canvases (BTree/HSM/Blueprint) keep their own perspectives — they are already good. What
changes is that **entering and leaving them is driven from the entity**, not from the Perspective menu.

## 3. Design decisions (`UXD`)

Status: `OPEN` = undecided · `LEAN` = Claude's recommendation, awaiting architect/user · `DECIDED` =
ruled, safe to implement (records who ruled and where).

### Structural

| ID | Decision | Status | Notes |
|---|---|:--:|---|
| <a id="uxd-01"></a>**UXD-01** | The default Editor-perspective layout (§2) | `LEAN` | Proposed above. Cheap to change now, expensive later — settle before UXT work on panels. Ties [UXR-04](UX_Requirements.md#uxr-04) |
| <a id="uxd-02"></a>**UXD-02** | **Scenario mutation model** — how authoring edits reach the ECS so they can be undone | `OPEN` 🔴 | **The single biggest decision in the programme.** Options: (a) command/undo-stack over an edit service, all gizmos and panels routed through it; (b) snapshot-diff undo reusing the preview snapshot machinery; (c) no undo — cheap safety net only (confirm + autosave + revert-to-saved). Blocks [UXR-15](UX_Requirements.md#uxr-15), [UXR-17](UX_Requirements.md#uxr-17). **Architect round required.** See OQ-3 |
| <a id="uxd-03"></a>**UXD-03** | **Behavior as a first-class concept** — one author-facing "Behavior" with one affinity contract, one param schema and one assignment path, over three implementations | `OPEN` | Blocks [UXR-20](UX_Requirements.md#uxr-20), [UXR-22](UX_Requirements.md#uxr-22), [UXR-40](UX_Requirements.md#uxr-40), [UXR-41](UX_Requirements.md#uxr-41). Reuse candidate: `BehaviorRegistry` + `[BehaviorContract]` + `BehaviorUiCompiler` already form two-thirds of this |
| <a id="uxd-04"></a>**UXD-04** | **Entity template/prefab representation** — new asset kind vs scenario-embedded; override semantics | `OPEN` | Blocks [UXR-16](UX_Requirements.md#uxr-16). See OQ-4 |
| <a id="uxd-05"></a>**UXD-05** | **Where the Behaviors section lives** — inside the unified Inspector, or a dedicated docked panel the Inspector links to | `OPEN` | Blocks [UXR-14](UX_Requirements.md#uxr-14), [UXR-20](UX_Requirements.md#uxr-20) |
| <a id="uxd-06"></a>**UXD-06** | **Offline vs cluster surface split** — how one panel serves both without showing OCC/Force-Commit offline | `OPEN` | Blocks [UXR-26](UX_Requirements.md#uxr-26). Depends on OQ-2 |

### Feedback & trust

| ID | Decision | Status | Notes |
|---|---|:--:|---|
| <a id="uxd-10"></a>**UXD-10** | **Diagnostic bus** — one sink that validation, compiler, load and runtime faults all publish to, with a navigable source reference per entry | `LEAN` | Required by [UXR-X2](UX_Requirements.md#uxr-x2), [UXR-34](UX_Requirements.md#uxr-34), [UXR-62](UX_Requirements.md#uxr-62). Reuse candidates to trace: the alert manager, `DiagnosticsWindow`, `FindResultsWindow`'s navigation model |
| <a id="uxd-11"></a>**UXD-11** | **No-dead-control enforcement mechanism** — command-registry completeness test + debug-build throwing `default:` arms | `LEAN` | Required by [UXR-X1](UX_Requirements.md#uxr-x1). Cheap, high trust yield, and it protects every later task. **Strong candidate for the first slice** |
| <a id="uxd-12"></a>**UXD-12** | **Acknowledgement mechanism** — toast/status-line convention for gesture outcomes | `OPEN` | Required by [UXR-X3](UX_Requirements.md#uxr-x3). Must not become modal spam |
| <a id="uxd-13"></a>**UXD-13** | **Play-mode chrome** — what changes visually, and the Play/Pause/Step/Stop contract wording | `LEAN` | Tint + explicit state label. Reuses correct `EditorPreviewAdapter` snapshot/rewind semantics. Consider renaming Preview → Play, keeping an Unreal-style *Simulate* distinction if needed. [UXR-31](UX_Requirements.md#uxr-31) |

### Composition & reuse

| ID | Decision | Status | Notes |
|---|---|:--:|---|
| <a id="uxd-20"></a>**UXD-20** | **Outliner build vs adopt** — grow `EditorOrbatPanel` (27-line stub) or adapt ExCon's `OrbatPanel` (434 lines, has hierarchy) | `OPEN` | Blocks [UXR-10](UX_Requirements.md#uxr-10)…[UXR-13](UX_Requirements.md#uxr-13). **Trace ExCon's panel first** — adopting may be most of the work already done. Note it targets the cluster/ExCon data path |
| <a id="uxd-21"></a>**UXD-21** | **Param authoring coverage** — extend `BehaviorUiCompiler`'s attribute vocabulary so map-pick/entity-pick are declarative, retiring the 3 special-cased behaviors and the raw-JSON fallback | `LEAN` | [UXR-23](UX_Requirements.md#uxr-23). Existing infra is good; the gap is vocabulary + a "no DTO ⇒ loud diagnostic" rule instead of a silent JSON textbox |
| <a id="uxd-22"></a>**UXD-22** | **Params storage** — structural JSON vs the current escaped string | `OPEN` | [UXR-24](UX_Requirements.md#uxr-24), [UXR-63](UX_Requirements.md#uxr-63). Migration cost: existing scenarios + `MissionPlanTranslator` + the DDS wire form. **Check the wire form before assuming this is editor-local** |
| <a id="uxd-23"></a>**UXD-23** | **Command palette** — new, or extend the existing command registry + `FindResultsWindow` pattern | `LEAN` | [UXR-05](UX_Requirements.md#uxr-05). Commands already carry `Id`/`DisplayName`/`Category`/`IsEnabled` (`EditorCommandDescriptor`) — a palette is largely a view over what exists |
| <a id="uxd-24"></a>**UXD-24** | **Object context menus** as the primary discovery path | `LEAN` | [UXR-25](UX_Requirements.md#uxr-25), [UXR-43](UX_Requirements.md#uxr-43), [UXR-50](UX_Requirements.md#uxr-50). Seams exist (`JsonEntityContextMenuHandler`, `ContextMenuLogic`, gizmo context menus with icon resolver) |

## 4. Sequencing principle

Ordered by **trust-per-unit-cost**, not by visibility. Rationale: every later task is verified by a
person walking the golden path — if controls still lie, that verification is worthless.

1. **Make the editor honest** — no dead controls, acknowledgement, problems panel ([UXD-11](#uxd-11),
   [UXD-10](#uxd-10), [UXD-12](#uxd-12)). Nothing downstream is verifiable without this.
2. **Build the spine** — outliner, unified inspector, selection ([UXD-20](#uxd-20), [UXD-05](#uxd-05)).
3. **Make the loop legible** — play chrome, status pill, tool state, palette, context menus
   ([UXD-13](#uxd-13), [UXD-23](#uxd-23), [UXD-24](#uxd-24)).
4. **Fix assignment** — one model, no allocator internals, typed params ([UXD-03](#uxd-03),
   [UXD-21](#uxd-21)).
5. **Structural bets** — undo model, templates ([UXD-02](#uxd-02), [UXD-04](#uxd-04)). Architect-gated.
6. **Prove the round-trip** — an explicit save/reload/run regression, then the walkthrough doc
   ([UXR-61](UX_Requirements.md#uxr-61), [UXR-X6](UX_Requirements.md#uxr-x6)).

Milestones are not yet cut into tasks — that follows the golden-path walk (see
[UX_RESUME.md](UX_RESUME.md#next-up)).

## 5. Constraints

| Constraint | Consequence for design |
|---|---|
| **ImGui** (immediate-mode) via `Fdp.Presentation.WindowManager` | No retained widget tree; panel state is explicit. Layout is docking + perspectives — work with that machinery, do not fork it |
| Editor is **one subsystem among many** (`--mode editor`), co-locating Brain + Muscle | Author-facing changes must not regress the ExCon/IG/CGF surfaces that share these panels |
| Panels are **shared across subsystems** (`MissionPanel`, `FdpEntityInspectorWindow`, ORBAT) | Prefer additive composition over editing shared panels in place; verify the other hosts |
| **Headless-testable seams** are the house pattern (logic in `Handle*` methods, ImGui in the composition root) | Keep it. It is why this codebase can be tested at all — and [UXD-11](#uxd-11) depends on it |
| Scenario JSON has a **migration adapter** with schema stamping and sidecars | Persistence changes ([UXD-22](#uxd-22)) must ship a migration, not a break |
| The **architect cannot be reached by Claude** | Every `OPEN` decision above needs a relayed round. Batch them into as few architect questions as possible |

## 6. Risks

| Risk | Mitigation |
|---|---|
| **Shared panels regress other subsystems.** ExCon/IG/CGF consume the same panels | Enumerate every host of a panel before touching it; run their test suites as gates |
| **The undo decision blocks everything else** | Sequenced last among structural work (§4); the cheap-safety option (c) is a valid ship |
| **Cosmetic churn mistaken for progress.** Icons and tints feel productive and change nothing | Every task states which `UXR` it closes and which of the five questions it improves. No `UXR` ⇒ no task |
| **Scope creep into runtime capability** | Non-goals list in [UX_Requirements.md](UX_Requirements.md#non-goals) is binding |
| **Doc drift** — this programme's own registers becoming misleading, as happened to ~6 blueprint docs | The tracker is the only status source; every task ends by updating it in the same commit |
| **A UX change that tests green and still feels bad** | Mandatory visual verification per task ([Briefing §5.9](UX_Programme_Briefing.md#59-visual-verification-is-mandatory)) |
