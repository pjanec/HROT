# Map-interaction parity baseline — Editor · SimHost · CGF

> **Inventory, 2026-08-10.** The acceptance checklist for [UXR-90](UX_Requirements.md#uxr-90) and the
> evidence base for [UXI-23](UX_Issues.md#uxi-23). Produced because the user noted there was likely
> *"more already supported stuff I can't recall now"* — so this aims at **exhaustiveness**, not depth.
>
> ⚠ **Inventory, not design.** Load-bearing claims re-derived by the orchestrating session; items the
> scan could not settle are listed under [needs a hand-check](#needs-a-hand-check).

## ⭐ The headline: one missing call explains most of the gap

`Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(...)` is called by **Editor, IG and
ReplayBrowser — and by neither SimHost nor CGF** (verified: 3 call sites repo-wide).

That single registrar owns **13 files**, and its absence is why SimHost and CGF have none of:

| | |
|---|---|
| `SelectionHighlightGizmo` | **no selection ring at all** |
| `ContextMenuProjectorGizmo` | **no entity right-click menu on the map** |
| `HealthBarGizmo` · `LineOfSightGizmo` · `VisibilityConeGizmo` · `NavigationTargetGizmo` · `SpatialGridGizmo` · `LayerControlGizmo` · `EntityRotationGizmo` | all absent |

⇒ **One line per host.** Neither needs a new project reference — both already consume types from
`Hrot.Common`.

## 🔒 A third axis, added by the user 2026-08-10 — component availability

> *"Not all map stuff is usable by all subsystems. SimHost and CGF differ a lot in what ECS components
> are available per entity — some are network-replicated, some are specific — so not all entity-data
> related gizmos are applicable everywhere. **The editor has all ECS components.**"*

⚠ **This corrects the classification below.** I measured *assembly* dependencies and concluded "zero gaps
are blocked". The real constraint is **which components exist on an entity in that host at runtime** —
which no `.csproj` check can see.

**So capability differences come from three places, not two:**

| Axis | Decided | Mechanism | Example |
|---|---|---|---|
| **Mode** | composition time | which registrations the host performs | SimHost has no place-entity tool |
| **Perspective** | runtime, switchable | a condition on the registration | a graph perspective hides map actions |
| ⭐ **Component availability** | **per entity, emergent from the data** | `[GizmoProjector(…)]` keys · `isApplicable: e => world.HasComponent<X>(e)` | a health bar needs a health component; absent ⇒ never drawn |

### ✅ The good news: the third axis needs no new mechanism

Applicability is **already** component-keyed — that is exactly what `[GizmoProjector(TypeA, TypeB)]` and
the `HasComponent<T>` guards in every context-menu lambda do. A gizmo whose components are absent simply
never fires; an action whose predicate fails simply never appears.

⇒ **"Register the shared set and let component presence decide" is safe**, and needs no per-host
allow-list for entity-data gizmos. It also explains the earlier structural finding from the data side:
**the Editor composes the most because it has every component.**

### 🔴 …and the hazard that follows, which the acceptance must handle

**Component absence and a wiring mistake look identical: nothing appears.**

Register `HealthBarGizmo` in CGF where no entity carries a health component and you see exactly what you
would see if you had wired it wrong. That is the codebase's own *"assert the effect, never the report"*
trap in a new form.

⇒ **[UXI-23](UX_Issues.md#uxi-23)'s acceptance must distinguish the two**, per capability:

| Verdict | How it is established |
|---|---|
| ✅ **wired and applicable** | the visual appears on an entity that carries the components |
| ⚪ **wired, not applicable here** | the components are **confirmed absent** in that host — not merely unobserved |
| ❌ **not wired** | the components are present and it still does not appear |

⚠ **A "⚪" claim requires positive evidence that the component is absent.** Without it the row is
untested, not passing.

## The gap, classified

**The important column is *why*.** Almost everything is category (i):

| | Meaning | Count |
|---|---|---|
| **(i) not wired** | the shared class exists and works in the Editor; the host simply never calls it | **effectively all of it** |
| **(ii) blocked** | an **assembly/service** dependency the host lacks | **zero identified** — `MissionPresentationGizmo` (needs `IGeographicTransform`) and `EntityEditorLabelGizmo` (needs `BehaviorRegistry`) are fine; both hosts hold those |
| **(ii-b) data-gated** ⭐ | the **components are not on the entity** in that host | ⚠ **not measured — added by the user 2026-08-10.** Applies to every *entity-data* gizmo below. See the third axis above |
| **(iii) not applicable** | genuinely outside the host's domain | SimHost's road/trajectory layers only |

## What SimHost lacks vs the Editor

Selection ring · rubber-band **visual** (the logic runs, nothing is drawn) · entity context menu **on the
map** · `Delete`/`Select`/`CenterOnEntity`/`EditOverlay`/`EditRoute` action ids (only `Rotate` and
`OpenLayerControl` are registered) · **all** authoring tools (measure, place, draw area, draw route,
vertex edit, waypoint edit) · `MapLayerAssignmentSystem` and the layer-toggle effect · grid, mission
presentation, labels, health bars · rename.

## What CGF lacks vs the Editor

**Everything SimHost lacks, plus** — and this is the deeper gap:

| | |
|---|---|
| `SelectionInteractionSystem` | **absent entirely** — no click-select, no box-select, no Delete key |
| `EntityDragGizmoDefinition` | no drag-move (SimHost *has* this) |
| `GlobalActionRegistry` + dispatch + ingress | **no scaffolding at all** — CGF's map is effectively read-only plus Rotate, and that only from the inspector |

⚠ A repo note (`.dev/_DONE/gizmos-1/headless-gizmos.md:218`) already flags registering the drag gizmo
*"for EditorSubsystem and CgfSubsystem just like it currently is for SimHostApp and IgApplication"* —
**a known, un-actioned TODO**, not a design choice.

## 🔑 Absent EVERYWHERE — not parity gaps, real feature gaps

**Do not plan these as "bring SimHost/CGF up to the Editor" — nobody has them.**

| Capability | Evidence |
|---|---|
| **Ctrl/Shift additive click-select** | `SelectionInteractionSystem.cs:80-83`: `// TODO(P2): read Raylib shift/ctrl state for multi-select. Phase 5 implements single-select only.` — click **always** clears and selects one |
| Frame/fit-all · fit-selection | no `FrameAll`/`FitAll`/`ZoomToFit`/`FitSelection` anywhere in map code |
| Select-all | no such action id exists |
| Duplicate entity | no action id, no code path |
| Hover highlight | no mechanism; SimHost has a `HoveredEntity` field nothing sets or draws |
| Outliner selection propagation | **no outliner exists** |
| World-space pan bounds | zoom is clamped (0.1–10); panning is unbounded in all three |

> ### ⚠ What this means for [UXR-91](UX_Requirements.md#uxr-91) (multi-select)
>
> **Multi-select *is* achievable today — but only by rubber-band.** The box-select logic
> (`_isBoxSelecting` / `ExecuteBoxSelection`) is in the shared system and runs wherever it is
> instantiated. What does not exist anywhere is **additive click-select**.
>
> ⇒ [UXI-24](UX_Issues.md#uxi-24) has a **prerequisite nobody had noticed**: with rubber-band as the only
> route to a multi-selection, a user cannot build a selection *incrementally*. Ctrl/Shift-click should be
> in scope with it, or the multi-select context menu will be reachable only by lasso.

## Parity runs both ways — what the Editor lacks

| | |
|---|---|
| **Right-click "navigate here"** — direct mission dispatch on the map | `SimHostVisualization.cs:290-339`. SimHost only; the Editor authors missions through panels and the route tools instead |
| Road-network overlay · live trajectory overlay | `SimHostRoadLayer`, `SimHostTrajectoryLayer` — legitimate live-simulation content **(iii)** |

## Cheapest wins — pure wiring, with a correct reference host

| Gap | The call | Reference |
|---|---|---|
| Selection ring, map entity menu, health bars, LOS, vis-cone, spatial grid | `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(...)` | `EditorSubsystem.cs:1099-1101` |
| Route / tactical-area / map-overlay projectors | `Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(...)` | `EditorSubsystem.cs:1095-1096` |
| Drag-move on CGF | `gizmoRegistry.Register(new EntityDragGizmoDefinition())` | `SimHostApp.cs:347` |
| Click/box select + Delete key on CGF | `new SelectionInteractionSystem(world, bus[, rubberBandState])` + `Tick(dt)` | `SimHostVisualization.cs:250` |
| Rubber-band **visual** on SimHost | `new RubberBandState()` + register `RubberBandGizmo` + pass the state into the ctor | `EditorSubsystem.cs:1285-1287` |
| Layer-toggle actually doing something | `RegisterGlobalSystem(new MapLayerAssignmentSystem())` | `EditorSubsystem.cs:967` |
| Action ids on SimHost | copy the `actionRegistry.Register(GlobalActionIds.X, …)` blocks | `EditorSubsystem.cs:1160-1217` |

⚠ These are **additive registrations against classes the host already links** — but see the hand-check
below before treating every one as a one-liner.

⚠ **And they divide into two kinds, which the third axis makes important:**

| Kind | Data-gated? | Rows |
|---|---|---|
| **Interaction machinery** — selection system, drag gizmo, rubber band, action registry, layer assignment | ❌ no — keyed on `SimTransform`/`SelectionState`, which every map host has | expect these to *work* once wired |
| **Entity-data gizmos** — health bar, line-of-sight, visibility cone, navigation target | ✅ **yes** — need domain components that may not be replicated to that host | wiring them may correctly produce **nothing** |

⇒ **Wire the first kind expecting a visible result; wire the second expecting to have to check the data.**

## <a id="needs-a-hand-check"></a>Needs a hand-check

| | |
|---|---|
| Do `Hrot.SimHost` / `Hrot.CGF` `.csproj` already reference everything the cheap wins need? | Probable — they consume types from both assemblies — **but not confirmed by a build** |
| `EntityPickerGizmo` (modal pick-an-entity) reachability for SimHost/CGF | not traced past `MapPickServiceBridge` construction |
| Does the Editor registering **both** `IgEntityPresentationGizmo` and `SimHostEntityPresentationGizmo` cause a double-draw? | still open — [UXI-19](UX_Issues.md#uxi-19) |
| CGF's own right-click behaviour beyond the inspector menu | not traced |
| Hi-DPI / multi-monitor camera-offset correctness | only the hardcoded offset was confirmed; scaling behaviour unchecked |
