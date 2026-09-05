<!--STATUS
state: LIVE
build-state: NOT-BUILT
verified: 2026-08-28 (coordinator source scan)
current-answer: NOT-BUILT (design only; Q27 answered). No IToolController/ToolDescriptor/modal-stack in source - only fossil comments.
-->
# Feature design — making a tool a thing

> **Design for [UXI-07](UX_Issues.md#uxi-07) 🔴 · drafted 2026-08-10.**
> 📐 **API: [UX_Interaction_API.md](UX_Interaction_API.md) · ✅ Acceptance: [UX_Interaction_UseCases.md](UX_Interaction_UseCases.md)**
> The API contract lives in the first — this doc keeps the
> evidence and the rulings; that one holds the types, the arbitration order and the threading model.
> 📐 **Architecture context: [`DESIGN_Map_Rendering_And_Interaction.md`](../DESIGN_Map_Rendering_And_Interaction.md)** —
> how the tool path sits inside the render/interaction stack, and §4.2's `stateDiagram` of the modal
> stack this issue builds.
> **Status: ❌ NOT-BUILT (design only; Q27 answered) — no `IToolController`/`ToolDescriptor`/modal-stack in source, only fossil comments.**
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

#### ⭐⭐ ADDED `2026-08-28` — **the TERMINAL half of the same defect: raw-input capture is decided by EMISSION ORDER**

📐 The section above is the **bus** side *(both systems ACT on the same typed stream)*. ⚠ **The raw-input
side is different, and worse in a quieter way.** Each arbiter also emits an `InputCaptureBinding` for its
own focus holder — `GlobalGizmoManager:138`, `DataDrivenGizmoSystem:334,373` — and the terminal resolves it
like this *(`GizmoMap.Presentation/Layers/DebugGizmoLayer.cs:118-134`)*:

```csharp
for (int i = 0; i < primitives.Length; i++) {
    if (prim.Shape != DebugPrimitiveShape.InputCaptureBinding) continue;
    if ((prim.ConditionMask & 1u) != 0) exclusiveAnchorId = prim.StructNetworkId;  // suppress hit-testing
    if ((prim.ConditionMask & 2u) != 0) routeRawInput      = true;                 // all raw HW to me
    captureToken = …;
    break;                       // 🔴 FIRST ONE WINS. No arbitration, no report.
}
```

⇒ 🔒 **When both arbiters hold "exclusive" focus, the one whose primitive lands FIRST IN THE BUFFER captures
raw input** — i.e. **whichever system runs earlier in the group**. ⛔ The other one still receives the typed
events *(the defect above)* but never the raw stream. ⇒ ⚠ **the two halves of one tool's input can end up
split across two tools**, and nothing anywhere says so.

📌 **The design predicted exactly this** *(`gizmo-input-focus-design.md` §6.2)*: *"If two tools
simultaneously emitted `InputCaptureBinding(Exclusive=true)`, the terminal would have no honest way to
choose. **We prevent that situation entirely on the backend**"* — ⛔ **and the prevention is what is
missing**, because there are two backends-within-the-backend.

⭐⭐ **Consequence for `A1`:** the single `IToolController` fixes BOTH halves at once — one arbiter means one
capture binding per frame, so the terminal's `break` becomes correct rather than arbitrary.

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
| **`EntityRotatorGizmo`** | ✅ **`Hrot.Presentation/ScenarioEditor/Gizmos/`** — ⚠⚠ **CORRECTED `2026-08-28`: this row said `Hrot.SimHost/Gizmos/` and that is STALE.** 📐 Measured: the only other copy is the `GizmoMap.Example` test bed. 📌 It was moved by *"AX item 4 — make `EntityRotatorGizmo` subsystem-agnostic"*, and the row was never updated ⇒ 🔒 **§M's rule exactly: a STATE CLAIM rots while the DECISION around it does not** | Editor, SimHost, **CGF** |

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
> bool CancelsModalTool = false;   // true ⇒ dispatching it clears the modal stack
> ```
>
> ⚠ **Renamed from `StealsFocus` 2026-08-10** — *"`StealsFocus` is now a misleading name; the flag should
> actually mean **cancel any running modal tool**"* (user). It never described focus acquisition; it
> described a consequence.
>
> 🔒 **Cross-issue:** this adds one field to
> [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s descriptor. Recorded there too.
> [Corrections 20](UX_Tasks_Detail.md#corrections).

### The controller — one per subsystem (B1)

```csharp
public interface IToolController
{
    ToolDescriptor?                     ActiveModal    { get; }   // = top of the stack, or null
    IReadOnlyList<ToolDescriptor>       ModalStack     { get; }   // bottom → top
    IReadOnlyCollection<ToolDescriptor> ActiveModeless { get; }   // several, unaffected by the stack

    void        Activate (string toolId, Entity? target = null);  // REPLACES the top
    IDisposable PushModal(string toolId, Entity? target = null);  // SUSPENDS the top; dispose pops & resumes
    void        Cancel();                                         // pops ONE level

    event Action<ToolDescriptor?> ActiveModalChanged;             // toolbar/status bind here
}
```

## ⭐ The modal tool **stack** — user requirement, 2026-08-10

> **User:** *"Sometimes in the middle of editing one thing we might need to temporarily jump to another
> and then back. A tool stack. I bet it was implemented."*

### ✅ It was — and it was deleted with the rest of the tool model

| Fossil | Evidence |
|---|---|
| `<see cref="MapCanvas.PopTool"/>` in a live doc comment — **a broken cref**; the member does not exist | `EditorMapPickAdapter.cs:26` |
| *"Uses an optional exit callback instead of `MapCanvas.PopTool()`"* | `GizmoInteractionProxyTool.cs:16` |
| `MapCanvas.KeyboardConsumedByTool` — a property named for the deleted model | `MapCanvas.cs:69` |
| *"tool-emitted primitives (written during `canvas.Update → ActiveTool.Draw`)"* | `EditorSubsystem.cs:1613` |

⚠ **Not recoverable from git** — `PopTool` appears only in comments even at the squashed import commit
(`e999566`), so the implementation lived in an ancestor repo. **The comments are the only survivors**, and
they are the fourth fossil of the same deleted architecture ([Correction 11](UX_Tasks_Detail.md#corrections)).

#### ⭐⭐ ADDED `2026-08-28` — **the replacement design EXPECTED the stack to survive** *(answers "intentional or accident?")*

📄 **[`docs/designs/gizmos-1/gizmo-input-focus-design.md`](../designs/gizmos-1/gizmo-input-focus-design.md)** —
the design of the very mechanism that replaced the tool model — **separates the two questions, and answers
them differently**:

| what | the design says | verdict |
|---|---|---|
| **single EXCLUSIVE focus on the backend** | §6.2: *"If two tools simultaneously emitted `InputCaptureBinding(Exclusive=true)`, the terminal would have no honest way to choose. **We prevent that situation entirely on the backend**"* — one `ActiveGlobalGizmo.ActiveInstance` slot | ✅ **INTENTIONAL, with a stated reason** |
| ⭐⭐⭐ **the frontend TOOL STACK** | §14: *"**The frontend keeps its tool stack for routing**, but stripped of business logic — the proxy tool remains as a generic input capturer that **pops itself** when the backend stops emitting the capture binding. No semantic decisions in the stack."* | 🔴 **the design assumed it would STILL BE THERE** |

⇒ 🔒 **So the loss was NOT a decision.** ⛔ Nothing in this repo records dropping the stack; the design of its
replacement **explicitly planned around keeping it**, stripped of semantics but present for routing.
⚠ **Stated fairly:** that document is marked *"Design proposal"*, and the deletion happened in an **ancestor
repo** — so this is *"no decision record exists, and the nearest intent says keep it"*, ⛔ **not** *"someone
decided to remove it."*

📌 **`R-137`** *(user, `2026-08-28`: unification may not cost a feature; if it does, put it back as
configuration)* **is the general form of what this section found on `2026-08-10`** — ⭐ the same disease,
recorded twice, eighteen days apart.

### 🔴 What replaced it is flat — and the "come back" is only half-implemented

`EditorMapPickAdapter`'s own comment still describes the old model — *"push tool → wire callbacks →
return `tcs.Task`; the cancellation handler calls `MapCanvas.PopTool`"* — but all three pick methods now
just call `_globalGizmoManager.Register(id, gizmo)`. **No LIFO, nothing saved, nothing restored.**

| Half of "jump away and back" | State |
|---|---|
| The **caller's control flow** resumes | ✅ `TaskCompletionSource` + `ct.Register` — works today |
| The **previous tool** resumes | ❌ **nothing restores it.** Interrupt a half-drawn route to pick a point and the route editor is not brought back |

### 🔑 Suspend ≠ deactivate — and the mechanism already exists

Both teardown paths **destroy** the gizmo:

```csharp
DataDrivenGizmoSystem.DeactivateGizmo(e)   // :102-114 → SetFocus(false); gizmo.Dispose();
GlobalGizmoManager.Unregister(id)          // :78-90   → SetFocus(false); gizmo.Dispose();
```

⭐ **But `SetFocus(false)` is a separate call that already precedes the `Dispose()`.** So *suspend* needs
no new gizmo API — it is **`SetFocus(false)` without the `Dispose()`**. Nothing calls it that way today;
that is the entire missing capability.

### Semantics

| | `Activate` | `PushModal` |
|---|---|---|
| **Current top** | cancelled and **disposed** | **suspended** — alive, unfocused, state intact |
| **On completion** | — | popped; the tool beneath **resumes with its state** |
| **Intent** | a deliberate switch — toolbar, menu | an **interruption** — pick a point, pick an entity |
| **Escape** | cancels it | pops **one** level, revealing the tool beneath |

⇒ **The caller chooses**, because only the caller knows whether it is switching or interrupting. The
descriptor cannot express that.

### It fits the existing async pattern exactly

```csharp
using var _ = tools.PushModal(ToolIds.EntityPicker);
int netId = await _pick.PickEntityAsync(ct);
// dispose → pop → the route editor beneath resumes, half-drawn route intact
```

That is today's `ct.Register(… Unregister …)` cleanup **plus the restore it is missing** — and it is what
*Mark Target for N Units* (`async void`, awaits an interactive pick) needs to stop stranding whatever was
running.

### ⭐ The stack and the focus rule are the same mechanism

The [B ruling](Architect_Question_27_Tool_Model.md#answers) requires an **unfocused subsystem's** modal
tool to stay armed but consume no input. That is *suspension* — of the whole stack rather than one
frame. 🔒 **One implementation serves both**, which is the strongest argument that suspend/resume belongs
in the controller rather than in each gizmo.

### ✅ Resolved — cancel is declarable, **suspend is not**

> **User:** *"Maybe another flag to just suspend the tool? But when to resume the tool again? The action
> handler would need to be async and resume on finish."*

**That question answers itself, and the asymmetry is the design:**

| | Expressible as a flag? | Why |
|---|:--:|---|
| **Cancel** | ✅ yes | a **point event**. Dispatch happens, the stack clears, nothing is owed afterwards |
| **Suspend** | ❌ **no** | it needs a matching **resume**, and only the handler knows when its work is done. A flag has no end |

🔒 **So there is no suspend flag.** Suspension is **scoped, not declared** — the handler takes it and
gives it back:

```csharp
// declarative: the descriptor says so, the handler need not touch the controller
new EntityActionDescriptor(..., CancelsModalTool: true)

// scoped: no flag; the handler owns the suspension for exactly as long as it needs it
async Task Execute(EntityActionContext ctx) {
    using var _ = ctx.Tools.PushModal(ToolIds.LocationPicker);
    var pt = await ctx.Pick.PickLocationAsync(ctx.Cancellation);
    ...
}   // dispose → pop → the suspended tool resumes
```

⇒ **`await` *is* the resume point.** The user's own objection — *"the handler would need to be async"* —
is the mechanism, not an obstacle.

> ### ⭐ And this settles the "is the flag redundant?" question I raised last turn — **it is not**
>
> I suggested `StealsFocus` might be redundant because focus transfer is observable when a handler calls
> `PushModal`. **Wrong for the cancel case:** *Delete entity* fired while a route editor runs must cancel
> that editor, yet its handler **never touches the controller** — it just deletes. Only a declaration can
> express that. ⇒ the two mechanisms are **complementary, not alternatives**:
>
> | | Handler touches the controller? |
> |---|---|
> | `CancelsModalTool` | **no** — the action invalidates the interaction from outside |
> | `PushModal` scope | **yes** — the handler takes over the interaction and returns it |

⚠ **`CancelsModalTool` clears the *whole* modal stack**, not just the top. A flag cannot express "cancel
two levels", and the honest reading is that the action asserts the whole interaction context is stale —
resuming a route editor beneath a popped picker, on an entity the action just deleted, would be worse.

### 🔗 Action concurrency lives in [UXI-03](UX_Feature_Entity_Action_Vocabulary.md#1b-concurrency--borrowed-not-invented)

Async actions can overlap each other, which is a **dispatch** question, not a tool question. Resolved
there by borrowing established models — AutoCAD transparent commands, Blender modal operators, Qt
modality levels, the reactive merge/switch/exhaust/concat set — rather than inventing flags. ⭐ Notably,
an action with `CancelsModalTool = false` **is** AutoCAD's *transparent command*, arrived at
independently.

### ⭐ A second, independent reason `execute` must return `Task`

[Q26-B](Architect_Question_26_Entity_Action_Model.md) already wanted it so the host can observe failure,
closing [UXI-17](UX_Issues.md#uxi-17)'s two `async void` handlers. **The stack adds a second reason:
without `await`, there is no resume point.** Two unrelated arguments converging on one signature is the
strongest case in this design.

### ⚠ Two sub-questions the stack raises

| | |
|---|---|
| **Does a suspended tool still draw?** | Lean **yes — draw, do not interact.** A half-drawn route that vanishes while you pick a point, then reappears, reads as a bug; leaving it visible is what makes "come back" legible |
| **Is depth bounded?** | Nothing needs more than 2 today (tool → picker). Lean: no hard limit, but **log** beyond 3 — an unbounded stack is a leak, not a feature |


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
