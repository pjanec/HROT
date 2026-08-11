# Architect question #26 — One entity-action vocabulary across surfaces and modes

> **Drafted 2026-08-10 · awaiting the architect.** Claude cannot reach the architect; the user relays.
>
> **Context:** [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md) (the evidence) and
> [UX_Cleanup_Path.md](UX_Cleanup_Path.md) (the staged proposal these questions gate).
>
> ⚠ **This supersedes [Q25-D](Architect_Question_25_Scenario_Authoring_Golden_Path.md), and Q25-F/F′ are
> moot** — the user withdrew the dedicated-editor-exe plan on 2026-08-10.

## Ground truth (verified against code)

| Fact | Evidence |
|---|---|
| **The execution layer is already unified.** All menu paths reach `GlobalActionRegistry` (`int id → handler`) via `GlobalActionDispatchSystem`; ids cross DDS | `GlobalActionRegistry.cs:15-27`, `GlobalActionIds.cs:10`, `ContextActionIngressSystem.cs:32-72` |
| **The authoring layer is fragmented three ways** — inspector lambdas, map JSON, hardcoded ORBAT items | `EditorSubsystem.cs:1425-1516` · `ContextMenuProjectorGizmo.cs` · `OrbatPanel.cs:290-314` |
| `IEntityContextMenuHandler` works and is used per host: Editor **4** providers, SimHost **1**, IG **1**, CGF **1**, ExCon **0** | `EntityInspectorPanel.cs:136`, `LambdaEntityContextMenuHandler.cs:21` |
| **`Center` and `Delete` are reimplemented three times with different behaviour** | Editor publishes `DestroyEntityCommand`; SimHost branches on `NetworkIdentity`, falls back to `_repo.DestroyEntity`, clears selection + inspector state |
| **Two independent parsers of the same JSON payload** | Editor's `JsonEntityContextMenuHandler.cs:74-120` vs IG's `ContextMenuSystem.cs:44` |
| **No perspective filter exists on any menu**; the toolbar has one, the menu registry does not | `WindowManager.cs:569-636` vs `MainToolbarManager.cs:129-134` |
| The interaction core (selection, drag, gizmo execution) is **already shared** by Editor, SimHost and IG | `EditorSubsystem.cs:1287`, `SimHostVisualization.cs:250`, `IgApplication.cs:767` |

**User brief, 2026-08-10:** Editor, SimHost and CGF *"should share most capabilities — capability to
share, but inheriting optionally only what is necessary."* ⇒ **share by default, opt out by profile.**
IG's menu must remain **configurable over the network**; ExCon is **natively mapless** and is the source
of those remote menus.

## Q26-A — Is one action vocabulary right, and how far does it reach?

- **A1 — One `IEntityAction` vocabulary, every surface.** Map, inspector and ORBAT all render the same
  provider-resolved list. The network/JSON menu becomes **one provider among several**.
  *Reuse:* `IEntityContextMenuHandler` generalised by two parameters; `GlobalActionRegistry` untouched as
  the backend. *Build:* the action/provider/context types + one adapter per surface.
  *Cost:* every surface's menu code changes at once.
- **A2 — Unify the local surfaces only**; leave the network/JSON menu a separate pipeline.
  *Reuse:* more. *Build:* less. *Cost:* keeps two parsers and two mental models; IG's menu stays unable
  to carry a locally-defined action.
- **A3 — Do nothing structural**; extract only a shared *item library* the existing lambdas call.
  *Reuse:* maximal. *Build:* minimal. *Cost:* solves the triplicated `Delete` but not the
  cross-surface-consistency requirement.

> **Claude's lean: A1.** The consistency requirement is cross-surface by definition, and A2 leaves the
> exact seam that would have to be reopened. A1 is also the smaller change than it appears, because the
> execution backend already unifies — only the *list-building* moves.
>
> **Sub-question A′:** should **ORBAT rows** be in scope for stage 2, or follow later? Including them
> collapses ExCon's 434-line fork via the same mechanism; excluding them keeps the stage smaller.

## Q26-B — Where does a profile live?

A profile answers *"which actions/tools/menu items does this mode present?"*

- **B1 — Code, per composition root.** Each host declares its set where it composes today.
  *Reuse:* matches the current idiom exactly. *Build:* nearly none. *Cost:* not inspectable, not
  diffable, and a designer cannot change it.
