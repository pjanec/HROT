<!--STATUS
state: LIVE
build-state: PARTIAL
verified: 2026-08-28 (coordinator source scan)
current-answer: PARTIAL. Only the pre-existing layer-mask round-trip exists (MapLayerRegistry, MapLayerAssignmentSystem, LayerControlGizmo on 4 hosts). MISSING: IMapTagRegistry/MapTag, combination interning, ALL-semantics, registry-generated panel, CGF's layer panel; runtime behaviour gated on a windowed check.
-->
# Feature design — map layers, tags and filtering

> **Design for [UXI-28](UX_Issues.md#uxi-28) · drafted 2026-08-12.** **Status: 🟡 PARTIAL — only the pre-existing layer-mask round-trip exists (`MapLayerRegistry`, `MapLayerAssignmentSystem`, `LayerControlGizmo` on 4 hosts); `IMapTagRegistry`/`MapTag`, interning, ALL-semantics, the registry-generated panel and CGF's panel are all missing.** All decisions settled in
> [Architect_Question_28](Architect_Question_28_Map_Layers.md) — this doc turns them into a build.

<img src="img/uxi28_gates.svg" width="820" alt="Five draw-time gates, one empty, plus three mechanisms that never reach the symbol">

## 0. The decisions this implements

| | Decision |
|---|---|
| **Tags, not a partition** | an entity *or gizmo* may be on several layers |
| **`DebugLayer` = interned combination id** | 🔒 **no wire-format change** |
| **`ZIndex` = draw order + pick priority** | the field exists and is set by no production code |
| **ALL semantics** | hidden if *any* tag is hidden; untagged is always visible |
| **Overflow ⇒ visible + log** | once per episode, never a silent drop |
| **One tag space**, `uint` (32) | `MapDisplayComponent.LayerMask` already is one |
| **`ActiveLayerMask` survives** | the *"which renderers run"* switch, a different granularity |
| **Dim is style, tag is filter** | search dims via `IStyleSource`; it is never a tag |

## 1. Prior art ([rule 6](UX_Issues.md#rules))

| Exists? | What | Adoption | Bearing |
|:--:|---|---|---|
| ✅ | **`LayerControlMask` primitive + `LayerMask256`** — renderer defaults to `SetAll()`, a backend primitive asserts authority for the frame | 4 hosts | ⭐ **the whole mechanism.** Permissive default + per-frame backend override, gizmo-native. **Widen and feed it; do not replace it** |
| ✅ | **`MapDisplayComponent.LayerMask`** — `uint`, doc: *"entity can appear on multiple layers"* | written by `MapLayerAssignmentSystem`; ⚠ **read only by selection rings + picking** | ⭐ **tags were the original intent** — the implementation never followed |
| ✅ | `MapLayerRegistry` — 5 named layers with predicates | IG + Editor | becomes **one host's registration**, not a global truth |
| 🔴 | **`ZIndex`** — *"intra-layer sort; 0=background"* | **0 production writers** | **seam-law instance 12** |
| 🔴 | `SetLayerMask(ushort)` | called by `DebugGizmoLayer.cs:100` | **empty body** |
| ✅ | **`LayerControlGizmo`** round-trip — mask + main-menu binding + `StructInspector`, edits returning via `GizmoStructUpdateEvent` → `OnStructUpdate` | Editor, IG, SimHost, ReplayBrowser — ⚠ **not CGF** | **every link is present in code** (§5) |
| ✅ | `IGizmoVisibilityPolicy` | ⭐ [UXI-10](UX_Feature_Entity_Symbology.md) §3.4 starts using it | culling, not tags — leave it there |

## 2. The design

### 2.1 One tag registry, host-declared, IG-overridable

```csharp
public sealed record MapTag(int Bit, string Id, string Label);

public interface IMapTagRegistry {
    IReadOnlyList<MapTag> Tags { get; }
    int Register(string id, string label);           // → bit index, 0..31
}
```

Layer **definitions** follow [ruling 20](UX_RESUME_INTERACTION.md)'s source layering exactly: the host
registers its set at startup; 🔒 **IG alone** may have the DDS layer tree override it. ⇒
`MapLayerRegistry`'s five predicates become the **default registration every host shares**, and
`IGCapabilitiesAnnounce.LayerTreeJson` stops being a mirror of a constant and becomes the real override
channel. ⚠ **It is currently published and read by nobody** — wiring a consumer is in scope only for IG.

### 2.2 Tag assignment — entity tags ∪ gizmo tags

| Source | Who | Where it lands |
|---|---|---|
| **Entity classes** — ground / air / vehicles / graphics / roads | `MapLayerAssignmentSystem` (exists, time-sliced ~3 s) | `MapDisplayComponent.LayerMask` |
| **Visual kinds** — entities / perception / ai-helpers | the **gizmo**, declared once per gizmo type | a `TagMask` property on the gizmo |

⇒ a primitive's tag set is `entityMask | gizmoTagMask`. 🔒 One space, per decision **C**.

### 2.3 The combination id — the heart of it

```csharp
// backend, per frame
byte id = _combinations.Intern(entityMask | gizmoTagMask);   // → 0..255
prim.DebugLayer = id;

// backend, once per frame, AFTER all primitives are emitted
var mask = new LayerMask256();
foreach (var (combo, comboId) in _combinations)
    if ((combo & _hiddenTags) == 0)      // 🔒 ALL: hidden if ANY tag is hidden
        mask.SetBit(comboId);
draw.EmitRaw(DebugPrimitive.MakeLayerControlMask(mask));
```

| | |
|---|---|
| ✅ **Renderer unchanged** | `if (!activeLayers.IsSet(prim.DebugLayer)) continue;` — byte for byte |
| ✅ **Untagged ⇒ combination 0 ⇒ `0 & hidden == 0` ⇒ visible** | today's permissive default falls out of the algebra |
| ✅ **Ids never leave the frame** | primitives and mask come from the same backend frame |
| ⚠ **The mask must be emitted last** | it has to see every combination interned this frame. Emit order within a frame is not otherwise significant — ⚠ **the one ordering constraint this design introduces** |

**Interning cost** — do **not** intern per primitive:

| Step | Cost |
|---|---|
| entity combination | cached in `MapDisplayComponent` next to the mask, recomputed by the system that already recomputes it |
| × gizmo tag mask | a small `[entityCombination × gizmoTagId] → byte` table |
| ⇒ emit | a table lookup |

**Overflow** — on the 257th distinct combination: 🔒 **return the always-visible id and log once per
episode** (a flag reset when the table is cleared), never per primitive per frame.

### 2.4 `ZIndex` takes over draw order and picking

| Was | Becomes |
|---|---|
| sort: `DebugLayer` asc, then `ZIndex` | **`ZIndex` asc** |
| pick: highest `DebugLayer` wins | **highest `ZIndex` wins** |

⭐ **Nothing depends on `ZIndex` today** — it is uniformly 0 — so the only ordering to reproduce is
today's `Entities`(0) → `Perception`(1) → `AiHelpers`(2), which becomes `ZIndex` 0/1/2 on those gizmos.
⚠ **Within a layer, order is currently arbitrary** (stable sort over emit order), so assigning `ZIndex`
can only make it more defined. ⚠ **Verify the pick box still beats the symbol beneath it.**

### 2.5 The control panel — 🔒 fully compatible, and here is why

> **User:** *"There was a gizmo able to show layer checkboxes, never worked end to end — not sure how
> compatible with the idea of a calculated `DebugLayer` byte."*

🔒 **Completely compatible — they operate at different levels, and the panel is the higher one.**

```
panel  →  which TAGS are hidden        ← the only state a user edits
             ↓  (backend, ALL semantics)
          which COMBINATIONS are visible
             ↓
          LayerMask256  →  renderer
```

⇒ **The UI never sees a combination id.** It edits tag visibility — the thing a user actually understands
— and the byte is an internal encoding underneath. ⭐ **Making the byte calculated does not touch the
panel at all.**

**The one real change:** `LayerControlDto` is a **static class with three `bool` properties**, and its
schema hash is `hash(typeof(LayerControlDto).FullName)`. A host-registered tag set is dynamic, so the
panel must be **generated from `IMapTagRegistry`** — one checkbox per registered tag — rather than
reflected off a fixed type. ⚠ **This is the design's main UI task**; the StructEdit path is
schema-hash + JSON, so a generated schema fits it, but the generation itself is new.

⇒ Adding a layer then needs **no UI edit**, which is the point.

## 3. What gets deleted or fixed

| | Action |
|---|---|
| `SetLayerMask(ushort) { }` | 🔴 **delete** — the real filter is the mask; an empty method that reads as working code is worse than none |
| `GlobalDebugSettings.DebugLayerMask` / `ForceAllGizmosVisible` | **delete** — deferred since GZ015, its UI is a literal stub |
| 🔴 **`MapLayerBits` bit-number collision** | the five constants are used for **both** `ActiveLayerMask` (layer objects — `RoadMapLayer` is bit 0) **and** entity classes (`GroundUnitsBit` is bit 0). **Split into two named sets**; folds in [UXI-14](UX_Issues.md#uxi-14) |
| `MapLayerRegistry`'s hand-synced copy of those constants | **delete** — the file's own comment admits the duplication |
| Duplicate `ConfigPanel` / `MapLayerState` / `IMapConfigController` (byte-identical, two projects) | **delete one** |
| `DebugGizmoLayer.cs:91` — *"`DebugLayer` is NOT used as a Z-order key"* | **correct it** — it is, twice, today |
| **CGF registers no `LayerControlGizmo`** | **register it** — CGF is silently all-visible |

## 4. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 28.1 | Interning is stable within a frame — the same mask returns the same id | H |
| 28.2 | 🔒 **ALL**: tags `{ground, hostile}`, `hostile` hidden ⇒ the combination is **not** in the visible mask | H |
| 28.3 | Untagged (combination 0) is visible with **any** set of hidden tags | H |
| 28.4 | Hiding a tag no entity carries changes nothing | H |
| 28.5 | 🔴 **257th distinct combination ⇒ always-visible id + exactly one log line**, and the 258th logs nothing more | H |
| 28.6 | A primitive's tag set is `entityMask \| gizmoTagMask` | H |
| 28.7 | Sort is by `ZIndex` alone; `DebugLayer` no longer affects order | H |
| 28.8 | `Entities`/`Perception`/`AiHelpers` at `ZIndex` 0/1/2 reproduce **today's** visual stacking | H |
| 28.9 | Hit-test returns the highest `ZIndex`; ⚠ **the pick box still wins** over the symbol under it | H |
| 28.10 | The `LayerControlMask` primitive is emitted **after** every other primitive in the frame | H |
| 28.11 | The panel is generated from the registry — registering a 6th tag yields a 6th checkbox, **no UI edit** | H |
| 28.12 | Toggling a checkbox → `GizmoStructUpdateEvent` → the next frame's mask differs | I |
| 28.13 | 🔴 **End-to-end in a real host**: *View ▸ Tactical Map Layers…* → uncheck *Perception* → cones disappear, symbols stay | I |
| 28.14 | CGF has a working layer panel (it has none today) | I |
| 28.15 | IG: a DDS layer-tree override changes the registered set at runtime | I |
| 28.16 | `ActiveLayerMask` still switches whole layer objects (roads, grid) independently of tags | H |
| 28.17 | Search dim leaves every primitive **present** — dimming never removes anything | H |

**12 H · 5 I · 0 V.**

## 5. ⚠ The unresolved runtime question

The user reports the layer panel *"never worked end to end"*. **Statically, every link is present** —
verified individually:

| Link | Evidence |
|---|---|
| gizmo registered globally | `EditorSubsystem.cs:1137` |
| menu action → event | `:1139-1141` → `OpenLayerEditorEvent` |
| gizmo drains it, toggles editing, emits `StructInspector` | `LayerControlGizmo.cs:99-112` |
| host draws the inspector | `EditorSubsystem.cs:1912`, `IgApplication.cs:1261`, `SimHostVisualization.cs:371`, `ReplayBrowserSubsystem.cs:419` |
| edits published back | `DebugGizmoLayer.cs:142-144` → `GizmoStructUpdateEvent` |
| routed to the gizmo | `GlobalGizmoManager.cs:169` → `OnStructUpdate` |
| mask recomputed | `LayerControlGizmo.cs:117-121` |

🔴 **So the break is not visible from the source.** It needs a **Windows session** to observe. Candidates
to check in order, cheapest first: whether the panel appears at all (schema registration /
`ComponentEditService`), whether the toggle reaches `OnStructUpdate`, and whether the recomputed mask
reaches the renderer's frame. ⚠ **Do not begin this design until that is known** — if the round-trip is
broken today, the same break will silently swallow the generated panel.

## 6. 🔒 Out of scope

| | |
|---|---|
| Search UI itself | dimming is [UXI-10](UX_Feature_Entity_Symbology.md)'s style path; the search *panel* is unfiled |
| 3D / other `PipelineTarget`s | `TargetView` is a separate gate and stays as-is |
| Per-layer opacity or ordering by layer | tags are boolean; ordering is `ZIndex` |
| Selection ring appearance | its own gizmo |

## 7. Risks

| | |
|---|---|
| 🔴 **The round-trip may already be broken** (§5) | ⇒ **gate the work on a Windows verification** |
| ⚠ **Draw-order change is visible in the production map** | 28.8 is the guard; IG is the surface [ruling 20](UX_RESUME_INTERACTION.md) protects |
| ⚠ **Emit-order constraint is new and invisible** | a gizmo emitting after the mask silently gets combination-0 treatment. ⚠ Assert it in debug builds rather than documenting it |
| ⚠ **Splitting `MapLayerBits`** touches config panels, adapters and IG's DDS config parse | mechanical but wide; 28.16 guards the layer-object half |
| ⚠ **Time-sliced tag assignment (~3 s)** | a newly-spawned entity is untagged — and therefore **visible** — for up to 3 s. ⭐ Correct by the ALL rule, but it means *"hide hostiles"* has a visible lag on spawn. Consider assigning on spawn |
