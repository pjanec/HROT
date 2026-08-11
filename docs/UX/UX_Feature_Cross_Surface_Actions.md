# Feature design — the same actions on every surface

> **Design for [UXI-04](UX_Issues.md#uxi-04) · drafted 2026-08-10.**
> **Status: ✅ designed — ready to break into `UXT` tasks.**
>
> Implements [UXR-85](UX_Requirements.md#uxr-85). Builds directly on
> [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) — the registry it extends to two more surfaces.
> Stage 2 of the [Cleanup Path](UX_Cleanup_Path.md); ORBAT is in scope here by architect answer Q26-A′.

## 0. Prior art — ✅ checked before designing ([rule 6](UX_Issues.md#rules))

| Exists? | What | Verdict |
|:--:|---|---|
| ✅ | **`SharedOrbatPanel` + `IOrbatController` + `IOrbatDataProvider` + `OrbatNodeViewModel`** | ⭐ **adopted by BOTH** — `EditorOrbatAdapter` and `ExConOrbatAdapter` each implement *both* interfaces. The ORBAT seam is **not** missing |
| ✅ | The whole **map menu round-trip** — emit → bind → hit-test → render → dispatch | traced end to end below; shared by every map host |
| ✅ | `MapContextActionController` (`Hrot.Presentation/Menus/`) | **0 consumers.** Written for exactly this, never wired |
| ❌ | `IHierarchyAdapter` | **0 implementers, 0 references** — the name appears only in its own file. ⚠ *Correcting my own published claim of "1 implementer"* ([Corrections 15](UX_Tasks_Detail.md#corrections)). **Not the ORBAT seam** — `IOrbatDataProvider` already is |

## ⭐ The finding that decides the ORBAT half

I expected *"ExCon runs a 434-line fork that the shared panel makes redundant."* **Wrong — and the real
reason is better.** Compare what each ORBAT actually offers on right-click:

| Panel | Items | Lines |
|---|---|--:|
| **ExCon `OrbatPanel`** `Panels/OrbatPanel.cs:284-315` | Select entity · Center on entity · Delete · *(if simulated)* Edit Route · Abort Mission | **434** |
| **`SharedOrbatPanel`** `Hrot.Presentation/Panels/SharedOrbatPanel.cs:115-121` | **Disembark.** That is the entire menu | 183 |

> ### ⇒ ExCon does not keep its fork out of inertia — **the shared panel is impoverished, not redundant.**
>
> The fork survives because migrating to the shared panel would *lose four of five menu items*. So
> *"collapse the 434-line fork"* is not a deletion task; it is **"give the shared panel the shared
> menu"** — which is precisely [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s registry. The fork
> then collapses as a **consequence**, not as an effort.

⚠ **All four ORBAT panels use raw ImGui** — none uses `IContextMenuBuilder`. That is the seam to insert.

## 🔴 The constraint that shapes everything: ORBAT is `int`, not `Entity`

| Layer | Identity |
|---|---|
| `OrbatNodeViewModel` | `int EntityId` — `Models/OrbatNodeViewModel.cs` |
| ExCon `OrbatNode` | `int EntityId`, sourced from **`IDerRepo`** — `ExConOrbatAdapter` has **no `Fdp.Core` import at all** |
| `EditorOrbatAdapter` | holds `Dictionary<int, Entity> _indexCache` internally, **projects `int` outward** |

**This is the same shape that blocked UXI-03's existing seam** (`IEntityActionController`'s `long
entityId`). It is not an accident: 🔒 **ExCon has no ECS world**, so it can never produce an
`EntityActionContext` carrying an `Entity`.

> ### The resolution — the descriptor/binding split, working across a bigger gap than it was designed for
>
> | Tier | |
> |---|---|
> | **Declaration** | one `EntityActionDescriptor` set — identity, label, group, order. Surface-neutral |
> | **Binding, ECS hosts** (Editor · SimHost · CGF · IG) | UXI-03's `EntityActionRegistry`, reached through an `int → Entity` resolver the host already owns (`NetworkEntityMap`, or the Editor adapter's `_indexCache`) |
> | **Binding, ExCon** | its **`IOrbatController` / `IExConLogic` facade**. Same descriptors, DER implementation |
>
> ⇒ ExCon consumes the shared *vocabulary* without ever seeing an `Entity`. **The split earns its keep
> here** — this is the case that would have forced a rewrite under any single-implementation design.

## The map half — the path exists; two hosts are simply not on it

**Traced hop by hop:**

```
[GizmoProjector(NetworkIdentity)] ContextMenuProjectorGizmo   → picks 1 of 4 pre-serialised JSON strings
  → draw.DrawContextMenuBinding(networkId, json)              ContextMenuProjectorGizmo.cs:125
  → DebugPrimitiveBuffer writes a ContextMenuBinding          DebugPrimitiveBuffer.cs:378
  → DebugGizmoLayer.HandleInput hit-tests on right-release    GizmoMap…/DebugGizmoLayer.cs:196-241
  → ContextMenuAdapter.Schedule / DrawScheduled → ImGui       ContextMenuAdapter.cs:42-156
  → GizmoMenuActionEvent{AnchorId, ActionId}                  Fdp.Presentation/…/DebugGizmoLayer.cs:128-138
  → ContextActionIngressSystem → GlobalActionRequestedEvent   ContextActionIngressSystem.cs:60-71
  → GlobalActionDispatchSystem → the host's handler
```

### 🔴 SimHost and CGF have **no per-entity map menu at all** — and the cause is precise

Both register `Hrot.Presentation.Gizmos.GizmoRegistrar` (which carries `CanvasContextMenuGizmo`, so
**empty-space** right-click works) but **neither calls `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar`**,
which is where `ContextMenuProjectorGizmo` lives — `SimHostApp.cs:337-345`, `CgfSubsystem.cs:497-500`.

⭐ **Why one registrar call is the whole difference:** `GizmoRegistrarGenerator` emits one registrar
**per namespace, per assembly**, from that assembly's own syntax trees only
(`GizmoRegistrarGenerator.cs:52-144`). A gizmo in `Hrot.Common` is invisible to a subsystem that does not
name that registrar. This is the mechanism behind [UXI-22/23](UX_Issues.md#uxi-23).

⚠ **But adding the call is the wrong fix.** `ContextMenuProjectorGizmo`'s JSON is hardcoded and
IG-flavoured — *Move Here · Engage · Stop*. SimHost and CGF would inherit IG's menu, not their own.

### ⇒ Replace the hardcoded projector with a registry-backed one

One gizmo, host-specific content: serialise **the host's registry items for that entity** instead of a
pre-baked string. Same emit → bind → dispatch path, unchanged.

## 🔒 The principled exception — the map cannot carry every action

The map round-trip is **id-based**: the click returns `GizmoMenuActionEvent{AnchorId, ActionId}`, so the
handler must be reachable from an **`int` action id**.

| Surface | Can carry |
|---|---|
| **Inspector · ORBAT** | any descriptor — items are invoked in-process, so an `execute` closure works |
| **Map** | ⚠ **only actions with a `GlobalActionRegistry` binding.** A closure-only or `Selection`-mode item cannot round-trip an id |

⇒ *Mark Target for N Units* (selection-scoped, `async` map-pick) **will not appear on the map menu**, and
that is correct rather than a gap. [UXR-85](UX_Requirements.md#uxr-85) already allows it: *"the same
action set, **minus any a surface genuinely cannot offer**"*.

🔒 **This makes the `GlobalActionIds` binding the price of map presence** — a clean, checkable rule, and
it is why Q26-C1 (build **on** the registry) was the load-bearing ruling.

## Migration

| Step | Change | Gate |
|--:|---|---|
| 1 | `IContextMenuBuilder` adapter for the ORBAT row — replaces raw `ImGui.MenuItem` in `SharedOrbatPanel` | menu unchanged (still Disembark) |
| 2 | Back the shared ORBAT menu with the registry via an `int → Entity` resolver; **Editor first** | Editor ORBAT gains Center/Select/Delete/Rotate |
| 3 | Bind ExCon's descriptors to `IOrbatController`; retire `OrbatPanel` | ExCon ORBAT items **identical** before/after |
| 4 | Registry-backed map gizmo, replacing `ContextMenuProjectorGizmo`'s static JSON — **Editor first** | Editor map menu unchanged |
| 5 | Register it in SimHost and CGF | ⭐ first per-entity map menu those hosts have ever had |

**Cross-surface gate:** right-click one entity on the map, in the inspector, and in ORBAT — the item sets
match except for map-only exclusions, which must be *explainable by the id rule*, not by omission.

## 🔒 Out of scope

| | Why |
|---|---|
| IG's DDS-authored menu | ruled a separate pipeline (Q26-A2) |
| Unifying `Delete` handlers | user ruling — divergence is structural |
| `IHierarchyAdapter` | **0 implementers, and not needed** — `IOrbatDataProvider` already carries the hierarchy. ⇒ file as dead code, not as this design's seam |
| ExCon's DER→ECS migration | not required; the facade binding is the point |

## Risks

| | |
|---|---|
| ⚠ `DrawContextMenuBinding` is a **default no-op** on `IDebugDrawBuilder` (`IDebugDrawBuilder.cs:140`) | a builder that does not implement it drops menus **silently**. Assert in a test, not by eye |
| ⚠ Serialising the registry per frame | `ContextMenuProjectorGizmo` pre-serialises for a reason. Build the JSON **on right-click**, not every `Draw` |
| ⚠ ExCon's `IContextMenuBuilder` flattens submenus (`JsonContextMenuBuilder`) | any grouped ORBAT item degrades there. Known from Q26-A″ |
| ⚠ Step 5 changes SimHost/CGF behaviour visibly | it is new capability, not a refactor — call it out in review |

## Two dead files found while designing this

`EditorOrbatPanel` (27 L) and `EditorOrbatWindow`: the panel is constructed at `EditorSubsystem.cs:1559`
and **read nowhere**; the window is **never registered**. The Editor's live ORBAT is `SharedOrbatPanel`
(`:3580`).

> 🔴 **[Correction 5](UX_Tasks_Detail.md#corrections) gets worse.** The programme's stated root cause —
> *"no right-click affordances on objects"* — was generalised from `EditorOrbatPanel`. That panel
> **never reaches the screen.** The claim was not merely over-generalised from one file; it was
> generalised from a **dead** one.

⇒ Add both to [UXI-01](UX_Feature_DeadUI_Removal.md)'s delete list. They sit in `Hrot.Editor`, so its
20-file `Hrot.UI.Common` sweep does not cover them.
