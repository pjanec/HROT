# Architect Question 28 — unifying map layers and entity filtering

> **For [UXI-28](UX_Issues.md#uxi-28) · opened 2026-08-12. Status: ◐ open — decisions pending.**
> Requested by the user: *"unification to a similar concept of defining, entity filtering and controlling
> map layers is desired; best if it follows the same sequence of default settings → overrides as the DDS
> net uses in IG, generalized. It needs to be compatible with the gizmo layers, as the whole map is
> rendered using gizmos."*
>
> Follows the format of [Q27](Architect_Question_27_Tool_Model.md): decision-shaped sub-questions, each
> with options, a recommended lean, and the reuse-vs-build tradeoff.

<img src="img/uxi28_gates.svg" width="820" alt="Five draw-time gates, one empty, plus three mechanisms that never reach the symbol">

## 0. The verified ground truth

| | Mechanism | Reality |
|--:|---|---|
| 1 | `IMapLayer.LayerBitIndex` vs `MapCanvas.ActiveLayerMask` (32-bit) | gates **whole layer objects**. `DebugGizmoLayer` = bit 31; nothing clears it ⇒ always on |
| 2 | `DebugPrimitiveRenderer2D.SetLayerMask(ushort)` | 🔴 **empty body**. `DebugGizmoLayer.cs:100` pushes the canvas mask in; it dies there |
| 3 | `DebugPrimitive.DebugLayer` (byte) vs `LayerControlGizmo`'s `LayerMask256` | ✅ **the only filter that reaches drawn primitives**. Default `SetAll()`; a backend `LayerControlMask` primitive asserts authority for the frame. **3 of 256 bits used**; bits 3-255 force-set visible |
| 4 | `MapDisplayComponent.LayerMask` (uint, 5 named layers) | computed per entity every ~3 s by `MapLayerAssignmentSystem` — 🔴 **read only by selection rings and picking; the symbol emitter never reads it** (always `layer: 0`) |
| 5 | `GlobalDebugSettings.DebugLayerMask` + `IGizmoVisibilityPolicy` | both deferred-dead ("GZ015 Phase 6"); ⭐ UXI-10 §3.4 begins using the policy |

**Four independent definition sources**: the hardcoded `MapLayerRegistry` (5 predicates), the DDS
`IGCapabilitiesAnnounce.LayerTreeJson` (a runtime *mirror* of it, **published and unconsumed**), seven
hardcoded `ConfigPanel` checkboxes, and `LayerControlDto`'s three unrelated names.

⭐ **The good news, and the spine of every answer below:** mechanism 3 **already is** the shape the user
asked for — *permissive default, backend asserts authority per frame*, travelling as a gizmo primitive.
It is the same default→override sequence as IG's DDS style layer, and it is gizmo-native by construction.
**The work is to widen and feed it, not to invent it.**

---

## A. Are layers a **partition** or **tags**?

🔴 **Answer this first — B, C and E all follow from it.**

| | Option | Consequence |
|--:|---|---|
| **A1** | **Partition** — every entity is in exactly one layer | `DebugLayer` byte is sufficient as-is; `MapDisplayComponent.LayerMask` becomes an index, not a mask |
| **A2** | **Tags** — an entity may belong to several ("ground unit" *and* "friendly" *and* "selected") | the byte cannot express it; needs B2 or B3 |
| **A3** | **Partition for drawing, tags for querying** | the symbol draws in one bucket; picking/search filters on many |

**Lean: A3.** It is what the code already half-does — `MapDisplayComponent.LayerMask` is consumed by the
**pick filter** (`LayerMaskFilter`), never by the renderer. ⚠ But it needs your confirmation, because A2
is the more natural operator model ("hide all hostiles", "hide all air") and would force B2.

**Reuse vs build:** A3 reuses both existing mechanisms unchanged in role. A2 requires widening the
primitive (B2) — a fixed-layout struct change.

## B. `DebugLayer` **byte** vs `LayerMask` **bitmask** — the conflict you flagged

| | Option | Cost |
|--:|---|---|
| **B1** | Primitive keeps the **byte** = one primary layer index; multi-membership resolved at emit time | free; loses "hide by any membership" |
| **B2** | **Widen the primitive** to carry a mask | ⚠ `DebugPrimitive` is a fixed 64-byte explicit-layout struct — a mask costs 32 bytes at 256 bits, or 4/8 at 32/64. **Needs a spare-offset audit before it can be costed** |
| **B3** | Emit one primitive per layer membership | ⛔ multiplies primitive count per entity — rejected on cost |
| **B4** | Keep the byte, but make it an index into a **256-entry layer table** whose entries are themselves compound | free; expressive; ⚠ indirection nobody can read at a glance |

**Lean: B1 if A3, B2-narrow (a 32- or 64-bit mask, not 256) if A2.** ⚠ **I have not audited the
primitive's spare bytes** — that is the first task if B2 is chosen, and it decides whether A2 is even
affordable.

**Reuse vs build:** B1 is pure reuse. B2 changes a wire-format struct shared with the DDS/terminal path —
the most expensive option on this page, and it touches IG (the production map).

## C. Two orthogonal axes, currently conflated into one byte

⭐ **This is the finding that reframes the issue.** The three *working* bits are **visual kinds** —
`Entities` / `Perception` / `AiHelpers`, set at each gizmo's emit site. The five *registry* layers are
**entity classes** — ground units, air units, vehicles, tactical graphics, road graphs, computed per
entity. **These are different questions**, and both are legitimate:

> *"Show me perception overlays"* is not the same request as *"show me air units"*.

| | Option |
|--:|---|
| **C1** | One axis — fold entity classes into the same 256-bit space as visual kinds (bits 0-2 kinds, 8+ classes) |
| **C2** | Two axes — a primitive carries **both** a visual-kind index and an entity-class index; both must pass |
| **C3** | One axis, entity classes only; visual kinds move to `IGizmoVisibilityPolicy` (per gizmo type) |

**Lean: C3.** ⭐ It uses two seams that already exist for exactly these two jobs — the layer mask for
*what the entity is*, the visibility policy for *what the gizmo is* — and UXI-10 §3.4 already starts
using the policy for culling. C2 doubles the per-primitive cost of a decision that C3 makes once per
gizmo type.

## D. Where do layer definitions come from?

**Lean: host-declared defaults + IG's DDS override**, mirroring [ruling 20](UX_RESUME_INTERACTION.md)'s
source layering exactly:

| Layer of the sequence | Source |
|---|---|
| default | the host registers its own layer set at startup (service maps: the 5 classes; IG: the same, plus whatever it announces) |
| override | 🔒 **IG only** — the DDS layer tree |

⇒ `MapLayerRegistry`'s 5 hardcoded predicates become **one host's registration**, not a global truth, and
`LayerTreeJson` stops being a mirror of a constant and becomes the actual override channel. ⚠ **Note it is
currently published and consumed by nobody** — ExCon has no reader.

## E. What gets deleted?

| | Verdict |
|---|---|
| `SetLayerMask(ushort) { }` | 🔴 **delete or implement — do not leave.** It reads as working code |
| `GlobalDebugSettings.DebugLayerMask` / `ForceAllGizmosVisible` | **delete** — deferred since GZ015, its UI is a literal stub |
| `MapCanvas.ActiveLayerMask` + `IMapLayer.LayerBitIndex` | **keep** — genuinely different granularity (whole layer objects: grid, roads, trajectories) |
| The duplicate `ConfigPanel` / `MapLayerState` / `IMapConfigController` (byte-identical in two projects) | **delete one** — folds in [UXI-14](UX_Issues.md#uxi-14) |
| `MapLayerRegistry`'s copy of `MapLayerBits`' constants | **delete** — the file's own comment admits they are hand-synced |

## F. One layer UI, or two?

Today there are **two unrelated panels**: `LayerControlGizmo`'s generic StructInspector (3 checkboxes,
gizmo-emitted) and `ConfigPanel` (7 checkboxes, ImGui, duplicated). Neither is generated from a registry.

**Lean: one panel, generated from the registered layer set** — so adding a layer needs no UI edit. Keep
the **gizmo-emitted** delivery, since that is what already works remotely and satisfies the
gizmo-compatibility constraint.

## G. Which hosts get which layers?

**Lean: every host registers the same five entity classes** (they are generic — ground/air/vehicles/
graphics/roads), and hosts differ only in *overrides*. ⚠ Today `MapLayerAssignmentSystem` runs only in IG
and the Editor; **CGF registers no `LayerControlGizmo` at all**, so it is silently all-visible.

---

## Open, needing your ruling

| # | Question |
|--:|---|
| **1** | **A — partition, tags, or split?** Everything else hangs on this |
| **2** | **B — if tags, may we widen `DebugPrimitive`?** It is a wire-format struct shared with the DDS terminal path. I have **not** audited spare bytes yet |
| **3** | **C — do you agree visual-kind and entity-class are separate axes**, with the gizmo-type axis moving to the visibility policy? |
| **4** | **E — `MapCanvas.ActiveLayerMask` survives** as the whole-layer-object switch? Or should everything collapse into one mechanism? |
| **5** | Should **selection/highlight** and **search results** become layers too, or stay separate? The user's phrase was *"defining, entity filtering and controlling map layers"* — filtering may be wider than layers |
