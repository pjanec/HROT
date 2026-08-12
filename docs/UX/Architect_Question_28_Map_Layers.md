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

## A. Partition or tags — 🔒 **RULED: tags**

> **User, 2026-08-12:** *"I would like one entity **or gizmo** to belong to more than one layer."*

⇒ **A2.** Multi-membership, and it applies to **both axes** — an entity carries classes, a gizmo carries
kinds, and a primitive belongs to the union. C below collapses accordingly.

## B. 🔒 **RULED: the byte becomes a combination id** — and it costs nothing on the wire

> **User, 2026-08-12:** *"Maybe the `DebugLayer` byte for the gizmo needs to be dynamically calculated out
> of the bitmasks?"*

⭐ **This works, and it is better than widening the struct.** Reinterpret the byte as an **index into the
set of tag-combinations actually in use this frame**, not as a layer id:

| Stage | What it does | Change |
|---|---|---|
| **Backend, per entity** | tags → 64-bit mask (already computed by `MapLayerAssignmentSystem`) | reuse |
| **Backend, at emit** | `id = intern(entityMask \| gizmoKindMask)` → a byte | **new, small** |
| **Backend, once per frame** | for each interned combination, evaluate *"is this combination visible?"* → set bit *id* in the `LayerMask256` it already emits as `LayerControlMask` | **new, small** |
| **Renderer** | `if (!activeLayers.IsSet(prim.DebugLayer)) continue;` | 🔒 **unchanged, byte for byte** |

| | |
|---|---|
| ✅ **No wire-format change** | `DebugPrimitive` stays 64 bytes; no spare-offset audit needed; the DDS terminal path is untouched |
| ✅ **The renderer stays a dumb terminal** | it never learns what a layer *is* — exactly the architecture the gizmo pipeline was built for |
| ✅ **Combination ids never leave the frame** | the primitives *and* the mask come from the same backend frame, so they cannot disagree |
| ✅ **The visibility policy lives in one place** | ANY/ALL (see B′) is a backend decision, changeable without touching the renderer or the wire |

⚠ **Two costs, both real:**

| | |
|---|---|
| **256-combination ceiling** | 64 layers ⇒ up to 2⁶⁴ combinations, but only *observed* ones get ids; entities cluster into few distinct tag sets. 🔒 **RULED (user, 2026-08-12): on the 257th, degrade to always-visible and log.** Never silently drop — an unfilterable symbol is a nuisance, a missing one is a lie. ⚠ The log must fire **once per overflow episode**, not per primitive per frame |
| **Interning cost per primitive** | ⇒ **cache the id, don't intern per primitive**: store it next to the mask in `MapDisplayComponent` (recomputed by the system that already recomputes the mask), then combine with the gizmo's kind via a small `[entityCombination × gizmoKind] → id` table |

### 🔴 B′ — the blocker: `DebugLayer` has **three** jobs, not one

Verified — the byte is also:

| Job | Evidence |
|---|---|
| 1. visibility filter | `DebugPrimitiveRenderer2D.cs:96` |
| 2. 🔴 **primary painter's-algorithm sort key** — `DebugLayer` ascending, `ZIndex` as tie-break | `:175-180` |
| 3. 🔴 **hit-test priority** — highest `DebugLayer` wins the pick | `DebugGizmoLayer.cs:447` |

⇒ **An interned combination id would scramble draw order and make pick priority arbitrary.** Today
`Perception`(1) and `AiHelpers`(2) deliberately draw *above* `Entities`(0), and that ordering is a side
effect of the layer numbering.

**The separation is available**: `DebugPrimitive.ZIndex` already exists — *"intra-layer sort;
0=background"*. So:

| | Option |
|--:|---|
| **B′1** | **Sort and hit-test by `ZIndex` alone**; `DebugLayer` becomes purely the visibility key. ⚠ Requires assigning meaningful `ZIndex` values to reproduce today's ordering |
| **B′2** | Keep an ordering byte separate from the combination id — ⚠ needs a spare byte after all |
| **B′3** | Make interning **order-preserving** (allocate ids in ascending primary-layer order) — ⚠ fragile: ids shift as combinations appear |

🔒 **RULED: B′1** (user, 2026-08-12) — *"is there a dedicated sorter field in the gizmo? if yes then of
course it should be made working."*

⭐ **There is, and it is dead.** `ZIndex` — `[FieldOffset(15)] public byte ZIndex; // intra-layer sort;
0=background` — is set by **no production code at all**. The only writers in the repo are three lines in
`GizmoMap.Example/Scenarios/DemoSceneGenerator.cs`. **Every production primitive has `ZIndex = 0`**, so
the sort collapses to `DebugLayer` ascending, then emit order. ⭐ **Seam-law instance 12.**

⇒ **The migration is far cheaper than feared:**