- **B2 — Data (a profile file per mode/perspective)**, loaded at startup.
  *Reuse:* the scenario/asset loading already in place. *Build:* a schema + loader + validation.
  *Cost:* a new asset kind; drift between code ids and data ids must be caught.
- **B3 — Declarative attributes**, like `[GizmoProjector]`, discovered by the existing Roslyn generator.
  *Reuse:* ⭐ a mechanism **already proven in this repo** for gizmos. *Build:* extend the generator.
  *Cost:* compile-time only — no runtime/per-deployment variation.

> **Claude's lean: B1 now, B2 later** — with the ids designed so the move is mechanical. The requirement
> today is *per-mode*, and modes are compile-time. B3 is tempting because the generator exists, but a
> profile is a *host* concern, not a *type* concern, and attributes put it on the wrong object.

## Q26-C — Replace or wrap the int action ids?

`GlobalActionIds` are `int` and **cross DDS** (ExCon → IG).

- **C1 — Wrap.** Keep the int ids as the wire/execution vocabulary; `IEntityAction` carries one.
  *Reuse:* total; no protocol change. *Cost:* two id spaces coexist forever.
- **C2 — Replace with string ids**, mapping at the network boundary.
  *Reuse:* less. *Build:* a boundary map. *Cost:* protocol-adjacent risk for a cosmetic gain.
- **C3 — Replace outright**, changing the wire format.
  *Cost:* breaks ExCon↔IG compatibility. Rejected unless the architect sees a reason.

> **Claude's lean: C1.** The int ids are working infrastructure with a network contract; the problem
> being solved is authoring ergonomics, which does not require touching the wire.

## Q26-D — Is *perspective* the right profile key?

The user said the menu *"needs to change with the subsystem-derived perspective (cgf/editor/simhost/ig)"*.
But the two concepts are not the same thing — see
[§5b](UX_Current_UI_Architecture.md#5b-how-perspective-switching-actually-works):

| | Perspective | Mode |
|---|---|---|
| Scope | a window-set filter, switchable at runtime | fixed for the process (`--mode`) |
| Today | 10 exist; 5 are cluster roles, 4 are the editor's internal graphs | 5 UI modes |
| Note | the editor's BTree/HSM/Blueprint perspectives are **not** subsystems | `editor` cannot combine with the others |

- **D1 — Key on perspective.** Matches the user's words; lets the editor's graph perspectives present
  different actions too.
  *Cost:* perspectives are emergent from window registration, not declared — keying on an emergent set is
  fragile. **Fix by declaring the perspective set** (also fixes the restore bug).
- **D2 — Key on mode.** Stable and declared.
  *Cost:* cannot vary within the editor between Scenario and Blueprint — probably wanted eventually.
- **D3 — Key on both**: mode selects the profile, perspective refines it.
  *Cost:* two axes to reason about.

> **Claude's lean: D3, implemented as D1 over a *declared* perspective set.** In practice mode and
> perspective coincide for the cluster roles, and the editor's internal perspectives are exactly the case
> D2 cannot express. ⚠ **This requires making the perspective set explicit rather than emergent** — which
> Stage 3 needs anyway to fix the silent restore bug.

## Q26-E — Is Stage 0 acceptable as a delete-only batch?

~1,800 lines of dead UI, including the `Hrot.UI.Common` project that **builds nowhere while owning the
namespace the live panels declare**.

- **E1 — Yes, ship deletion alone**, gated on a green build and suites, before anything else.
- **E2 — Fold deletions into the stage that touches each file.**
  *Cost:* the namespace trap stays live for months, and every later stage risks editing the dead copy.

> **Claude's lean: E1, emphatically.** The trap has even odds of wasting a session's work, and a
> delete-only batch is the cheapest thing in this plan to review and revert.

## Answers

*To be filled in after the architect round. Then update the matching
[UXD rows](UX_Design.md#3-design-decisions-uxd) and unblock the stages in
[UX_Task_Tracker.md](UX_Task_Tracker.md).*

| Question | Decision | Notes |
|---|---|---|
| **Q26-A** — one vocabulary, how far | — | *lean A1* |
| **Q26-A′** — ORBAT in stage 2? | — | *including it collapses a 434-line fork* |
| **Q26-B** — where a profile lives | — | *lean B1 now, B2 later* |
| **Q26-C** — replace or wrap int ids | — | *lean C1; the ids cross DDS* |
| **Q26-D** — perspective or mode as key | — | *lean D3 over a declared perspective set* |
| **Q26-E** — delete-only batch | — | *lean E1* |
