# Architect question #27 — what is a "tool"?

> **Drafted 2026-08-10 · awaiting the architect.** Claude cannot reach the architect; the user relays.
>
> **Evidence:** [UX_Feature_Tool_Model.md](UX_Feature_Tool_Model.md) — every claim below is cited there.
> **Gates:** [UXI-07](UX_Issues.md#uxi-07), and [UXR-81](UX_Requirements.md#uxr-81) /
> [UXR-84](UX_Requirements.md#uxr-84).
>
> ⚠ **Why this needs a round at all:** every other issue in this programme adopted a seam that already
> existed. A repo-wide search for `ITool` / `ToolDescriptor` / `ToolRegistry` / `ActiveTool` returns
> **zero types**. This is the programme's first genuinely new abstraction.

## Ground truth (verified against code)

| Fact | Evidence |
|---|---|
| 🔴 **Two exclusive-focus arbiters share one event bus with no arbitration between them** | `EditorSubsystem.cs:1122-1134` builds `DataDrivenGizmoSystem` and `GlobalGizmoManager` on the same `interactionBus`; each guards only its own `_focusedGizmo` (`:65` / `:31`); `FdpEventBus.Read<T>()` is non-destructive |
| Exclusivity is **per-entity**, so two tools can be live on two entities | `DataDrivenGizmoSystem._injectedGizmos` keyed by `Entity` (`:74`) |
| **Six activation idioms**; `Edit`/`Route`/`Rotate` each reachable by **two pipelines inside one class** | toolbar event path `:3806-3894` vs action path `:1143-1197` |
| **No current-tool state exists anywhere** | `IEditorLogic` has no such property; `EditorTool` is fire-and-forget |
| The toolbar **cannot** show active state, even in principle | `EditorToolbarPanel.DrawContent` — six bare `ImGui.Button`s reading no state |
| `Measure`/`Rotate` have **no toolbar button**; `Select` is a **no-op** case | `:3814-3816` |
| Repeat-click semantics differ per tool | `Edit`/`Route` toggle off; `Measure`/`Rotate` do not |
| **Escape is re-implemented in 8 gizmos** | `EntityRotatorGizmo.cs:98`, `VertexEditGizmo.cs:184`, `RouteWaypointGizmo.cs:197`, `MeasureGizmo.cs:149`, +4 |
| `EditorTool`'s doc comments name **four classes that no longer exist** | `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool` — 0 declarations; PACK2-E002 converted them to gizmos |
| Tools live in **three homes**; Editor and CGF depend on one inside **SimHost** | `EntityRotatorGizmo` in `Hrot.SimHost/Gizmos/` |

## 🔒 Standing rulings that bound every answer

| | |
|---|---|
| **Q26 constraint 3** — *"the same applies to tools: a tool descriptor is shared; its activation is host-bound"* | ⇒ share the **declaration**, bind activation per subsystem. Same split as actions |
| **Q26 constraint 1** — no higher-level concept leaks into a generic component | ⇒ `Fdp.Presentation` may know a tool id and an opaque context; never what a *mode* is |
| **User, 2026-08-10** — all map subsystems share the **full** tool set; differences are data availability or host rules, never set membership | ⇒ do **not** design a per-subsystem tool *whitelist* |
| **User, 2026-08-10** — the Editor **runs**, it is not preparation-only | ⇒ "is running" is a per-tool condition, not a tool-set axis |

## Q27-A — Where does the single arbiter live?

- **A1 — A new `IToolController` *above* both gizmo systems.** It owns `Current`, and delegates
  activation/teardown to whichever engine hosts that tool.
  *Reuse:* both engines unchanged internally. *Build:* one class + rewiring 6 idioms.
  *Cost:* a third participant in an already-crowded area; the two `_focusedGizmo` fields still exist and
  must be kept honest by the controller.
- **A2 — Fold `GlobalGizmoManager` into `DataDrivenGizmoSystem`**, giving one engine and one focus field.
  *Reuse:* the entity-bound path is already the richer one. *Build:* larger, touches a `Fdp.Toolkits`
  type used by **every** subsystem, not just the Editor.
  *Cost:* blast radius well beyond this programme.
- **A3 — Leave both engines; add arbitration only** (a shared focus token neither may bypass).
  *Reuse:* maximal. *Build:* minimal. *Cost:* fixes the 🔴 defect but delivers **no** tool model — the
  toolbar still cannot show state.

> **Claude's lean: A3 *then* A1, as two separate changes.** A3 is a correctness fix that stands alone and
> is worth shipping before any abstraction; A1 then builds the model without touching a toolkit type
> every subsystem shares. **A2 looks tempting and is the one I would most expect to be wrong** — the
> blast radius is outside this programme's remit.

## Q27-B — Is `Current` per-subsystem or per-perspective?

Co-running subsystems each own a map canvas and are *"independent, as if in their own process"*
([Glossary](UX_Glossary_Host_Mode_Subsystem.md#-co-running-subsystems-independent-and-focus-follows-perspective)),
and only the focused one draws.

- **B1 — Per subsystem.** Each keeps its own current tool; switching perspective leaves it untouched.
- **B2 — Per process, reset on perspective change.** One tool active anywhere.

> **Claude's lean: B1**, matching every other per-subsystem registry. ⚠ **But it needs a ruling on one
> case:** if SimHost has `Measure` active and the user switches to CGF and back, should `Measure` still
> be armed? B1 says yes. That may surprise an operator.

## Q27-C — Are `Modal` and `EntityBound` tools one concept or two?

`Measure`/`Spawn` are armed with no target; `Edit`/`Route`/`Rotate` act **on a specific entity** and are
injected per-entity today.

- **C1 — One concept with a `Scope` field** (the design's current shape).
- **C2 — Two concepts** — "modes" and "entity editors" — with only the descriptor shared.
- **C3 — One concept, entity as an optional activation argument** — `Activate(id, target?)`.

> **Claude's lean: C3.** It matches the existing call shape and avoids a taxonomy the evidence does not
> yet demand. ⚠ It does leave one question open: *is `Rotate` on entity A still "current" after the user
> selects entity B?* Today both stay alive — which is the bug.

## Q27-D — Does `EditorTool` survive?

- **D1 — Keep the Editor-only enum**, add the controller around it. *Cost:* SimHost/CGF cannot name a
  tool, so the shared set stays unnameable — contradicts the full-set ruling.
- **D2 — Shared string/int ids**, mirroring `GlobalActionIds`. *Reuse:* the action vocabulary's exact
  pattern; ⭐ several tools are **already** triggered through `GlobalActionIds` (`Measure`, `PlaceEntity`,
  `Rotate`, `EditOverlay`, `EditRoute`).
- **D3 — Tools *are* actions** — one vocabulary, with a `IsModal` flag.

> **Claude's lean: D2.** D3 is seductive — the overlap is real and five tools already dispatch as actions
> — but an action *fires* and a tool *stays armed*, and collapsing them would put "current tool" state
> into a registry deliberately designed to be stateless. ⚠ **This is the sub-question I am least
> confident about and would most value a ruling on.**

## Q27-E — Do the 8 Escape handlers centralise?

- **E1 — `Cancel()` delegates** to the focused gizmo's existing handler. Minimal, keeps per-gizmo cleanup.
- **E2 — Centralise cancellation policy**; gizmos expose teardown only.

> **Claude's lean: E1.** The duplication is *policy* (Escape means cancel), not *mechanism* (each gizmo's
> cleanup genuinely differs). Moving policy up is the whole win; moving cleanup up is not.

## Answers

*To be filled after the architect round; then unblock [UXI-07](UX_Issues.md#uxi-07).*

| Question | Decision | Notes |
|---|---|---|
| **A** — where the arbiter lives | | *lean A3 then A1* |
| **B** — per subsystem or per process | | *lean B1, with the switch-back case flagged* |
| **C** — modal vs entity-bound | | *lean C3* |
| **D** — `EditorTool` vs shared ids | | *lean D2 — least confident* |
| **E** — Escape | | *lean E1* |
