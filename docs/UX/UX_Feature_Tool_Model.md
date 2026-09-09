<!--STATUS
state: LIVE
build-state: NOT-BUILT
verified: 2026-09-09 (PREMISE SWEEP - all 13 premises re-tested against source; see 0b)
current-answer: NOT-BUILT (design only; Q27 answered). No IToolController/ToolDescriptor/modal-stack in
  source. >>> READ 0b FIRST <<< - the premises SURVIVE, but four moved and three are now WIDER than this
  document says, because CE-051/CE-061/UXI-23-S2b reworked this exact area after the 2026-08-28 scan.
known-rot: the line citations in the body are pre-CE-051 and mostly MOVED. 0b carries the current ones.
  Specifically: the idiom table's "EditorSubsystem.cs:3806-3894" is now the shared ToolActivationDrainSystem;
  "EditorSubsystem.cs:1122-1134" (the two-arbiter construction) is now MapInteractionPack.cs:92-100 and
  therefore structural on FIVE hosts, not one; EditorSpawnAdapter no longer exists (it is the SHARED
  ScenarioSpawnAdapter); and the D-prime duplicate has a SECOND instance on IG that this document never named.
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

## 0b. ⭐⭐⭐ PREMISE SWEEP — **`2026-09-09`, all 13 premises re-tested against source**

> ⛔⛔ **Why it was owed.** This document's previous `verified:` date is `2026-08-28`. **After** it,
> `CE-051` *(Axis-C `E3` — the viewport interaction went shared)*, `CE-061` *(Axis-C `E5` — the
> Scenario windows + a CGF spawn adapter)* and `UXI-23` `S2b` *(`MapInteractionPack`)* reworked
> **exactly this area**. ⇒ 🔒 the design's rulings are user rulings and do not decay — ⛔ **but its
> LINE CITATIONS are state claims, and `§M` says those rot.**
>
> ⭐⭐ **VERDICT: the design SURVIVES INTACT — every premise still holds.** ⚠ But **four moved** and
> ⭐⭐⭐ **three are now WIDER than the body says**, in ways that change the migration's economics.

| # | premise | verdict | measured `2026-09-09` |
|--:|---|:--:|---|
| **1** | prior art empty — `ITool`/`ToolDescriptor`/`ToolRegistry`/`ToolController`/`ActiveTool` = 0 types | ✅ **HOLDS** | `search_graph` + grep agree: **0**. ⭐ And the code now **says so deliberately** — `Hrot.Common/Editor/EditorTool.cs` header: *"There is deliberately still no `ITool`/`ToolManager` registry — inventing a registry is not what `E3` is for."* ⇒ `E3` **declined** this scope; it did not absorb it |
| **2** | 🔴 two exclusive-focus arbiters, one bus, no arbitration | ✅ **HOLDS — and is now WIDER** | ⛔⛔ **MOVED and became STRUCTURAL.** The construction is no longer `EditorSubsystem.cs:1122-1134`; it is **`MapInteractionPack.cs:92-100`**, which hands the **same `bus`** to `GlobalGizmoManager` and `DataDrivenGizmoSystem` — ⇒ **on all FIVE hosts, by construction.** ⚠ It was an editor composition accident; it is now a guaranteed property of the shared pack. ⭐ The per-arbiter guards are unchanged: `DataDrivenGizmoSystem.cs:91` · `GlobalGizmoManager.cs:66` |
| **3** | terminal half — first `InputCaptureBinding` wins, no arbitration | ✅ **HOLDS** | `DebugGizmoLayer.cs:121-135` — the loop still `break`s on the first match |
| **4** | exclusivity is only per-`Entity` | ✅ **HOLDS** | `DataDrivenGizmoSystem.cs:74` `_injectedGizmos` is still `Dictionary<Entity, …>` |
| **5** | six activation idioms | ✅ **HOLDS — MOVED** | ⭐ Idioms **A/B/D/E/F migrated wholesale into the shared `ToolActivationDrainSystem`** (`Hrot.Presentation/ScenarioEditor/Systems/`), registered by `ScenarioEditorModule` on **both** Editor and CGF. ⛔ `EditorSubsystem.cs:3806-3894` no longer holds the switch. ⭐⭐ **A real improvement `E3` delivered for free: the drain now REPORTS** *(`Unserviceable(tool, reason)`)* instead of failing silently |
| **6** | 🔴 **D′ — the toggle duplicated via `GlobalActionRegistry`** | ✅ **HOLDS — and there are now TWO instances** | ⛔ **Editor:** `EditorSubsystem.cs:1875-1912` *(`EditOverlay`/`EditRoute`, `HasInjectedGizmo` at `:1879`/`:1898`)* + `:1857` *(`Rotate`, the E/F shape)* — duplicating `ToolActivationDrainSystem.cs:176`. 🔴🔴 **AND IG, which this document never named:** `IgApplication.cs:3167` and `:3199`, the same `HasInjectedGizmo` toggle for `RoutePlan`/`EditablePolyline`, in `ActivateAreaEditingTool`. ⇒ ⭐ **step 3's blast radius is TWO hosts** |
| **6b** | — | ⭐ **PARTIALLY DONE ALREADY** | `GlobalActionIds.Measure` (`:1867`) and `PlaceEntity` (`:1871`) **already publish `ActivateEditorToolEvent`** instead of duplicating ⇒ **2 of 5 action-path routes are converted**; step 3 is the remaining 3 (`Rotate`, `EditOverlay`, `EditRoute`) × 2 hosts |
| **7** | no button can show active state | ✅ **HOLDS — restate the count** | ⛔ *"six bare `ImGui.Button` calls reading no state"* is now inaccurate: `EditorToolbarPanel.DrawContent` has **four TOOL buttons reading no state** *(`Select`, `Place Entity`, `Edit Shape`, `Edit Route`)* plus two **non-tool** buttons, one of which *(the mode toggle)* **does** read state. ⭐⭐ **And step 5 got cheaper:** the panel now has `BuildViewModel` → `EditorToolbarPanelViewModel`, a state projection that did not exist on `2026-08-28` ⇒ `ActiveModal` binds into an existing seam |
| **8** | `Measure`/`Rotate` have no toolbar button | ✅ **HOLDS** | absent from `DrawContent` |
| **9** | `Select` is a dead no-op | ✅ **HOLDS — MOVED** | now `ToolActivationDrainSystem.Execute`'s `case EditorTool.Select: break;` |
| **10** | the enum names four deleted classes | ✅ **HOLDS — MOVED** | the enum moved to `Hrot.Common/Editor/EditorTool.cs` (`CE-051`) and its doc comments **still** name `CreationTool` · `EditTool` · `RouteEditTool` · `MeasureTool` — **all four still 0 declarations** |
| **11** | Escape re-implemented per gizmo | ✅ **HOLDS** | **16 files** handle Escape themselves *(11 in-tree gizmos + `DebugGizmoLayer` + ExtDeps)*. ⚠ The body says *"8 gizmos"* — the true count is higher, so the premise is understated, not overstated |
| **12** | fossils of the deleted stack | ✅ **HOLDS** | `EditorMapPickAdapter.cs:26` still carries the **broken `<see cref="MapCanvas.PopTool"/>`**; `GizmoInteractionProxyTool.cs:16` still says *"optional exit callback instead of `MapCanvas.PopTool()`"* |
| **13** | three bypassing adapters call `Register` directly | ✅ **HOLDS — and step 4 got CHEAPER** | ⭐⭐⭐ **`EditorSpawnAdapter` NO LONGER EXISTS.** It is the **shared `Hrot.Presentation/Adapters/ScenarioSpawnAdapter`**, composed by **both** Editor (`EditorSubsystem.cs:2263`) and CGF (`CgfSubsystem.cs:1400`), still bypassing at `:153`, `:215`, `:271`. ⇒ **converting it once fixes two hosts.** The other two remain editor-private: `EditorMapPickAdapter.cs:73,109,~125` · `EditorZoneAdapter.cs:74` |