| | |
|---|---|
| ✅ **Nothing depends on `ZIndex` today** | it is uniformly 0 — no existing ordering can break by starting to use it |
| ✅ **Only *one* ordering must be reproduced** | today's cross-layer order is `Entities`(0) → `Perception`(1) → `AiHelpers`(2). Give those gizmos `ZIndex` 0/1/2 and the picture is unchanged |
| ✅ **Within a layer, order is currently *arbitrary*** (stable sort over emit order) ⇒ assigning `ZIndex` can only make it **more** defined |
| ⚠ **Hit-test priority still needs a rule** | `DebugGizmoLayer.cs:447` picks the highest `DebugLayer`. It becomes highest `ZIndex` — ⚠ verify the pick box still wins over the symbol it sits under |

⇒ 🔒 **`DebugLayer` becomes purely the visibility key; `ZIndex` becomes the sole draw order and pick
priority.** Both fields then do exactly what their names and doc comments already claim.

⚠ **Also note a stale doc comment**: `DebugGizmoLayer.cs:91` claims *"`DebugLayer` is **NOT** used as a
Z-order key"* — while `:447` uses it to choose the pick winner and `:178` sorts by it. Whatever is decided,
that comment must stop lying.

### B″ — 🔒 **RULED: ALL** (user, 2026-08-12)

A primitive tagged `{ground, hostile}`; the operator hides `hostile`.

| | Rule | Result |
|--:|---|---|
| **ANY** | visible if **any** of its tags is visible | ⛔ still shown — it is still `ground`. *"Hide all hostiles"* silently fails |
| **ALL** | hidden if **any** of its tags is hidden | ✅ hidden |

🔒 **ALL.** *"Hide hostiles"* and *"hide perception overlays"* both behave as an operator expects, and the
two axes compose — a perception cone on a hostile unit tagged `{perception, hostile, ground}` disappears
when *either* `perception` or `hostile` is switched off.

| Accepted consequence | |
|---|---|
| **More tags ⇒ easier to hide** | an entity in four layers is hidden if any one of the four is off |
| **Empty set is always visible** | combination 0 (no tags) preserves today's permissive default |
| ⭐ **The rule lives in exactly one place** | the backend's per-frame *"is this combination visible?"* evaluation. Changing it later touches no wire format, no renderer, no gizmo |

## C. The two axes — 🔒 collapsed by the tags ruling

Ruling A2 covers *"entity **or gizmo**"*, so **one tag space serves both**: entity classes (ground, air,
vehicles, graphics, roads) and visual kinds (entities, perception, ai-helpers) are bits in the same 64-bit
space, and a primitive's combination is the union of the two.

⇒ My earlier lean (C3 — move visual kinds to `IGizmoVisibilityPolicy`) is **withdrawn**: it cannot express
*"hide perception overlays **on hostiles only**"*, which one tag space with ALL semantics gets for free.
⚠ The policy seam still earns its keep for **culling** ([UXI-10](UX_Feature_Entity_Symbology.md) §3.4) —
a different job, per gizmo type, not per tag.

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

## E′. What `MapCanvas.ActiveLayerMask` actually is — and why it is a *different* question

It is **not** an entity filter. It switches **whole drawing objects** on and off. Each `IMapLayer`
declares a `LayerBitIndex`; `MapCanvas.Draw` skips the object entirely when its bit is clear
(`MapCanvas.cs:142-149`), and `-1` means *always draw*.

| Layer object | Bit |
|---|---|
| `GridMapLayer`, `SelectionRenderSystem`, `SimHostTrajectoryLayer` | `-1` — always |
| `RoadMapLayer` / `SimHostRoadLayer` | 0 |
| `PerceptionMapLayer` | 9 |
| **`DebugGizmoLayer`** — *the entire gizmo pipeline* | **31** |

⇒ It is the *"which renderers run"* switch — coarse, cheap, and it never inspects a primitive. The
`ConfigPanel` checkboxes (Satellite / Roads / Grid) drive it, and they are the right UI for it.

🔒 **RULED (user, 2026-08-12): `ActiveLayerMask` survives.** It answers *"draw the road network at all?"*,
while §B answers *"is this hostile air unit visible?"*. Folding them together would mean walking every
primitive to decide something that is currently one bit test per renderer. ⚠ **Stop calling it a layer
mask in the UI** — that name is why the two concepts got conflated.

