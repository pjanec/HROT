<!--STATUS
state: LIVE
build-state: NOT-BUILT
verified: 2026-08-28 (coordinator source scan)
current-answer: 3.3 and 3.4 are BUILT (UXI-23 S2a/S4). START AT 3.8 - READY-TO-BUILD, with UML:
  TWO selectable symbol paths (silhouette, nato2525-as-a-stub) plus a non-selectable emergency box
  fallback, switchable per host; the deleted health bar is restored; decorations are per path. User
  rulings 2026-08-30. Still NOT-BUILT: 3.1 (CE-125 - the renderer hardcodes cyan at
  EntityPresentationGizmoShared.cs:92), 3.2, 3.5, 3.6, 3.7.
known-rot: 3.0 is SUPERSEDED by 3.8.9 - the user ruled the JSON style cascade an IG-ONLY speciality, so
  StyleResolutionSystem is NOT lifted to every host. 3.8's own first draft is in the HISTORY section and
  must not be quoted.
-->
# Feature design — entity symbology on the map

> **Design for [UXI-10](UX_Issues.md#uxi-10) · drafted 2026-08-12.** **Status: ❌ NOT-BUILT (design only) — renderer still hardcodes cyan (`EntityPresentationGizmoShared.cs:92`); resolved style not consumed; the three per-host gizmos never merged.** Also **verifies and absorbs [UXI-19](UX_Issues.md#uxi-19)** (previously
> *unverified*) and supplies the mechanism behind [UXI-11](UX_Issues.md#uxi-11).

<img src="img/uxi10_symbology.svg" width="880" alt="Two symbology pipelines that never meet">

## 0. 🔴 The issue as filed is the smallest part of it

> *"Map symbology seam exists and no host uses it — every host passes `DefaultEntityShapeLibrary`."*

True. But the scan found something larger: **HROT has two symbology pipelines, fully built, that are not
connected to each other.**

| | Pipeline | Ends at |
|---|---|---|
| **Upstream** | `StyleResolutionSystem` — **278 lines**, a **3-layer merge** (TKB default → DDS override → operator config) writing `ResolvedStyle` **every PostSimulation tick** | 🔴 **text UI only** — the inspector, a tooltip, and history-trail sampling |
| **Downstream** | `IEntityShapeLibrary` → `EntityShapeProfile` → polylines on the map | fed a **DIS number** and **one hardcoded colour** |

⇒ The colour is **computed correctly, every frame, for every entity — and thrown away.**

## 1. Prior art ([rule 6](UX_Issues.md#rules))

| Exists? | What | Adoption | Bearing |
|:--:|---|---|---|
| ✅ | **`StyleResolutionSystem`** + **`ResolvedStyle`** — Tint, Affiliation, DamageLevel, TextureName, Label, ShowTrail, ShowSensors | **0 renderers** | ⭐ **the resolver this design was going to invent already exists** — `StyleResolutionSystem.cs`, `ResolvedStyle.cs` |
| ✅ | `ApplyAffiliationColor(...)` inside that system (`StyleResolutionSystem.cs:113`) | 1 (internal) | it already merges `EntityInfo.ForceId` and the DDS `StyleSetId` into a tint |
| ✅ | **`ForceId`** — `Neutral / Friend / Hostile`, **145 references** in `Hrot/` | perception, EQS, TKB | 🔴 its own XML doc says *"Rendered as **green** / **blue** / **red**"* (`ForceId.cs:12-19`) — **no renderer implements it** |
| ✅ | **`ResolvedStyleConstants`** — the affiliation palette: Friend `(0,100,255)`, Hostile `(255,0,0)`, Neutral `(0,255,0)`, Unknown white | 1 (the resolver) | ⭐ **the authoritative table** — §3.2 |
| ⚠ | **`GetAffiliationColor(ForceId)`** — a **second** copy of the palette | 🔴 `private`, 1 caller | `EntityPlacementGizmo.cs:255-261`. The **placement ghost** is correctly coloured; the moment the entity is placed it turns cyan. ⚠ **And it disagrees**: Friend is `(0,0,255)` here vs `(0,100,255)` in `ResolvedStyleConstants` |
| ⚠ | `MilStd2525Renderer.GetAffiliationColor` — a **third** palette (Neutral=**Yellow**, Unknown=**Green**) | the `MilStd2525` primitive, **never emitted** | ⚠ three inconsistent affiliation palettes; only the unreachable one is unit-tested |
| ✅ | `IEntityShapeLibrary.GetShape(string? shapeName, ulong fallbackDisType)` | 4 explicit hosts + CGF implicitly | 🔴 **`shapeName` is `null` at the only call site** (`DebugPrimitiveRenderer2D.cs:410`) — half the interface is dead |
| 🔴 | **`VisualData.MapShapeName`** — a `FixedString32`, doc-commented *"Optional explicit name of the 2-D map shape to render **from the entity shape library**"* | **0 readers** | ⭐⭐ **the purpose-built field for the dead parameter.** Declared (`VisualData.cs:33`), carried through the TKB DTO (`VisualDefinitionDto.cs:29`), **populated by the translator** (`PresentationTkbTranslator.cs:41`), present in scenario JSON — and **read nowhere in the repo** |
| ✅ | `StatelessGizmoRegistry.Register(projector, visibilityPolicy)` — an **`IGizmoVisibilityPolicy` parameter** | default only | ⭐ the clean fix for the double-registration (§3.4) |
| ⚠ | `EntityPresentationGizmoShared` — the shared helper | 3 gizmos, **inconsistently** | CGF bypasses it for the shape (§2, defect E) |

⭐ **Seam-law instance 10 — the largest yet.** Not a helper nobody wired: a **278-line system with DDS
integration and operator overrides**, running every tick, whose output the map never reads. **Instance 11**
is `MapShapeName`: a field authored in scenario data, translated into a component, and never read.

### ⚠ The filed wording is wrong in one detail, and it matters

> *"every host passes `DefaultEntityShapeLibrary`"*

| Host | Reality |
|---|---|
| Editor, SimHost, IG, ReplayBrowser | ✅ pass it **explicitly** (`EditorSubsystem.cs:1545`, `SimHostVisualization.cs:242`, `IgApplication.cs:826`, `ReplayBrowserSubsystem.cs:237`) |
| **CGF** | ⚠ **omits the argument** — the 3-arg `DebugGizmoLayer` ctor (`CgfSubsystem.cs:583`); the default arrives through **three levels of `??`** |
| **StrideMock** | ⚠ **not wired at all** — its renderer call is commented `// wire in SM-009`; it draws `Raylib.DrawCircleV(..., Color.Red)` per entity (`StrideMockSubsystem.cs:218-221`) |
| **ExCon** | ✅ correctly absent — it has no map ([ruling 16](UX_RESUME_INTERACTION.md)) |

⇒ 🔒 **The accurate statement:** *every host with a map gets the default — four by choice, one by omission —
and **no second implementation of `IEntityShapeLibrary` exists**.* And the seam is **not an uncalled
interface**: it fires every frame at `DebugPrimitiveRenderer2D.cs:410`. What is dead is the
**polymorphism** and the **name parameter**, not the call.

## 2. 🔴 Verified defects

| | Defect | Evidence |
|--:|---|---|
| **A** | **Every entity is the same cyan.** `prim.Color = new Rgba32(100, 220, 255, 255)` — a literal, for all entities in all subsystems. **Friend and hostile are indistinguishable on the map** while the simulation itself distinguishes them | `EntityPresentationGizmoShared.cs:92` |
| **B** | **CGF's shapes are `alpha 0`.** CGF calls `draw.DrawSemanticShape(...)` **directly** instead of the shared helper; the builder leaves `Color` at `default` = `(0,0,0,0)`, and the renderer uses `ToRaylibColor(prim.Color)`. ⚠ **Even the debug fallback is invisible** — the magenta "unknown profile" rectangle is drawn with `color.A`, i.e. CGF's zero alpha | `CgfEntityPresentationGizmo.cs:49` vs `DebugPrimitiveBuffer.cs:364-376`, `DebugPrimitiveRenderer2D.cs:197,437` |
| **C** | **CGF emits no pick box** — `EmitPickBox` is called by the IG and SimHost gizmos, not CGF ⇒ **CGF entities cannot be picked on the map**. This is the mechanism behind [UXI-11](UX_Issues.md#uxi-11) | `CgfEntityPresentationGizmo.cs:45-49` |
| **D** | **Damage visuals exist in one subsystem of three.** IG computes the condition mask from health; **CGF and SimHost hardcode `conditionMask: 0u`** ⇒ a damaged vehicle looks healthy in both | `IgEntityPresentationGizmo.cs:33-38` vs `CgfEntityPresentationGizmo.cs:49`, `SimHostEntityPresentationGizmo.cs:35` |
| **E** | 🔴 **[UXI-19](UX_Issues.md#uxi-19) is REAL — now verified.** See below |
| **F** | **`ResolveProfileId` returns 0 off a snapshot** — `if (view is not EntityRepository repo) return 0UL;` ⇒ `_fallback` ⇒ a grey rectangle, silently | `EntityPresentationGizmoShared.cs:48` |
| **G** | **The shape vocabulary is 4 hardcoded profiles** selected by a DIS bit-decode; the named half is unreachable. ⚠ `rotary_wing` reuses the `fixed_wing` geometry verbatim | `DefaultEntityShapeLibrary.cs:15-42,106-107` |
| **H** | **Zero test coverage of shape selection.** No test constructs `DefaultEntityShapeLibrary` or calls `GetShape`; the one test renderer overrides `DispatchShape` and never calls `base`, so it cannot reach the library | `GizmoPresentationTests.cs:23-34` |
| **J** | 🔴 **Rotating an entity in CGF does not visibly rotate it** — the rotator writes `SimTransform.Rotation`, the gizmo draws `NetworkTransform.LastRotation`. ⚠ **The pose-source fix alone is not the remedy** — see below | `EntityRotatorGizmo.cs:118-122` + `CgfSubsystem.cs:605` vs `CgfEntityPresentationGizmo.cs:27-35` |
| **J′** | 🔒 **RULED (user, 2026-08-12): CGF must not write `SimTransform` at all.** *"Similar to Delete — CGF does not own `SimTransform`, so it needs to send a request to SimHost, not change ECS directly. Editor owns all."* ⇒ making the pose source uniform turns *"never rotates"* into *"rotates, then snaps back on the next DDS sample"* (ingress overwrites `SimTransform` for non-owned entities, `:85-89`). **The real fix is a request path** — mirroring `DeleteEntity`'s `DestroyEntityCommand` publish-by-`NetworkId` (`CgfSubsystem.cs:777-785`). 🔴 **No such command exists for pose** ⇒ **[UXI-29](UX_Issues.md#uxi-29)**, out of scope here | `EntityDragGizmo.cs:155`, `EntityRotatorGizmo.cs:118` both `GetComponentRW<SimTransform>` |
| **I** | **`SelectionHighlightGizmo` is not registered in SimHost or CGF** — neither calls `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll` ⇒ no selection ring, and no `HealthBarGizmo` either | `SimHostApp.cs:337-345`, `CgfSubsystem.cs:498-500` vs `EditorSubsystem.cs:1100` |

### 🔴 UXI-19 verified — the Editor draws every entity twice

It was filed as *"two presentation gizmos may match one entity — **unverified**"*. The chain is now closed:

| Step | Evidence |
|---|---|
| Projector keys **differ by one component** — `IgEntityPresentationGizmo` needs `(SimTransform, NetworkIdentity, CullingState)`; `SimHostEntityPresentationGizmo` needs `(SimTransform, NetworkIdentity)` | `IgEntityPresentationGizmo.cs:13`, `SimHostEntityPresentationGizmo.cs:14` |
| Matching is a **superset** test, with no exclusivity | `BitMask512.HasAll(comp, rule.RequiredMask)` — `StatelessGizmoSystem.cs:104` |
| The Editor registers **both** registrars | `EditorSubsystem.cs:1094-1097` |
| Editor entities **do** get `CullingState` — `MapCullingSystem` sets it on **every entity with `SimTransform`** | `MapCullingSystem.cs:68-80`, module registered `EditorSubsystem.cs:971` |

⇒ In the Editor, every networked entity emits **two spatial anchors, two pick boxes and two semantic
shapes** per frame.

⚠ **And it defeats [UXI-09](UX_Feature_Map_Viewport.md).** The IG gizmo honours culling
(`if (!cull.IsVisible) return;`); the SimHost one does not. So the Editor computes culling and then draws
the culled entities anyway — **narrowing the cull rect in UXI-09 buys the Editor nothing until this is
fixed.**

## 2.5 🔒 RULED by the user, 2026-08-12 — two classes of map

> *"`StyleResolutionSystem` was meant for the **IG 2D map** (production 2D map, remotely controlled via a
> DDS API), based on DDS-network-provided styles. CGF, SimHost and Editor are **service-level maps**. We
> can and should share the infrastructure where it looks useful — generic, reusable, user-attractive and
> helpful features. The DDS feed (plus ECS components) is the source of the data for IG, while for the
> others the sources are mostly internal/local user inputs only (no remote DDS control)."*

| | **IG — production map** | **Editor · CGF · SimHost · ReplayBrowser — service maps** |
|---|---|---|
| Controlled by | **remote DDS API** + ECS | **local user input** + ECS only |
| Layer 1 — TKB / `VisualData` / `ForceId` | ✅ | ✅ **generic — share** |
| Layer 2 — `IgSymbolOverride` (DDS) | ✅ **IG's reason to exist** | ❌ **must not be required** |
| Layer 3 — operator / user config | ✅ | ✅ **mechanism is generic**; the specific toggles may differ |
| Consumption — tint, damage, shape name | ✅ | ✅ **generic — share** |

🔒 **So the sharing is of the *machinery and the generic layers*, not of the DDS pipeline.** This design
must not make a service map depend on a DDS concept it will never receive.

⭐ **And the Editor already proves the split works** — it registers `StyleResolutionModule`
(`EditorSubsystem.cs:972`) today, with layer 2 **inert** because nothing populates `IgSymbolOverride`
locally. The layered shape is already in production; it has simply never been named.

| Host | Runs the resolver today? |
|---|---|
| **IG** | ✅ `IgNodeBootstrapper.cs:171` — all three layers |
| **Editor** | ✅ `EditorSubsystem.cs:972` — layers 1 + 3, layer 2 inert ⇒ 🔴 **the tint is already computed and still discarded; the fix here is consumption only** |
| **CGF · SimHost · ReplayBrowser** | ❌ not registered — they need the shared resolver, without the DDS layer |

## 3. The design

🔒 **Connect the two pipelines. Do not build a third.** No change to `FDP/ExtDeps/GizmoMap` — the seam
there is an interface, and HROT implements it.

### 3.0 ⛔ SUPERSEDED by §3.8.9 — the resolver stays IG-only

> 🔒 **User ruling, `2026-08-30`:** *"no json cascading for CGF/SimHost/ReplayBrowser."* ⇒ ⛔ **do NOT lift
> `StyleResolutionSystem`, `MapUserConfig` or `IgSymbolOverride` out of `Hrot.IG`.** ⭐ The text below is the
> superseded plan, kept because §3.1 still cites its vocabulary. 📄 **Read §3.8.9 instead.**

#### ⛔ (superseded) The resolver becomes layered — IG adds one layer, service maps add none

```csharp
public interface IStyleSource                 // ordered; later sources overwrite earlier
{
    void Apply(ISimulationView view, Entity e, ref StyleDraft draft);
}
```

| Source | Registered by | Reads |
|---|---|---|
| `TkbStyleSource` | **all hosts** | `VisualData` (symbol, colour hex, **`MapShapeName`**) + `EntityInfo.ForceId` |
| `UserConfigStyleSource` | **all hosts** | the host's own config object (force-hostile, hide-labels, …) |
| **`DdsOverrideStyleSource`** | 🔒 **IG only** | `IgSymbolOverride` — the DDS-fed layer |

⇒ `StyleResolutionSystem` moves to the shared layer and takes its sources as a constructor argument. **Its
current three-layer body becomes IG's source list** — no behaviour change for IG, which is the host it was
written for.

| | |
|---|---|
| ✅ **Service maps never learn what DDS is** | they register two sources; the DDS type stays in IG |
| ✅ **IG keeps exactly today's behaviour** | same three layers, same order, now named |
| ✅ **The seam is the contribution point** the programme already uses elsewhere — descriptor + per-host binding |
| ⚠ `MapUserConfig` lives in `Hrot.IG.Systems` | the Editor already depends on it (`EditorSubsystem.cs:972`). Moving it out is part of this, not a separate cleanup |

### 3.1 The one assignment that fixes A, B and D

`EntityPresentationGizmoShared.DrawSemanticShape` reads the style that is already there:

```csharp
var (tint, condition) = view.HasComponent<ResolvedStyle>(entity)
    ? (style.Tint.ToRgba32(), ConditionFrom(style.DamageLevel))
    : (AffiliationColors.For(view, entity), 0u);       // ← falls back to ForceId, then to today's cyan
prim.Color = tint;
```

| Fixes | How |
|---|---|
| **A** | the tint is the merged, network-aware, affiliation-derived colour |
| **B** | CGF routes through the same helper ⇒ never `alpha 0` |
| **D** | condition comes from `ResolvedStyle.DamageLevel` — the *merged* damage, not IG's local component ⇒ all three subsystems agree |

⭐ **`ConditionFrom` keeps IG's existing thresholds** (`≥50` damaged, `≥90` immobile,
`IgEntityPresentationGizmo.cs:37-38`) — promoted, not re-invented.

### 3.2 One affiliation palette — and it is **not** the private one

⚠ **Correction to this design's own first draft**: the palette to keep is **`ResolvedStyleConstants`**, not
`EntityPlacementGizmo.GetAffiliationColor`. Three palettes exist and two disagree:

| Source | Friend | Neutral | Unknown | Verdict |
|---|---|---|---|---|
| **`ResolvedStyleConstants`** | `(0,100,255)` | green | white | 🔒 **authoritative** — it is what the resolver already writes into `ResolvedStyle.Tint` |
| `EntityPlacementGizmo.GetAffiliationColor` (private) | `(0,0,255)` | green | — | ⇒ **delete**, redirect to the constants |
| `MilStd2525Renderer.GetAffiliationColor` | blue | **yellow** | **green** | leave alone — it serves a primitive nothing emits; ⚠ note it before anyone revives that path ⇒ ⭐ **that revival is §3.8, and the note is resolved there (§3.8.8): entity-driven ⇒ `prim.Color`; SIDC-driven ⇒ this palette** |

⇒ After this, the placement ghost and the placed entity finally match, because both read one table.

### 3.3 One presentation gizmo, not three

`IgEntityPresentationGizmo` + `SimHostEntityPresentationGizmo` + `CgfEntityPresentationGizmo` →
**`EntityPresentationGizmo`**, projector key `(SimTransform, NetworkIdentity)`.

🔒 **RULED by the user, 2026-08-12:** *"CGF's `NetworkTransform` does not make sense to me. CGF is not
different from the others — all should use the same source (`SimTransform`) and the same rendering path for
the symbol (same gizmo, same DIS-type / TKB-derived shape, maybe just IG can override via DDS)."*

⚠ **This corrects an earlier draft of this very section** ([Correction 26](UX_Tasks_Detail.md#corrections)),
which kept CGF's preference as a "pose-source rule" on the false premise that the other hosts have no
`NetworkTransform` to prefer. **They do** — `SharedTranslatorPack` is created for **every** role
(`NedReplicationModule.cs:215-216`), so IG, SimHost and CGF worlds all carry it. That rule would have
**silently changed the production map's pose source**.

⇒ 🔒 **One pose source: `SimTransform`.** The preference is deleted, not migrated. Evidence it is
vestigial *and* harmful:

| | |
|---|---|
| **Both are written from the same decode** | `GeoSpatialIngressTranslator.Decode` sets `NetworkTransform` at `:75` and `SimTransform` at `:89` from the *same* `position`/`rotation` ⇒ numerically identical after ingress |
| **`SimTransform` is deliberately the local authority** | the `:85` guard — *"do NOT override `SimTransform` for locally-owned entities"* — exists precisely so local edits survive DDS loopback |
| 🔴 **The preference breaks CGF's own Rotate** | `EntityRotatorGizmo.CommitRotation` writes **`SimTransform.Rotation` only** (`EntityRotatorGizmo.cs:118-122`), and CGF wires that gizmo to its *Rotate* menu item (`CgfSubsystem.cs:605-608`) — while its gizmo draws `NetworkTransform.LastRotation`. ⇒ **rotating an entity in CGF does not visibly rotate it** |
| **The stated rationale points at deleted code** | the comment cites `CgfDebugVisualizerAdapter`, which was **removed** in the gizmo migration. The only recorded justification is a task instruction hedged with *"Optionally… CGF nodes **may** use `NetworkTransform` as a more current position source"* — a hypothesis, never verified |

#### ⭐ Is there already one gizmo that serves all hosts? — analysed, **no**

A repo-wide census of every gizmo emitting a per-entity visual: **the three presentation gizmos are the
only candidates, and none is a superset** — `Ig` adds culling + damage, `SimHost` adds the pick box,
`CGF` adds the (wrong) pose preference and loses both. ⇒ **merging them is correct and is this design's
§3.3**; there is nothing to adopt instead.

⚠ **But the merge does not close the real gap**, which is *registration*, not implementation:

| Subsystem | Per-entity gizmos registered |
|---|---|
| **IG · Editor** | full set — health bar, selection ring, rotation, vision cone, nav target, LOS, routes, areas, overlays, effects, … |
| **ReplayBrowser** | broad read-only set |
| **SimHost** | presentation + canvas menu + drag — **nothing else** |
| **CGF** | presentation + canvas menu — **nothing else** |

⇒ 🔒 **Out of scope here, worth its own issue**: SimHost and CGF get **no** health bars, selection rings,
headings, routes or overlays — and nothing records whether that is a deliberate capability choice or
drift. ⚠ It is the same shape as [UXI-13](UX_Issues.md#uxi-13) (four hand-maintained gizmo menu blocks):
**per-subsystem registration lists with no declared rationale.**

### 3.4 Culling moves to a visibility policy — ✅ **BUILT `2026-08-30` by `UXI-23` `S4`**

> ✅✅ **This section is AS-BUILT.** `CullingStateVisibilityPolicy` exists at
> `Hrot.Presentation/ScenarioEditor/Map/`, and the pack's default resolver attaches it to the entity
> projector. 📄 The full record, including two things §3.4 did not know: `UX_Feature_Map_Parity.md` §3.2f.

⚠⚠ **What §3.4 could not see, and why it sat unbuilt:**

| # | |
|---|---|
| **①** | 🔴 **The consumer half did not exist.** `StatelessGizmoSystem` called only `IsGloballyEnabled` — so the `CullingStateVisibilityPolicy` this section prescribes would have been **stored and silently ignored**. ✅ `S4` made the system honour `IsEntityVisible` |
| **②** | 🔴 **Reflection could not supply the policy.** ⛔ The code line below is a **hand-written registration site that `ST-031` deleted**. ✅ `S4` added a `Func<Type, IGizmoVisibilityPolicy?>` resolver to `RegisterAll`, so the wiring is `MapInteractionContext.VisibilityPolicyResolver` rather than a literal `Register` call |
| **③** | ⭐ **The double-match this section aimed at was already gone.** `S2a` merged the three entity projectors, so *"one gizmo, one key"* was banked by a different route |
| **⚠** | 🔴 **`CE-131`:** IG's culling input marks EVERY entity invisible *(viewport from projected screen corners)*. ⇒ **the setting defaults OFF**; this section is now correctly placed but still wired to a broken source |



```csharp
registry.Register(new EntityPresentationGizmo(), new CullingStateVisibilityPolicy());
```

`StatelessGizmoRegistry.Register` **already takes an `IGizmoVisibilityPolicy`** and defaults it to
`AlwaysVisiblePolicy.Instance` (`StatelessGizmoRegistry.cs:87`). Moving `CullingState` out of the
projector key and into the policy:

| | |
|---|---|
| ✅ **Kills the double-match by construction** — one gizmo, one key | defect E / UXI-19 |
| ✅ **Culling applies uniformly**, so UXI-09's narrowed rect pays off in the Editor | |
| ✅ Subsystems without culling register the same gizmo with the default policy | no fork |

### 3.5 Make the `shapeName` half real — the actual filed issue

```csharp
_shapeLibrary.GetShape(prim.ShapeName, prim.ProfileId)     // renderer, today: GetShape(null, …)
```

⭐ **The name already exists in the data.** `VisualData.MapShapeName` is authored in TKB/scenario JSON,
translated into the component (`PresentationTkbTranslator.cs:41`), and read by nobody. Its doc comment
states its purpose exactly: *"Optional explicit name of the 2-D map shape to render from the entity shape
library. When empty, the renderer selects a shape automatically based on `DISEntityType`."* — **that is
this design's specification, written before the code diverged from it.**

🔒 **Resolve the shape name in the layer that already resolves style.** The resolver gains one more merged
output — shape name — seeded from `VisualData.MapShapeName` by `TkbStyleSource`, ⭐ **so every host gets it
from local scenario data**; IG's `DdsOverrideStyleSource` can additionally override it at runtime. ⚠ Keep
it **distinct from `TextureName`**: that field carries `SymbolCode` / `TextureOverride` (a *texture*),
which is a different concept from a *vector shape profile*.

Carrying it to the renderer: `DebugPrimitive` is a **fixed-layout struct** with no room for a string, but
the buffer already interns strings by FNV-1a for menu bindings (`DebugPrimitiveBuffer.cs:378-385`).
🔒 **Use that same mechanism** — intern the name, carry the `uint` hash, resolve hash → name → profile in
the renderer.

⇒ Scenario authors get **`mapShapeName` working as documented on every map**, and IG additionally gets
runtime symbol control from the DDS feed — each host fed from the sources it actually has.

### 3.6 `HrotEntityShapeLibrary` — using the seam, without touching ExtDeps

```csharp
public sealed class HrotEntityShapeLibrary : IEntityShapeLibrary
{
    public void Register(string name, EntityShapeProfile profile);
    public void Register(ulong disType, EntityShapeProfile profile);
    public EntityShapeProfile GetShape(string? shapeName, ulong fallbackDisType);   // name → dis → default
}
```

Passed at the **four explicit injection points** (`EditorSubsystem.cs:1545`, `IgApplication.cs:826`,
`SimHostVisualization.cs:242`, `ReplayBrowserSubsystem.cs:237`) **and at CGF's**, which today omits the
argument entirely (`CgfSubsystem.cs:583`). It **delegates to the default for anything unregistered**, so
the shipped 4 profiles keep working.

⚠ **Make the omission impossible to repeat**: CGF's silent default came from an optional parameter with a
`??` chain three levels deep. The library should be a **required** argument at the layer constructor —
the default becomes something a host *chooses*, not something it *misses*.

### 3.7 Defect F — profile resolution off a snapshot

`ResolveProfileId` needs the live repo because `GetDisType` is an `EntityRepository` method. ⚠ **Do not
paper over it**: log once when the cast fails, and 📌 **carry the DIS type in the primitive instead** — the
gizmo already runs where the repo is available, so resolve early and pass the value. No new component.

### 3.8 ⭐⭐⭐ TWO SELECTABLE SYMBOL PATHS + ONE EMERGENCY FALLBACK — switchable per host; IG may drive it from its own cascade

<!--build-state: READY-TO-BUILD-->

> 🔒 **RULED by the user, `2026-08-30`:** *"i do not want to lose any of the renderers. they should become
> alternative symbol rendering paths, switchable (one active) per host, active path defined in hosts config."*
> 🔒 **Refined by the user, same day**, after measurement: *"i see basically just 2 meaningful selectable
> renderers … if those can not be used (missing data), the entity-real-sized wire rect with health bar is a
> good fallback (non-selectable, just an emergency fallback if nothing better exists)."*

⚠⚠ **An earlier draft of this section described FOUR selectable paths and a `nato2525` that draws a disc and
a label. That draft was WRONG on three counts and is SUPERSEDED — see `## ⛔ HISTORY`.**

#### 3.8.1 ⭐⭐ INVENTORY — **the queries, and the one the graph caught**

```
cli search_graph {"name_pattern":".*(ShapeLibrary|SymbolRenderer|ShapeRenderer|Symbology|MilStd).*"}
  → total 31, has_more false           # found SemanticShapeRenderer, which grep had missed
grep -rn "\[GizmoProjector"                                   → 16 projectors, 6 of them map-drawing
grep -rn "new DebugPrimitiveRenderer2D|DefaultEntityShapeLibrary()"  → 5 construction sites
grep -rn "SemanticShapeRenderer"                              → 0 callers
git log --all -S HealthBar --name-only                        → the deleted bar, below
```

| # | renderer | draws | where | verdict |
|---|---|---|---|---|
| **A** | `PerspectiveShapeRenderer` + `IEntityShapeLibrary` | oriented polyline silhouette, perspective exaggeration, `ShowWhen`/`HideWhen` gating | `GizmoMap.Presentation/Shapes/` | ⭐⭐ **selectable path `silhouette`** |
| **B** | inline `else` branch, `DebugPrimitiveRenderer2D.cs:432` | magenta wire rect | — | ⛔ **NOT a path — emergency fallback** (§3.8.4) |
| **C** | `MilStd2525Renderer` | filled disc + 4-char SIDC label | `GizmoMap.Presentation/Rendering/` | ⭐ **selectable path `nato2525`** — ⚠ **explicitly a STUB** |
| **D** | `SemanticShapeRenderer` (0 callers) | rect + **red X on damage**; magenta circle fallback | `GizmoMap.Presentation/Rendering/` | ⛔ **not a path — donates its damage-X to `nato2525`** |

⛔⛔ **C and D are not vestiges.** `.dev/_DONE/gizmos-1/batches/BATCH-20-INSTRUCTIONS.md:124-146` specifies both
by name as `GZ055` deliverables. ⭐ **And it calls C what it is:** *"Stub NATO symbol rendering … draw a filled
circle in the symbol's standard affiliation color … plus a text label with the first 4 chars of the SIDC code."*
🔒 **User ruling:** *"If nato renderer is a stub, ok, so be it, i never saw it working better, so it can stay a
stub but still a selectable entity renderer mode."* ⇒ ⚠ **`nato2525` is labelled STUB here so nobody re-files
its appearance as a defect.** ⛔ It is **not** the multi-polyline STANAG frame renderer; that remains unbuilt.

##### ⛔ Six projectors that are NOT switchable, by construction

`MapOverlayGizmo` *(`MapOverlayStyle`)* · `TacticalAreaGizmo` + `RouteGizmo` *(`TkbIdentity`)* ·
`EffectPresentationGizmo` · `ProjectilePresentationGizmo` · `EqsSensorGizmo`.
⭐⭐ **None emits `SemanticShape`**, so the path switch cannot reach them even by accident. 🔒 Matches the user's
ruling that *"specific map drawing entities with their own specific look & behavior … is not style-switchable."*

#### 3.8.2 ⭐⭐ How the silhouette polyline is chosen — **name first, DIS second, and the name half is dead**

📐 `DefaultEntityShapeLibrary.GetShape(shapeName, fallbackDisType)`:

```
shapeName non-empty AND registered  →  that profile
else  decode fallbackDisType: kind 1 + domain 1 → ground_vehicle
                              kind 1 + domain 2 → cat ≥ 20 ? rotary_wing : fixed_wing
                              kind 3            → humanoid
else  → EntityShapeProfile { Name = "_fallback" }        ⇒ the emergency box, §3.8.4
```

🔴 **The call site passes `null`** — `DebugPrimitiveRenderer2D.cs:410`: `GetShape(null, prim.ProfileId)`.
⇒ **only the DIS half ever runs**, and TKB's `VisualData.MapShapeName` is authored, translated into the
component, and read by nobody. 📄 **Making the name half real is §3.5**, unchanged and still owed.

#### 3.8.3 🔴 Why `IEntityShapeLibrary` is NOT the seam

⭐ The tempting design is *"a shape library per path"* — the seam exists and all five hosts already inject it.
⛔ **It cannot work:** `IEntityShapeLibrary` returns `EntityShapeProfile` = **polylines only**, and `nato2525`
needs a **filled** disc plus a **text** label. ⇒ 🔒 **the switch is at the DRAW call, not the shape lookup.**
⭐ `IEntityShapeLibrary` stays exactly as it is and becomes `silhouette`'s internal detail.

##### ⚠⚠ The argued, additive deviation from §3's *"no change to `FDP/ExtDeps/GizmoMap`"*

| option | verdict |
|---|---|
| ⭐⭐ **`IEntitySymbolPath` + one dispatch line in `DebugPrimitiveRenderer2D`** | ✅ **RECOMMENDED** — ~30 lines, **additive**, default `null` ⇒ byte-identical output. It exposes ExtDeps renderers that already exist; it forks nothing |
| ⚠ subclass and intercept the `virtual DispatchShape` | ⛔ **double-draws** — the `Fdp.Toolkit.Vis2D` wrapper runs its own dispatch loop **and then** `_inner.Render(...)` |
| ⛔ honour the constraint literally | ⛔ loses `nato2525`, which the user ruled against |

⇒ 🔒 **§3's constraint is amended narrowly:** *no **forking** of ExtDeps; an additive seam that exposes its own
renderers is allowed.*

#### 3.8.4 ⭐⭐ The emergency fallback — **not selectable, and already correctly sized**

🔒 **User:** *"the entity-real-sized wire rect with health bar is a good fallback (non-selectable, just an
emergency fallback if nothing better exists)"* — *"which is not a normal shape renderer anyone would want
selected intentionally."*

📐 **Measured: the sizing the user asks for is already what the code does.**
`EntityPresentationGizmoShared.TryGetVehicleDimensions` fills `LengthMeters`/`WidthMeters` from TKB, and the
renderer defaults `len = 5`, `wid = len * 0.5` when they are zero. ⇒ ⭐ **no work; only a demotion in the
design** — B stops being a *path* and becomes what every path falls back to when its data cannot resolve.

| path | falls back to the box when |
|---|---|
| `silhouette` | the profile resolves to `_fallback` *(no shape name, no DIS match)* |
| `nato2525` | ⚠ never — a stub disc always draws. Kept for symmetry if it later needs real data |

#### 3.8.5 ⭐⭐⭐ THE HEALTH BAR — **it existed, it was deleted, and it is being restored**

> 🔒 **User:** *"there was a nice implementation of the health bar; maybe it was lost when gizmo renderer took
> over?"* — 📐 **Measured: yes, exactly that.**

| | |
|---|---|
| ⭐ **built** | `NedVisualizerAdapter` *(file `SstVisualizerAdapter.cs`)*; made always-on by **`e726734cc` "fix: health bar on IG map"**, `2026-04-22` |
| 🔴 **deleted** | **`5ce023677` "GZ059: eradicate legacy IVisualizerAdapter/EntityRenderLayer rendering stack"**, `2026-05-08` — 268 lines + 95 of constants, with the whole legacy adapter stack |
| ⛔ **never replaced** | `HealthBarGizmo` emits `DrawEntityBadge("87%")`. ⚠⚠ **It reads `BarWidth`/`BarHeight` from settings and DISCARDS them — and has done so since its first commit** *(`HealthBarGizmoInstance`, BATCH-07)*. ⇒ the badge never *replaced* the bar; it was written beside it and the bar was deleted underneath |

##### ⭐ The recovered behaviour — `e726734cc`, verbatim intent

```csharp
Raylib.DrawRectangleV(pos, new Vector2(width, height), new Color(30,30,30,200));  // dark backing
float fillWidth = width * (health / 100f);                                        // fill lerps on %
Raylib.DrawRectangleV(pos, new Vector2(fillWidth, height), fill);
Raylib.DrawRectangleLinesEx(new Rectangle(pos.X,pos.Y,width,height), 1f, Color.White);
```

🔒 **Three DISCRETE colours** — `green ≥ 66`, `yellow ≥ 33`, else `red` — with the **fill WIDTH** proportional
to the percentage. ⚠ **Confirmed by the user against a smooth-lerp alternative:** *"probably you are right and
it was just 3 distinct colors health bar lerp on percentage — that was what i need now as well."*
📐 Original geometry: `30 × 6` px, `25` px above the entity.

##### ⭐⭐ It needs no new machinery — **two `DrawBox2D` calls**

📐 `IDebugDrawBuilder.DrawBox2D(center, extents, color, angleDeg, thickness, sizeMode, target, layer,`
**`fillColor`**`, style, anchorId, subElementId)`, and the renderer honours an explicit fill
*(`prim.FillColor.A > 0`)* **plus** an outline on the same primitive, supports `SizeMode.ScreenPixels`, and
resolves `Box2D` in `CoordinateSpace.EntityLocal` against the entity's `SpatialAnchor` in pass 2.

⇒ ⭐ **backing box + fill box, ~15 lines in `HealthBarGizmo.Draw`, no primitive change, no ExtDeps change.**
⚠ The one thing to settle at build time is the vertical offset in the EntityLocal frame.
⭐ **And `BarWidth`/`BarHeight` stop being read-and-discarded** — the restored bar is the first code that uses them.

#### 3.8.6 ⭐⭐ Decorations are PER PATH — and the switch reuses `S4`'s policy resolver

🔒 **User:** *"the red X for destroyed entities should be part of all renderers not having the health bar (i.e.
the nato 2525); the silhouette should have the health bar rendered at the top."*

| decoration | `silhouette` | `nato2525` | box fallback | owner |
|---|---|---|---|---|
| **health bar** | ✅ | ⛔ | ✅ | `HealthBarGizmo` — a **separate projector** |
| **red X on destroyed** | ⛔ | ✅ | ⛔ | the **path itself**, in the renderer |

⭐⭐ **Why the split.** A path runs inside the renderer and sees only primitives — it cannot query ECS, so it
cannot compute a health percentage; but it *does* receive `ConditionMask`, which is all the X needs.
⭐ **The bar therefore stays a projector**, and *"which paths get it"* is expressed with the machinery `S4`
already built: a `IGizmoVisibilityPolicy` on `HealthBarGizmo` whose `IsGloballyEnabled` is false when the
active path is `nato2525`. ⛔ **No new mechanism.**
📐 The X's source already exists — `SemanticShapeRenderer` draws exactly it on `ConditionMask` bit 0.

#### 3.8.7 ⭐⭐ The seam — class diagram

```mermaid
classDiagram
    class IEntitySymbolPath {
        <<interface>>
        +string Name
        +Draw(prim, worldX, worldY, rot, len, wid, color, cond, zoom)
    }
    class SilhouettePath
    class Nato2525Path
    class BoxFallback

    class DebugPrimitiveRenderer2D {
        -IEntityShapeLibrary shapeLibrary
        -IEntitySymbolPath symbolPath
        +Render(primitives, camera, zoom)
    }
    class PerspectiveShapeRenderer
    class MilStd2525Renderer
    class SemanticShapeRenderer
    class IEntityShapeLibrary {
        <<interface>>
        +GetShape(shapeName, fallbackDisType)
    }
    class DefaultEntityShapeLibrary

    class SymbolPathFactory {
        +Create(name) IEntitySymbolPath
    }
    class GizmoSettingsRegistry
    class DebugGizmoLayer

    class HealthBarGizmo {
        +Draw(view, entity, drawBuilder)
    }
    class IGizmoVisibilityPolicy {
        <<interface>>
        +IsGloballyEnabled
    }
    class PathScopedPolicy

    IEntitySymbolPath <|.. SilhouettePath
    IEntitySymbolPath <|.. Nato2525Path
    SilhouettePath ..> PerspectiveShapeRenderer : delegates
    SilhouettePath ..> IEntityShapeLibrary : looks up
    SilhouettePath ..> BoxFallback : profile is _fallback
    Nato2525Path ..> MilStd2525Renderer : delegates
    Nato2525Path ..> SemanticShapeRenderer : damage X
    IEntityShapeLibrary <|.. DefaultEntityShapeLibrary

    DebugPrimitiveRenderer2D o-- IEntitySymbolPath : 1 active
    DebugPrimitiveRenderer2D o-- IEntityShapeLibrary
    DebugGizmoLayer *-- DebugPrimitiveRenderer2D
    SymbolPathFactory ..> GizmoSettingsRegistry : reads map.symbology.path
    DebugGizmoLayer ..> SymbolPathFactory : per host, at construction

    IGizmoVisibilityPolicy <|.. PathScopedPolicy
    HealthBarGizmo --> PathScopedPolicy : off when nato2525
    PathScopedPolicy ..> GizmoSettingsRegistry : same key

    note for PerspectiveShapeRenderer "EXISTS - GizmoMap.Presentation/Shapes/"
    note for MilStd2525Renderer "EXISTS, a STUB by its own spec - disc plus SIDC label"
    note for SemanticShapeRenderer "EXISTS, 0 callers - only its damage X is reused"
    note for BoxFallback "EXISTS inline at DebugPrimitiveRenderer2D.cs:432 - demoted to fallback"
    note for HealthBarGizmo "EXISTS but draws a TEXT BADGE - restore the bar from e726734cc"
    note for IGizmoVisibilityPolicy "EXISTS - S4 made StatelessGizmoSystem honour it"
```

#### 3.8.8 ⭐⭐ Selection and one frame — sequence diagram

```mermaid
sequenceDiagram
    autonumber
    participant Host as Host boot
    participant Cfg as IG only - MapInteractionConfig
    participant Settings as GizmoSettingsRegistry
    participant Factory as SymbolPathFactory
    participant Layer as DebugGizmoLayer
    participant Rend as DebugPrimitiveRenderer2D
    participant Path as IEntitySymbolPath
    participant Ent as EntityPresentationGizmo
    participant Bar as HealthBarGizmo

    Host->>Settings: write "map.symbology.path" from host config
    opt IG only
        Cfg->>Settings: styles.globalStandard, MapId over MapGroupId over global
    end
    Host->>Factory: Create(settings)
    Factory-->>Host: SilhouettePath
    Host->>Layer: ctor(buffer, camera, shapeLibrary, symbolPath)
    Layer->>Rend: ctor(shapeLibrary, symbolPath)

    loop every PostSimulation frame
        Ent->>Rend: SemanticShape prim - ProfileId, len, wid, cond, Color
        Bar->>Bar: policy off when path is nato2525
        Bar->>Rend: two Box2D prims - backing plus fill
        Rend->>Rend: pass 1 cache SpatialAnchor
        Rend->>Rend: pass 2 resolve world pose
        Rend->>Path: Draw(prim, world, rot, len, wid, prim.Color, cond, zoom)
        Path-->>Rend: silhouette, or nato stub plus red X, or box fallback
    end
```

#### 3.8.9 ⭐ Configuration — **host-scoped for everyone; IG additionally has its own cascade**

| key | values | default |
|---|---|---|
| **`map.symbology.path`** | `silhouette` · `nato2525` | 🔒 **`silhouette`** — byte-identical to today on every host |

⭐ Read through **`GizmoSettingsRegistry`**, the same per-host injectable settings object
`EntityPresentationGizmoSettings` uses *(📄 `UX_Feature_Map_Parity.md` §3.2c)*.

##### 🔒 The JSON cascade is an **IG SPECIALITY** — ⛔ not a shared feature

> 🔒 **User ruling, `2026-08-30`:** *"we can ignore the json cascade for cgf and simhost; that was used when IG
> was representing a stylable map; so the style cascading could likely stay as IG subsystem speciality which may
> affect some configs of shared map rendering of IG (like switching the entity rendering style), but is not a
> shared feature for other subsystems' maps — no json cascading for CGF/SimHost/ReplayBrowser."*

⇒ ⛔⛔ **This REVERSES §3.0**, which proposed lifting `StyleResolutionSystem` to every host with a per-host source
list. ⭐ §3.0 is **SUPERSEDED**: the resolver, `MapUserConfig`, `IgSymbolOverride` and the cascade stay in
`Hrot.IG`. ⭐ **CGF · SimHost · ReplayBrowser · Editor get the shared renderer and a plain host-config key.**

##### ⭐⭐ Where IG's per-map style already lives — **measured, and it is not where we guessed**

🔒 **User:** *"StyleParamJson was very likely for a mapId … the IG subsystem instance should be assigned a
concrete mapId and mapGroupId and also the last received per-map json style."*
⭐⭐ **The memory is right; the carrier is a different field, and it is BETTER.**

| carrier | scope | state |
|---|---|---|
| ⭐⭐⭐ **`MapInteractionConfig.ConfigurationJson`** | **per map** — `[DdsKey] MapId` + `[DdsKey] MapGroupId`, documented tiering **`MapId > MapGroupId > global (both 0)`** | ✅ **on the wire, and IG ALREADY PARSES IT** *(`IgApplication.cs:~3250`)* — ⚠ but only the `"interaction"` key *(`PLACEMENT`, `AREA_AUTHORING`)*. 🔴 **Its own doc comment says *"Keys include: 'view' (layers), 'tools' (active cursor), `"styles"`'"* — and `"styles"` is never read** |
| `MapConfigStatus.CurrentSettingsJson` | per map instance | ⭐ *"the FULL current configuration state"* — the *"last received per-map style"* the user remembered |
| ⚠ `MapEntitySymbol.StyleParamsJson` | **per ENTITY** — *"colorOverride", "forceLabel", "halo"* | ⛔ **NOT the per-map style.** This is the entity-instance override the user ruled unnecessary |

⇒ ⭐⭐⭐ **IG's mount point is `ConfigurationJson` → `"styles"` → `globalStandard`** *(the spec's own name, line
1543 of `map-specs.md`)*, which writes `map.symbology.path` into `GizmoSettingsRegistry`. 🔒 **The shared
renderer never learns what DDS is** — it reads one settings key, and on IG something else happens to write it.
⭐ **~30 lines added to a parser that already exists**, not new machinery.

#### 3.8.10 ⭐ The truncated cascade — **what to delete, what to KEEP**

📐 **Measured: of the four fields `StyleResolutionSystem` merges, only `StyleSetId` is ever populated**, and only
as one of four hardcoded affiliation tokens. The ingress translator hardcodes `TextureOverride = null` and never
sets the other two.

| field | verdict |
|---|---|
| `IgSymbolOverride.TextureOverride` | ⛔ **DELETE** — written `null` at both ingress sites, genuinely unread |
| `IgSymbolOverride.LabelOverride` | ⛔ **DELETE** — never set |
| ⭐⭐ **`IgSymbolOverride.ShowHistory`** | ✅✅ **KEEP.** 🔴 It gates `ResolvedStyle.ShowTrail`, which gates `HistoryRecordingSystem`, which fills `HistoryTrail`. Since ingress never sets it, **IG's entire movement-trail feature is dead by construction, not by design** — deleting the field deletes the trail's only intended on-switch. 🔒 **User: *"Let's keep the history trail."*** ⇒ ⭐ **wiring it is its own row** |
| ⚠ `MapEntitySymbol.StyleParamsJson` | ⚠ **KEEP ON THE WIRE.** `MapEntitySymbol` is `[DdsTopic]` + `[DdsIdlFile("hrot-map-desc")]` — an **external contract with ExCon/IOS**. ⛔ Removing the C# component fields is internal and free; removing the wire field changes the IDL. ⭐ Leave it unparsed and say so here, so the next reader does not re-file it |

#### 3.8.11 ⭐ Palette — **unchanged**

🔒 **User, `2026-08-30`:** *"Ad palette, ok, let's use what is there now."* ⇒ ⭐ `ResolvedStyleConstants` stays
authoritative: Friend `(0,100,255)` · Hostile `(255,0,0)` · Neutral green · Unknown white.
⭐ §3.2's verdicts are unchanged, and its note *"before anyone revives that path"* resolves as: an **entity** on
`nato2525` is coloured by `prim.Color` *(so `ResolvedStyleConstants` wins)*; a genuine **SIDC** primitive keeps
`MilStd2525Renderer`'s own palette, which is what makes it a distinct path.

#### 3.8.12 ⭐ Sequencing

| step | what | depends on |
|---|---|---|
| **1** | 🔒 **`CE-125` / §3.1** — the affiliation-derived tint reaches `prim.Color` | — |
| **2** | ⭐ **the health bar restoration** *(§3.8.5)* — self-contained, no seam needed | — |
| **3** | ⭐ **the path seam + two paths + the fallback demotion + the config key** | — |
| **4** | ⚠ **IG's `"styles"` parsing** *(§3.8.9)* — IG-only | 3 |

⚠⚠ **Correcting an earlier statement in this design:** step 1 was called a **prerequisite** for the seam. 📐 It
is not — both compile independently. ⭐ But until step 1 lands every path renders in the literal cyan of
`EntityPresentationGizmoShared.cs:92`, so **step 1 is what makes step 3 worth looking at.**

#### 3.8.13 ⭐ Acceptance

| # | |
|---|---|
| ① | ⭐⭐ **Default is byte-identical** — with no key set, every host renders exactly what it renders today; a rail asserting `symbolPath: null` takes the pre-existing code path |
| ② | ⭐ **Each path draws its own thing** — a rail per path against a capturing double, not Raylib: `silhouette` emits the profile's polylines; `nato2525` emits the disc **plus the red X when `ConditionMask` bit 0 is set** |
| ③ | ⭐ **The fallback is reachable and is NOT selectable** — a rail feeding an unresolvable `ProfileId` gets the entity-sized wire box; and a rail asserting `SymbolPathFactory.Create("box")` is **not** a valid path name |
| ④ | ⭐⭐⭐ **The health bar is a BAR** — a rail asserting two `Box2D` primitives with `FillColor.A > 0`, the fill box's width proportional to health, and the three discrete colours at the `66`/`33` boundaries. ⛔ A rail that only asserts "a primitive was emitted" is vacuous — the badge would satisfy it |
| ⑤ | ⭐ **Decorations follow the path** — a rail asserting `HealthBarGizmo` emits nothing when the active path is `nato2525`, and emits when it is `silhouette` |
| ⑥ | ⭐⭐ **Config selects per host** — two `GizmoSettingsRegistry` instances, two different `IEntitySymbolPath` types |
| ⑦ | ⭐ **Nothing is lost** — `MilStd2525Renderer`, `SemanticShapeRenderer`, `PerspectiveShapeRenderer` and the `case MilStd2525:` demo dispatch all still exist. ⛔ A diff deleting any of them fails this section |
| ⑧ | ⚠ **`ShowHistory` survives** — a rail asserting the field still exists and still reaches `ResolvedStyle.ShowTrail` |

## 4. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 10.1 | `AffiliationColors.For(Friend/Hostile/Neutral)` = blue / red / green, matching `ForceId`'s documentation | H |
| 10.2 | Entity with `ResolvedStyle` → the emitted primitive's `Color` **is** `style.Tint` | H |
| 10.3 | Entity without `ResolvedStyle` → falls back to `EntityInfo.ForceId`; without that, today's cyan | H |
| 10.4 | 🔴 **CGF's semantic shape is never `alpha 0`** | H |
| 10.5 | CGF emits a **pick box** | H |
| 10.6 | `DamageLevel` 0 / 50 / 90 → condition mask `0` / `Damaged` / `Damaged\|Immobile`, in **all three** subsystems | H |
| 10.7 | 🔴 An entity with `(SimTransform, NetworkIdentity, CullingState)` in an Editor-style registry emits **exactly one** semantic shape — the UXI-19 regression guard | H |
| 10.8 | An invisible (`CullingState.IsVisible = false`) entity emits **nothing**, via the visibility policy | H |
| 10.9 | 🔒 **Pose comes from `SimTransform` in every host**, even when `NetworkTransform` is populated and differs — the one-source guard | H |
| 10.23 | 🔴 CGF's *Rotate* **emits a request and writes no ECS** — the drawn symbol follows once the owner replies. ⚠ **Depends on [UXI-29](UX_Issues.md#uxi-29)**; until then CGF's *Rotate* stays as-is and this design does **not** claim to fix it | I |
| 10.10 | `HrotEntityShapeLibrary` returns a registered profile by name; by DIS id; and **delegates to the default** when unregistered | H |
| 10.11 | 🔴 **`VisualData.MapShapeName` reaches the library** — a scenario naming `mapShapeName` resolves the **named** profile, not the DIS fallback. The field's own doc comment becomes true | H |
| 10.12 | 🔒 **A service map resolves style with no DDS source registered** — `IgSymbolOverride` present on an entity is **ignored** when `DdsOverrideStyleSource` is absent | H |
| 10.20 | IG's source list reproduces **today's** 3-layer merge exactly — the no-behaviour-change guard for the production map | H |
| 10.21 | A DDS shape/style override changes the resolved profile end-to-end **in IG** | H |
| 10.22 | Editor: `ResolvedStyle` is already populated before this change ⇒ the tint appears with **no new module registered** | H |
| 10.17 | Empty `MapShapeName` → falls back to the DIS decode, exactly as documented | H |
| 10.18 | The three affiliation palettes collapse to one — the placement ghost's Friend colour **equals** `ResolvedStyleConstants.Friend*` | H |
| 10.19 | The shape library is a **required** constructor argument — CGF cannot silently default again | H |
| 10.13 | `ResolveProfileId` off a **snapshot** view logs once and still yields a usable profile | H |
| 10.14 | Placement ghost and the placed entity render the **same colour** | I |
| 10.15 | Two entities, opposing `ForceId` → visibly different colours on the map in every subsystem | I |
| 10.16 | Editor: an entity is drawn **once**, and off-screen entities are not drawn | I |

**20 H · 4 I · 0 V.** ⚠ Note 10.10-10.11, 10.17: **there is currently no test anywhere that calls
`GetShape`** (defect H), so these are the first coverage this logic has ever had. 🔒 **10.20 is the
load-bearing one** — IG is the production map, and this design must be provably invisible to it.

## 5. 🔒 Out of scope

| | |
|---|---|
| MIL-STD-2525 / APP-6 symbol set | a symbol *library*, not the plumbing; §3.6 makes it addable without further design |
| Texture/sprite symbols | `ResolvedStyle.TextureName` is carried, not yet rendered as a texture |
| Labels on the map | `ResolvedStyle.LabelText` is resolved and unused — ⚠ **a second unconsumed field**; own issue |
| `ShowSensors` / FOV cones | same — resolved, unconsumed |
| Selection highlight's **appearance** | separate gizmo, unaffected — ⚠ but its **absence in SimHost/CGF** (defect I) is a registration bug worth its own issue |
| StrideMock's red circles | its renderer call is commented out pending `SM-009`; out of this issue's reach |
| ExCon's own map | DDS-only, no ECS ([ruling 16](UX_RESUME_INTERACTION.md)) — it *produces* the override this design consumes |

## 6. Risks

| | |
|---|---|
| ⚠ **Everything on the map changes colour** | that is the feature — but it is the most visible change in the programme so far. ⭐ Recommend it lands with [UXI-09](UX_Feature_Map_Viewport.md) so the map's visual change is one event, not two |
| ⚠ **Collapsing three gizmos touches all three subsystems** | 10.7-10.9 are the guards; the pose-source rule (§3.3) is the one real behavioural merge |
| ⚠ **Interning a name per entity per frame** | the intern map is idempotent and allocates only on first sight — but ⚠ **measure**: this runs per visible entity. If it costs, cache the hash in `ResolvedStyle` at resolution time instead |
| ⚠ **`ResolvedStyle` is IG-namespaced** (`Hrot.IG.Components`) while becoming a cross-subsystem contract | it already **is** one — it lives in the shared `Hrot.Core` project and the Editor registers it (`EditorSubsystem.cs:601`). Promotion is a **namespace** rename, not a move. ⚠ Same for `MapUserConfig`, which the Editor already reaches into `Hrot.IG.Systems` to get |
| 🔒 **Touching IG is touching the production map** | per [ruling 20](UX_RESUME_INTERACTION.md), IG is the DDS-controlled production surface. The refactor must be behaviour-preserving there — 10.20 exists for exactly this, and IG's source list should be reviewed as its own step rather than folded into the service-map work |
| ⚠ **Layer-3 toggles may not generalise** | *hide labels* is generically useful; *operator force-hostile* is a production-map concept. Register per host — do not assume the service maps want IG's flag set |
| ⚠ **UXI-19's fix changes the Editor's draw count** | half the primitives disappear. If anything depends on the duplicate (nothing found), it will surface here |

## ⛔ HISTORY

### ⛔ HISTORY — §3.8's FIRST DRAFT (`2026-08-30`, superseded the same day)

⛔ **Do not quote it. Three claims were wrong**, each corrected by the user against measurement:

| the draft said | ⭐ the truth |
|---|---|
| **FOUR selectable paths** — `silhouette` · `box` · `profile` · `nato2525` | 🔒 **two selectable + one emergency fallback.** The box *"is not a normal shape renderer anyone would want selected intentionally"*, and `SemanticShapeRenderer` contributes only its damage-X |
| `nato2525` is a real symbol path that *"becomes correct"* once `CE-125` lands | ⚠ it is a **STUB by its own spec** *(`BATCH-20-INSTRUCTIONS.md:126`)*, kept selectable **as a stub** — 🔒 *"a disc is nothing anyone would want"* |
| the health bar was out of scope, and `HealthBarGizmo` merely *"draws no bar"* | 🔴 **a real bar existed and was DELETED** by `5ce023677` — §3.8.5. Restoring it is part of this design |

⚠ **It also proposed a palette change** *(gray neutral, magenta unknown)*; 🔒 the user ruled *"let's use what is
there now"* ⇒ §3.8.11.
⚠ **And it asked whether the JSON cascade should be shared**; 🔒 ruled **IG-only** ⇒ §3.8.9.
