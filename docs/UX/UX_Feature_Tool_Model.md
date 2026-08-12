# Feature design — making a tool a thing

> **Design for [UXI-07](UX_Issues.md#uxi-07) 🔴 · drafted 2026-08-10.**
> **Status: 🔒 BLOCKED — needs the [Q27](Architect_Question_27_Tool_Model.md) architect round.**
> This is the first issue in the programme that is **genuinely new architecture**, not adoption of an
> existing seam. Implements [UXR-81](UX_Requirements.md#uxr-81), [UXR-84](UX_Requirements.md#uxr-84).

## 0. Prior art — ❌ **empty, and that is the finding** ([rule 6](UX_Issues.md#rules))

| Searched | Result |
|---|---|
| `ITool` · `ToolDescriptor` · `ToolRegistry` · `ToolState` · `ToolController` · `ActiveTool` | **0 types repo-wide** |
| Anything holding "the current tool" | **nothing** — `IEditorLogic` has no such property (full-file read) |

⚠ **Every other design in this programme adopted a seam that already existed.** This one has none — so
the [seam-law prior](UX_Seam_Inventory.md) is genuinely absent here, and that is *why* it needs an
architect round rather than a recipe.

**But two *partial* mechanisms exist**, and they are the problem as much as the starting point:

| | Tracks | Scope |
|---|---|---|
| `DataDrivenGizmoSystem._focusedGizmo` (`:65`) + `_injectedGizmos` (`:74`) | which gizmo has raw input; per-`Entity` injections | entity-bound tools |
| `GlobalGizmoManager._focusedGizmo` (`:31`) + `_activeGizmos` (`:30`) | the same, independently | non-entity tools |

## 🔴 The defect that changes this issue's severity

**Two exclusive-focus arbiters share one event bus, and nothing arbitrates between them.**

```csharp
// EditorSubsystem.cs:1122-1134 — same bus into both
var interactionBus = new FdpEventBus();
_editorDataDrivenGizmoSystem = new DataDrivenGizmoSystem(..., interactionBus: interactionBus, ...);
_globalGizmoManager          = new GlobalGizmoManager(_gizmoBuffer, interactionBus, ...);
```

Each guards exclusivity **only within itself** — `DataDrivenGizmoSystem.cs:91`:
`if ((gizmo.RequiresExclusiveFocus || gizmo.WantsRawInput) && _focusedGizmo == null)`.

`FdpEventBus.Read<T>()` is a non-destructive `ReadOnlySpan<T>`, so both systems read **the same**
`GizmoMouseEvent`/`GizmoKeyEvent` stream every frame.

> ⇒ **`Rotate` (DataDriven) and `Measure` (GlobalGizmoManager) can both hold "exclusive" focus at once
> and both act on the same drag.** Not a smell — a correctness defect. 🔴

**And exclusivity is narrower still than that:** `_injectedGizmos` is keyed **per `Entity`**, so
activating `Rotate` on entity A then `Edit` on entity B leaves **both** alive.

## The rest of the evidence

### Six activation idioms — and two of them for the same tools in one class

| | Idiom | Tools |
|---|---|---|
| A | **No-op case** — `case Select: break;` (`EditorSubsystem.cs:3814-3816`) | `Select` |
| B | Enum → `ActivateEditorToolEvent` → switch (`EditorApplication.cs:189`, drained `:3806-3894`) | all 6 |
| C | Delegate to an adapter that calls `GlobalGizmoManager.Register` (`EditorSpawnAdapter.cs:81-150`) | `Spawn` |
| D | **Toggle** keyed on `HasInjectedGizmo` (`:3823-3869`) | `Edit`, `Route` |
| **D′** | 🔴 **the same toggle, duplicated verbatim**, reached via `GlobalActionRegistry` instead (`:1160-1197`) | `Edit`, `Route` **again** |
| E/F | Direct inject / bare `Register`, **no toggle guard** (`:3871-3893`, duplicated at `:1143-1151`) | `Measure`, `Rotate` |

⇒ `Edit`, `Route` and `Rotate` are each reachable through **two independent pipelines inside
`EditorSubsystem.cs`** — the toolbar's event path and the context menu's action path — with the logic
copy-pasted between them.

### Consequences a user can see

| | Evidence |
|---|---|
| **No button can show active state** — and *cannot in principle* | `EditorToolbarPanel.DrawContent` is six bare `ImGui.Button` calls reading no state ([UXR-84](UX_Requirements.md#uxr-84)) |
| **`Measure` and `Rotate` have no toolbar button at all** | reachable only via context menu |
| **`Select` is a dead button** | the enum's only no-op case |
| **Repeat-click means different things per tool** | `Edit`/`Route` toggle off; `Measure`/`Rotate` do not — a genuine inconsistency, not a reporting artefact |
| **Escape is re-implemented in 8 gizmos** | `EntityRotatorGizmo.cs:98`, `VertexEditGizmo.cs:184`, `RouteWaypointGizmo.cs:197`, `MeasureGizmo.cs:149`, +4. No central cancel, because there is no central state |
| **No keyboard shortcut activates any tool** | only `Ctrl+O`/`Ctrl+N` exist, for assets |

### The vocabulary is a fossil, and the tools are scattered

`EditorTool`'s own doc comments name `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool` —
**all four have zero declarations.** PACK2-E002 deleted them and converted the behaviour to gizmos
([Correction 11](UX_Tasks_Detail.md#corrections)). The enum is the surviving vocabulary of a deleted
architecture, and it is still the toolbar's contract.

| Tool gizmo | Lives in | Adopted by |
|---|---|---|
| `MeasureGizmo` · `RouteWaypointGizmo` · `VertexEditGizmo` | `Hrot.Presentation/ScenarioEditor/Gizmos/` | Editor, IG |
| **`EntityRotatorGizmo`** | ⚠ **`Hrot.SimHost/Gizmos/`** | Editor, SimHost, **CGF** |

⇒ Editor and CGF depend on a tool that lives **inside the SimHost subsystem**.

## Recommended shape — for the architect to confirm, not to rubber-stamp

Mirrors the [Q26 constraint 3](Architect_Question_26_Entity_Action_Model.md#-constraints-added-by-the-user-2026-08-10--these-bound-every-answer-below) ruling —
*"a tool descriptor is shared; its activation is host-bound"* — i.e. the same descriptor/binding split
that [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) uses for actions.

```csharp
// Shared, inert
public sealed record ToolDescriptor(string Id, string Label, ToolScope Scope);
public enum ToolScope { Modal, EntityBound }   // Measure/Spawn vs Edit/Route/Rotate

// Per subsystem — one arbiter, owning "current"
public interface IToolController
{
    ToolDescriptor? Current { get; }
    void Activate(string toolId, Entity? target = null);   // re-activating Current cancels it
    void Cancel();                                          // Escape routes here
    event Action<ToolDescriptor?> CurrentChanged;
}
```

**What it buys, mapped to the evidence:**

| Defect | Closed by |
|---|---|
| 🔴 two arbiters, one bus | the controller is the **single** owner of exclusivity; both gizmo systems become implementations it drives |
| two tools alive on two entities | `Current` is singular by construction |
| toolbar cannot show state | `Current` + `CurrentChanged` ⇒ [UXR-84](UX_Requirements.md#uxr-84) falls out |
| 6 idioms, 2 pipelines | one entry point; the context-menu action handler calls `Activate` instead of duplicating |
| inconsistent repeat-click | one rule, stated once |
| 8 Escape handlers | `Cancel()` — gizmos keep their own cleanup, the *policy* moves up |
| dead `Select` | `Select` becomes the **null tool** — a real state, not a no-op case |

## 🔒 What this design does **not** decide — see [Q27](Architect_Question_27_Tool_Model.md)

Five questions are genuinely open and materially change the shape. Listing them here so the design is not
mistaken for settled:

| | |
|---|---|
| **A** | Where the single arbiter lives — a new controller **above** both gizmo systems, or fold `GlobalGizmoManager` into `DataDrivenGizmoSystem` |
| **B** | Is `Current` per-subsystem or per-perspective? (co-running subsystems each have their own map) |
| **C** | Are `Modal` and `EntityBound` one concept or two? |
| **D** | Does `EditorTool` (Editor-only enum) survive, or become shared string ids like `GlobalActionIds`? |
| **E** | Do the 8 self-handled Escapes centralise, or does `Cancel()` merely delegate to them? |

## Sequencing

⚠ **Not on the critical path.** [UXI-01](UX_Feature_DeadUI_Removal.md)/[UXI-02](UX_Feature_HalfBuilt_Decisions.md)/
[UXI-06](UX_Feature_Perspective_Restore.md) are unblocked and cheap; UXI-07 waits for the architect.

⭐ **But 🔴 the two-arbiter defect is separable** and worth fixing on its own, ahead of the abstraction —
it is a correctness bug today, independent of whichever tool model is chosen.
