# Feature design — map-interaction parity

> **Design for [UXI-23](UX_Issues.md#uxi-23) · drafted 2026-08-12.** Absorbs
> [UXI-22](UX_Issues.md#uxi-22) and UXI-04 step 5. **Status: ✅ designed — one decision open (§5).**
> Depends on [UXI-10](UX_Feature_Entity_Symbology.md) → [UXI-11](UX_Feature_Selection.md).

## 0. 🔴 The issue as filed is optimistic in one direction and pessimistic in another

> *"Mostly one missing registrar call per host."*

⚠ **Not one call** — and the deeper finding is that **there are two dispatch mechanisms**, not one
under-adopted mechanism.

| | Shared `GlobalActionRegistry` | IG's hand-written `switch` |
|---|---|---|
| Adopters | Editor **11** ids · SimHost **4** · ReplayBrowser **2** · **CGF 0** | **IG only**, 6 ids |
| Where | `EditorSubsystem.cs:1139-1258` etc. | `IgApplication.cs:2386-2400` → `ExecuteLocalContextAction` |

🔴 **IG — the richest map host — does not use the shared vocabulary at all.** Verified: zero occurrences
of `GlobalActionRegistry` in `IgApplication.cs`. It maps ids to local strings
(`CenterOnEntity → "IG_CenterOnEntity"`, `Delete → "IG_DeleteEntity"`, …) and forwards the rest to ExCon
as `ContextActionTriggered`.

⇒ ⭐ **Seam-law instance 15, and the sharpest yet**: the shared action registry is adopted by three hosts,
and the one host with the most map interactions runs a private fork.

## 1. The verified matrix

**21 ids defined** (`GlobalActionIds.cs:13-48`). Handlers registered in the shared registry:

| | Editor | SimHost | CGF | IG | ReplayBrowser |
|---|:--:|:--:|:--:|:--:|:--:|
| `CenterOnEntity` `Select` `Delete` | ✅✅✅ | — | — | ⚠ *fork* (Centre, Delete) | ✅ — — |
| `Rotate` | ✅ | ✅ | — | — | — |
| `Measure` `PlaceEntity` | ✅✅ | — | — | ⚠ *fork* (Measure) | — |
| `EditOverlay` `EditRoute` | ✅✅ | — | — | ⚠ *fork* (both) | — |
| `OpenLayerControl` | ✅ | ✅ | — | — | ✅ |
| `ToggleAiTrace` ×2 | ✅✅ | ✅✅ | — | — | — |
| **Total** | **11** | **4** | **0** | **0 shared / 6 forked** | **2** |

⚠ **[Correction 30](UX_Tasks_Detail.md#corrections):** [UX_Map_Parity_Baseline.md](UX_Map_Parity_Baseline.md)
states SimHost registers *"only `Rotate` and `OpenLayerControl`"* — it also registers **both AI-trace
toggles** (`SimHostApp.cs:384-389`, added a month before that inventory). SimHost has **4**, not 2.

### 🔴 Nine ids have **no handler anywhere in the repo**

`MoveHere · Engage · Stop · Properties · Teleport · Repair · Reinforce · Resupply · Transfer` — each has
only its definition plus a menu-item emission. They are emitted by `ContextMenuProjectorGizmo` and
`ExCon/Logic/ContextMenuLogic.cs` and handled **only by ExCon**.

⇒ **43% of the declared vocabulary is inert in every ECS host.** §5 is the decision.

### 🔴 And one item is already dead **on screen**

`CanvasMenuUpdateSystem` emits a hardcoded `[{"id":200,"label":"Measurement Tool"}]` and is registered by
**Editor, SimHost, CGF and IG** (`:1337`, `:457`, `:545`, `:872`) — but SimHost has **no `Measure`
handler** and CGF has **no dispatch pipeline at all**.

⇒ 🔴 **Right-clicking empty map in SimHost or CGF today offers "Measurement Tool", and clicking it does
nothing.** The register listed inert items as a *risk of activating menus early*; it is **already true**.

## 2. What each host is missing — measured

| | Registrar calls | `GlobalActionRegistry` | `SelectionInteractionSystem` | Drag | Rubber-band **visual** |
|---|:--:|:--:|:--:|:--:|:--:|
| **Editor** | 6 + 4 manual | ✅ 11 | ✅ | ✅ | ✅ |
| **ReplayBrowser** | 4 + 7 manual | ✅ 2 | ✅ | — | ✅ |
| **IG** | 6 | ⚠ fork | ✅ | ✅ | ❌ |
| **SimHost** | 2 + drag | ✅ 4 | ✅ | ✅ | ❌ |
| **CGF** | **2, nothing manual** | ❌ | ❌ | ❌ | ❌ |

⚠ **Rubber-band is subtler than "missing"**: `SelectionInteractionSystem` takes `RubberBandState?` as an
**optional** ctor arg. SimHost and IG pass `null`, so **box-select logic runs and nothing is drawn** —
a *blind* rubber band, not an absent one. Only Editor and ReplayBrowser draw it.

🔴 **CGF is missing `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar` entirely**, costing it the selection
ring, context-menu projector, health bars, layer control, rotation, LOS, vision cones, nav targets and
the spatial grid — in one absent call.

## 3. The design

### 3.1 🔒 One dispatch mechanism

Retire IG's `switch` into the shared `GlobalActionRegistry`. IG's six local behaviours become six
registered handlers; `HandleContextMenuActionById`/`ExecuteLocalContextAction` go away.

⚠ **IG keeps its ExCon forwarding** — that is a *fallback for unhandled ids*, not a dispatch mechanism,
and it stays as the registry's miss path.

### 3.2 🔒 One registration entry point — `MapInteractionPack`

```csharp
public static class MapInteractionPack
{
    public static void Register(MapInteractionContext ctx);   // every map subsystem calls exactly this
}
```

It performs, in one place, what five hosts do differently today: the four gizmo registrars, the action
registry + dispatch + `ContextActionIngressSystem`, `SelectionInteractionSystem` **with** a
`RubberBandState`, the rubber-band gizmo, the drag gizmo ([ruling 36](UX_RESUME_INTERACTION.md)),
`CanvasMenuUpdateSystem`, and the layer control.

🔒 **Per [the 2026-08-10 ruling](UX_Issues.md#uxi-23), all hosts share the FULL set** — differences are
data availability or host rules, never set membership. ⇒ a host that cannot service an action **binds it
and reports why**, rather than omitting it:

| | |
|---|---|
| ✅ **Membership is uniform** | the pack decides; the host does not curate |
| ✅ **Absence becomes impossible** | there is no "forgot to call registrar #3" |
| ⭐ **Applicability stays per-host** | via [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s descriptor predicates — *disabled with a reason*, never silently missing |

### 3.3 Binding the unbound

| Host | Work |
|---|---|
| **CGF** | the whole pack — registry, dispatch, ingress, selection, drag, rubber-band, the `Common.Diagnostics` registrar. ⚠ **Its edits must be legal first** — [UXI-29](UX_Feature_Authority_Aware_Writes.md) |
| **SimHost** | the missing 7 ids + `Common.Diagnostics` registrar + rubber-band **state** (one ctor argument) |
| **IG** | migrate the fork; gain the rubber-band visual |
| **ReplayBrowser** | ⚠ **read-only host** — binds the *inspection* subset; ⚠ its dispatch system is **never wired to an ingress**, so its 2 ids can never fire today (`:227` vs no `ContextActionIngressSystem`) |
| **Editor** | reference implementation; loses its bespoke wiring |

### 3.4 Per-selection actions — a finding, not this design's scope

| | |
|---|---|
| **Delete-selected** | ✅ exists — but as a **raw `Delete` key** in `SelectionInteractionSystem.cs:117-152`, **not** an action id. So per-entity and per-selection Delete are **two unrelated code paths** |
| **Centre-on-selected** | 🔴 **does not exist anywhere** — no id, no handler |

⇒ **[UXI-24](UX_Issues.md#uxi-24) owns this.** ⚠ But note the consequence for [ruling 29](UX_RESUME_INTERACTION.md):
the multi-delete confirmation must sit on the **key path**, which today bypasses the action vocabulary
entirely.

## 4. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 23.1 | Every map subsystem registers the **same action id set** — parameterised over all five; none may omit | H |
| 23.2 | 🔴 **No registered id is inert**: every id a host binds has a handler that runs | H |
| 23.3 | 🔴 **No menu item is emitted without a handler** — the *Measurement Tool* dead-click guard, per host | H |
| 23.4 | IG's six forked behaviours are reachable **through the shared registry**; the `switch` is gone | H |
| 23.5 | An id a host cannot service is **disabled with a reason**, never absent | H |
| 23.6 | Unhandled ids still forward to ExCon from IG — the fallback survives the migration | H |
| 23.7 | `SelectionInteractionSystem` is constructed **with** a `RubberBandState` in every host | H |
| 23.8 | CGF registers `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar` — ring, health bars, menus, layer control all present | H |
| 23.9 | ReplayBrowser's dispatch is **wired to an ingress**, or its ids are deliberately unbound with a reason | H |
| 23.10 | **CGF**: right-click an entity → the shared menu appears with live items | I |
| 23.11 | **SimHost · IG**: rubber-band is **drawn** while dragging | I |
| 23.12 | *Measurement Tool* works in every host that shows it | I |

**9 H · 3 I · 0 V.**

## 5. 🔴 One decision open — the nine orphan ids

`MoveHere · Engage · Stop · Properties · Teleport · Repair · Reinforce · Resupply · Transfer` have **no
handler in any ECS host**; only ExCon consumes them.

| | Option | Consequence |
|--:|---|---|
| **A** | **Bind them** in the pack | 9 new behaviours to specify — *"Engage"* is a real command, not a menu label |
| **B** | 🎯 **Keep them ExCon-only, and stop the ECS hosts emitting them** | menus shrink to what works; ⚠ the ids stay in the shared file, correctly, because ExCon is a legitimate consumer |
| **C** | Bind them as **disabled-with-reason** | honest, discoverable, no behaviour to write; ⚠ nine permanently greyed items is clutter |

**Lean: B**, with C for any id that *should* eventually work locally. ⚠ **This needs your ruling** — it is
the difference between a map menu that offers 21 things and one that offers 12.

## 6. 🔒 Out of scope

| | |
|---|---|
| Multi-select fan-out and per-selection actions | [UXI-24](UX_Issues.md#uxi-24) |
| The action **vocabulary** itself | [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) / [UXI-04](UX_Feature_Cross_Surface_Actions.md) |
| CGF's symbol, pick box, selection chain | [UXI-10](UX_Feature_Entity_Symbology.md), [UXI-11](UX_Feature_Selection.md) — **prerequisites** |
| Making CGF's edits legal | [UXI-29](UX_Feature_Authority_Aware_Writes.md) — **prerequisite** |
| ExCon | no map |

## 7. Risks

| | |
|---|---|
| 🔴 **Order matters** | UXI-10 → UXI-11 → UXI-29 → **this**. Binding *Rotate* in CGF before UXI-29 gives it an action that writes a component it does not own |
| ⚠ **Migrating IG's fork touches the production map** | [ruling 20](UX_RESUME_INTERACTION.md). Its six behaviours must be proven identical before the `switch` is deleted |
| ⚠ **The pack is a single point of failure** | one wrong line breaks five hosts at once — which is also why 23.1-23.3 are parameterised over all five |
| ⚠ **A uniform set means CGF gains items it may not be able to service** | §3.2's *disabled with a reason* is what keeps that honest |