🔴 **But one real collision must be fixed**: `MapLayerBits`' five constants (`GroundUnitsBit` = `1<<0` …)
are used for **both** `ActiveLayerMask` (layer objects — `RoadMapLayer` is bit 0) **and**
`MapDisplayComponent.LayerMask` (entity classes — `GroundUnitsBit` is bit 0). **Same bit numbers, two
unrelated meanings, one constants file** — and a second hand-synced copy in `MapLayerRegistry`. That is
the actual mess behind [UXI-14](UX_Issues.md#uxi-14).

## E. What gets deleted?

| | Verdict |
|---|---|
| `SetLayerMask(ushort) { }` | 🔴 **delete or implement — do not leave.** It reads as working code |
| `GlobalDebugSettings.DebugLayerMask` / `ForceAllGizmosVisible` | **delete** — deferred since GZ015, its UI is a literal stub |
| `MapCanvas.ActiveLayerMask` + `IMapLayer.LayerBitIndex` | **keep** — genuinely different granularity (whole layer objects: grid, roads, trajectories) |
| The duplicate `ConfigPanel` / `MapLayerState` / `IMapConfigController` (byte-identical in two projects) | **delete one** — folds in [UXI-14](UX_Issues.md#uxi-14) |
| `MapLayerRegistry`'s copy of `MapLayerBits`' constants | **delete** — the file's own comment admits they are hand-synced |

## H. 🔒 **Dim ≠ hide** — and dimming needs none of this machinery

> **User, 2026-08-12:** *"Dimming everything else but search hits sounds great **if it really means
> dimming, not completely hiding**."*

🔒 **It does — and they are deliberately two different mechanisms:**

| | Hide | Dim |
|---|---|---|
| Mechanism | the combination mask (§B) | the **resolved colour** |
| Nature | binary — the primitive is skipped | continuous — alpha / desaturation |
| Where decided | backend, per frame, per combination | backend, at emit, per primitive |
| Renderer change | none | ⭐ **none** |

⭐ **Dimming costs nothing extra**, because [UXI-10](UX_Feature_Entity_Symbology.md) already makes the
backend compute each primitive's colour from `ResolvedStyle`. A *dim* state is one more contribution to
that colour — naturally an `IStyleSource` ([ruling 20](UX_RESUME_INTERACTION.md)), e.g. a
`SearchHighlightStyleSource` that desaturates everything not in the current result set.

| | |
|---|---|
| ✅ **The renderer stays dumb** | colour already travels in the primitive; the remote IG terminal dims correctly with no protocol change |
| ✅ **Composable** | dim-because-not-a-search-hit and dim-because-out-of-perspective stack as sources |
| 🔒 **Search is a *style* concern. Full stop** | user, 2026-08-12: *"**dimming is style, tag is layer filter**"* ⇒ search dims; it does **not** become a tag |
| ⚠ **Selection stays its own gizmo** | the ring is a separate primitive, unaffected either way |

### 🔒 The dividing line, stated once

| | **Tag** | **Style** |
|---|---|---|
| Answers | *"is this thing on a layer I am showing?"* | *"how does this thing look?"* |
| Effect | **filter** — present or absent | **appearance** — colour, alpha, emphasis |
| Carried by | the combination-id byte + mask (§B) | the primitive's resolved colour (§H) |
| Examples | ground units, air units, perception overlays, roads | affiliation tint, damage, **search dim** |

⚠ **Withdrawn: my open question "should search also get a hide mode?"** It conflated the two columns —
exactly the confusion this section exists to end. If *"hide everything except hits"* is ever wanted, it is
a new **tag** and needs no redesign; it is not a variant of dimming.

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

## ✅ All decisions settled — 2026-08-12

| | Decision |
|---|---|
| **A** | Layers are **tags**; an entity *or gizmo* may be in several |
| **B** | `DebugLayer` becomes an **interned combination id** — **no wire-format change** |
| **B′** | **`ZIndex`** becomes the sole draw order and pick priority — the field exists and was dead |
| **B″** | **ALL** semantics — hidden if any tag is hidden; untagged is always visible |
| **overflow** | 257th combination ⇒ **degrade to visible + log once per episode**, never drop |
| **C** | **One tag space** — entity classes and visual kinds are bits in the same mask |
| **E′** | **`MapCanvas.ActiveLayerMask` survives** as the *"which renderers run"* switch |
| **H** | 🔒 **Dim is style, tag is filter** — search dims and is never a tag |
| **width** | **`uint` (32 tags)** — `MapDisplayComponent.LayerMask` already is one; no change |

⇒ **Ready to become a design.** Remaining work is mechanical, not decisional:

| | |
|---|---|
| 1 | Fix the `MapLayerBits` bit-number collision (layer objects vs entity classes) + the hand-synced copy — folds in [UXI-14](UX_Issues.md#uxi-14) |
| 2 | `SetLayerMask` — delete or implement, never leave |
| 3 | One layer UI generated from the registered set (§F); delete the duplicate `ConfigPanel` |
| 4 | Register the control gizmo in **CGF**, which has none and is silently all-visible |
| 5 | Delete `GlobalDebugSettings.DebugLayerMask` (deferred-dead since GZ015) |
| 6 | Correct the stale doc comment claiming `DebugLayer` is not a Z-order key |
| **4** | **How wide is the tag space?** ⚠ **Lean revised — keep `uint` (32).** `MapDisplayComponent.LayerMask` is **already a `uint`**, and its doc comment already says *"entity can appear on multiple layers"* — ⭐ **tags were the original intent**. Today 8 ids are in use; even with affiliation, selection and search added it is ~15. **32 needs no change at all**; widen to 64 only when a concrete need appears |
