# Feature design — making a tool a thing

> **Design for [UXI-07](UX_Issues.md#uxi-07) 🔴 · drafted 2026-08-10.**
> **Status: ✅ designed — [Q27](Architect_Question_27_Tool_Model.md) answered by the user, 2026-08-10.**
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

## The resolved shape — ✅ all five Q27 questions ruled by the user, 2026-08-10

### ⭐ `gizmo ≠ tool` — and the distinction is **already encoded**, just not enforced

> **User:** *"Many tools are modal per subsystem, i.e. up to one currently active tool requiring focus —
> like rotate entity. But a tool can be modeless, i.e. permanent until turned off — for example one that
> renders an info box for a given entity with its own buttons. Note a gizmo is not equal to a tool: some
> gizmos are stateless, showing status per entity (health bar); many such can be active per entity."*

| Category | Existing encoding | Live examples | How many active |
|---|---|---|---|
| **Not a tool** — status draw | `IStatelessGizmo { void Draw(); }` | `RouteGizmo`, `RubberBandGizmo`, health bars | many, per entity |
| **Modeless tool** | `IEntityStatefulGizmo`, `RequiresExclusiveFocus => false` | `LayerControlGizmo`, `EntityDragGizmo` | several, concurrently |
| **Modal tool** | `IEntityStatefulGizmo`, `RequiresExclusiveFocus => true` | `EntityRotatorGizmo`, `EntityPickerGizmo`, `PointSequenceGizmo`, `MeasureGizmo` | 🔒 **at most one per subsystem** |

`RequiresExclusiveFocus` is declared on `IGizmoInteractionHandler:19`. ⇒ **The taxonomy exists; the
enforcement does not** — each engine consults the flag only within itself, which is the 🔴 defect above.

### The descriptor — registration-time flags, per the ruling

> **User:** *"Tool handling should be defined by registration-time flags. No less flexibility than now."*
> And: *"Tools do not necessarily need to be shown on the toolbar — this must be optional."*

```csharp
public enum ToolModality { Modal, Modeless }

public sealed record ToolDescriptor(
    string       Id,                          // its own vocabulary — NOT a GlobalActionId
    string       Label,
    ToolModality Modality,
    bool         ShowOnToolbar       = false, // 🔒 optional by default (user ruling)
    bool         ToggleOnReactivate  = false);// 🔒 re-activating does NOT cancel unless flagged
```

> ### ⚠ Corrected 2026-08-10 — `SurvivesActions` was on the wrong object
>
> An earlier revision put `SurvivesActions` on the **tool**. **Wrong.**
>
> > **User:** *"`SurvivesActions` can't be a tool property — it must be driven by **focus changes only**.
> > Actions might need flagging if they **steal focus**."*
>
> ⇒ the flag moves to the **action**, because the action is what causes the effect:
>
> ```csharp
> // EntityActionDescriptor — UXI-03. Additive.
> bool StealsFocus = false;   // true ⇒ dispatching it cancels the active modal tool
> ```
>
> 🔒 **Cross-issue:** this adds one field to
> [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s descriptor. Recorded there too.
> [Corrections 20](UX_Tasks_Detail.md#corrections).

### The controller — one per subsystem (B1)

```csharp
public interface IToolController
{
    ToolDescriptor?                     ActiveModal    { get; }   // at most one
    IReadOnlyCollection<ToolDescriptor> ActiveModeless { get; }   // several
    void Activate(string toolId, Entity? target = null);          // target is an argument, not a taxonomy
    void Cancel();                                                // Escape → cancels ActiveModal
    event Action<ToolDescriptor?> ActiveModalChanged;             // toolbar/status bind here
}
```

**Rules, each traceable to a ruling:**

| Rule | Source |
|---|---|
| ⭐ **Focus is the only currency.** A modal tool holds focus; whatever takes focus displaces it | user, 2026-08-10 — *"driven by focus changes only"* |
| Activating a modal tool takes focus ⇒ cancels the current modal tool | C — *"up to one currently active tool requiring focus"* |
| **Re-activating the current modal tool does NOT cancel it** — no-op, or re-target when a different target is supplied. Toggle only if `ToggleOnReactivate` | 🔒 user, 2026-08-10 — *"if toggle behaviour is required it must be set via flag"* |
| An action cancels the modal tool **only if it is marked `StealsFocus`** — a *"recenter map"* shortcut fired mid-tool must leave the tool armed | 🔒 user, 2026-08-10 |
| Modeless tools coexist and toggle independently | C — *"permanent until turned off"* |
| Stateless gizmos are untouched | C — *"a gizmo is not equal to a tool"* |
| **An action activates a tool; a tool is not an action** | D — two vocabularies, one relationship |
| Escape is centralised **for modal tools**; gizmos keep their own cleanup | E — *"centralize for modal tools, keep local where necessary"* |
| 🔒 An **unfocused** subsystem's modal tool stays armed but consumes no input | B — *"perspective switch often means focus switch to another subsystem"* |

### ✅ No sub-decisions left

The previous open item (`SurvivesActions`'s default) is **void** — the flag moved to the action and its
default is `false`: an action leaves the active modal tool alone unless it declares that it steals focus.
That is both the safe default and the one that preserves today's behaviour, so the dilemma disappears.

### A1 without A3 — the condition

> **User:** *"A1 if doable without the A3 intermezzo, but no problem with A3 first if it helps."*

**A1 alone fixes the 🔴 defect *iff* every modal activation routes through the controller.** The bypass
routes are `EditorMapPickAdapter`, `EditorZoneAdapter` and `EditorSpawnAdapter`, which call
`GlobalGizmoManager.Register` directly — and their gizmos are `RequiresExclusiveFocus => true`, i.e.
genuinely modal. ⇒ **Convert those in the same change and A3 is unnecessary. If they cannot be, do A3
first**, because until then a bypassing picker can still fight the controller for the same drag.

### What the toolbar becomes

`ActiveModal` + `ActiveModalChanged` make [UXR-84](UX_Requirements.md#uxr-84) fall out — but only for
tools that opted in via `ShowOnToolbar`. ⇒ **`Select` becomes the null modal tool** (a real state, not a
dead case), and `Measure`/`Rotate` may stay button-less by choice rather than by omission.

## Migration

| Step | Change | Gate |
|--:|---|---|
| 1 | `ToolDescriptor` + `IToolController`; **Editor only**; register the 6 existing tools with their real modality (`Select` = null modal tool) | nothing calls it yet |
| 2 | Route the **toolbar event path** (`ActivateEditorToolEvent` switch) through `Activate()` | every tool behaves as today |
| 3 | Route the **action path** (`GlobalActionIds.Rotate/EditOverlay/EditRoute`) through `Activate()` — 🔴 **deletes the duplicated toggle logic**, the D′ idiom | context-menu activation identical |
| 4 | Convert the three bypassing adapters (`EditorMapPickAdapter`, `EditorZoneAdapter`, `EditorSpawnAdapter`) | ⭐ **completes A1 — the 🔴 two-arbiter defect closes here** |
| 5 | Toolbar binds `ActiveModalChanged`; opt tools in via `ShowOnToolbar` | [UXR-84](UX_Requirements.md#uxr-84): active tool visibly active |
| 6 | Central Escape → `Cancel()`; gizmos keep their own cleanup | Escape cancels the modal tool from anywhere |
| 7 | Repeat for SimHost / CGF | same descriptors, host-bound activation |

⚠ **Steps 1-4 are behaviour-preserving except for the bug fix.** Step 5 is the first visible change.

🔒 **Step 4 is not optional.** Until those three adapters route through the controller, a picker can still
fight the controller for the same drag — that is precisely the condition under which
[Q27-A](Architect_Question_27_Tool_Model.md#answers) says A3 would be needed first.

## Sequencing against the rest

⚠ **Not on the critical path.** [UXI-01](UX_Feature_DeadUI_Removal.md) /
[UXI-02](UX_Feature_HalfBuilt_Decisions.md) / [UXI-06](UX_Feature_Perspective_Restore.md) are cheaper and
independent.

⭐ **But steps 1-4 close a 🔴 correctness defect**, which argues for doing them earlier than the toolbar
work they enable.

⚠ **Touches [UXI-02](UX_Feature_HalfBuilt_Decisions.md):** that design proposed deleting the dead
`EditorTool.Select` button. **Supersede it** — `Select` becomes the *null modal tool*, a real state.