### ⭐⭐ Two mechanisms the `§0` prior-art table MISSED — **both are partial building blocks, not substitutes**

| found | what it is | bearing on the design |
|---|---|---|
| ⭐⭐ **`CancelInteractiveTools()` on BOTH arbiters** — `GlobalGizmoManager.cs:96` · `DataDrivenGizmoSystem.cs:124` | cancels the focused gizmo, calls `OnCancel()`, keeps permanent/modeless ones. **Driven from one place**: `GizmoExecutionController.cs:48-49` calls both **when the last terminal disconnects** | ⛔ **NOT a user-facing Escape** and not a stack — so *"no central cancel"* still holds for the user. ⭐⭐ **But `IToolController.Cancel()` should DELEGATE to these rather than invent a third teardown** — they already encode *"cancel the interactive, spare the permanent"*, which is exactly the modal/modeless split `Q27` ruled. 🔒 seam law |
| ⭐⭐ **`DebugGizmoLayer._activeTool`** — a live `GizmoInteractionProxyTool?` (`:28`), created at `:180`/`:187`, self-clears via `onExit`, and **handles Escape at `:266`** | the **frontend** routing tool that `gizmo-input-focus-design.md` §14 promised would survive | ⭐⭐⭐ **§14's proxy tool DID survive — what did not survive is that it is a SINGLE NULLABLE SLOT, not a stack.** ⇒ refines this document's §"the tool stack was deleted": the *routing* half is alive and correct; only the **LIFO depth** is missing. ⛔ Do not re-implement the proxy; the backend `IToolController` supplies the depth the frontend slot cannot |

### ⭐ What the sweep does NOT change

⭐ **Every `Q27` ruling stands** — they are user rulings, answered `2026-08-10` directly, and nothing measured
here touches them. ⭐ The **migration's 7 steps stand as written**; only their sizing moves: **step 3 doubles**
*(two hosts)*, **step 4 shrinks** *(the spawn adapter is now one shared conversion serving two hosts)*, and
**step 5 gets a seam it did not have** *(`EditorToolbarPanelViewModel`)*.

⛔ **Not measured, and stated as such:** whether the two-arbiter defect is *observable* at runtime on a real
cluster. 📐 The code path is proven; the **symptom** is not, and `RUNBOOK_Cluster_Debugging_Over_Http.md`
would be how to try. ⚠ That is the one row a reader should push on.

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
