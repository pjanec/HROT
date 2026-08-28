<!--STATUS
state: LIVE
build-state: NOT-BUILT
verified: 2026-08-28 (coordinator source scan)
current-answer: NOT-BUILT (design only) -- confirmed independently 2026-08-28: grep finds ZERO
  occurrences of MapInteractionPack/MapInteractionContext. No shared pack; IG fork
  (HandleContextMenuActionById) intact. READ SECTION 2b FIRST: section 2's per-host baseline has
  INVERTED -- CGF now emits the full entity gizmo set (CgfSubsystem.cs:928) and SIMHOST emits none,
  which is the host a user reported broken (CE-123). Section 3.3's work split is stale accordingly.
  Section 5 is CLOSED (the seven commanding ids deferred to UXI-32/Q29; Properties+Teleport unblocked).
-->
# Feature design — map-interaction parity

> **Design for [UXI-23](UX_Issues.md#uxi-23) · drafted 2026-08-12.** Absorbs
> [UXI-22](UX_Issues.md#uxi-22) and UXI-04 step 5. **Status: ❌ NOT-BUILT (design only) — no shared `MapInteractionPack`; IG's fork (`HandleContextMenuActionById`) intact; CGF registers no `GlobalActionRegistry` (CE-051/E3 shared only center/select across Editor+CGF).**
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

## 2b. ⚠⚠ MEASURED `2026-08-28` — **§2's PER-HOST BASELINE HAS INVERTED. CGF is fixed; SIMHOST is now the empty one.**

🔒 **User visual test on `--mode all`:** *"The entities were not shown in the SimHost perspective. in
Scenario perspective, map was showing them."* ⇒ 📐 **confirmed by `GET /panels/_gizmo?max=4000`** after a
live load of `hill-attack`:

| perspective | primitives | non-`Line` | verdict |
|---|---:|---:|---|
| **`Scenario`** *(CGF)* | 739 | **69** — `Box2D 8`, `Arrow 12`, `Text 8`, `SemanticShape 16`, `SpatialAnchor 16` | ✅ **the richest entity output of the three** |
| **`IG`** | 714 | **104** — `Box2D 24`, `SpatialAnchor 24`, `SemanticShape 24`, `EntityBadge 6` | ✅ |
| 🔴 **`SimHost`** | 605 | 🔴 **3** — `LayerControlMask`, `MainMenuBinding`, `ContextMenuBinding` + `Line 602` | 🔴 **grid and shell bindings only — ZERO per-entity primitives** |

⇒ ⛔⛔ **§2's table and §3.3's work split are STALE IN BOTH DIRECTIONS and must not be planned against
as-written:**

| §2/§3.3 says | 📐 measured now |
|---|---|
| *"CGF is missing `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar` entirely"* · *"CGF: the whole pack"* | ⚠ **CGF now calls `GizmoReflectionRegistrar.RegisterAll` at `CgfSubsystem.cs:928`** and emits the full entity set. ⭐ Fixed by the cgf==editor correctives *(`CE-016`, and the catalog-contributor round)* **after this design was written** |
| *"SimHost: the missing 7 ids + `Common.Diagnostics` registrar + rubber-band state"* — i.e. a **partial** host | 🔴 **SimHost emits NO entity primitives at all** — worse than the table implies, and it is the host a user actually reported as broken |

⭐⭐ **Why this strengthens the design rather than weakening it.** The two hosts swapped places **without
either behaviour being intended**, because each wires its own map independently — which is precisely
§3.2's argument for `MapInteractionPack`. ⇒ 🔒 **a per-host baseline table is a snapshot that rots; the
pack is what makes the question unaskable.** ⚠ **So do not re-derive the table before building — build the
pack and let `23.1`-`23.3`'s parameterised cases hold it.**

⚠ **The SimHost gap itself is NOT yet root-caused** — 📄 **`CE-123`** carries the elimination table
*(camera, missing projector class, un-called `RegisterAll`, two buffers, boot listener asymmetry, a missing
`gizmoByPerspective` entry and a null `GizmoController` are ALL disproved by measurement)*. ⭐ Open leads:
whether `_dataDrivenGizmoSystem` is **scheduled** on the cluster path, and the `LayerControlMask` primitive
as a possible emit-time filter.

⭐ **One prerequisite is better than §7 claims.** §7 orders `UXI-10 → UXI-11 → UXI-29 → this` and warns that
binding `Rotate` in CGF before `UXI-29` hands it an action writing a component it does not own.
📐 **Measured: `UXI-29`'s mechanism IS BUILT** — `IEntityComponentWriter`
*(`FDP/Toolkits/Fdp.Toolkits/Replication/Patching/IEntityComponentWriter.cs:40`)* and `EntityWriteRouter`
*(`.../Replication/Attributes/EntityWriteRouter.cs:29`)*, adopted by **IG** *(`IgApplication.cs:755,760`)*,
**SimHost** *(`SimHostApp.cs:362,397`)* and **Editor** *(`EditorSubsystem.cs:1621,1651`)*.
⇒ ⭐ **the risk narrows from "UXI-29 is unbuilt" to one concrete fact: `CGF` is NOT in the adopter list**, so
CGF must route its writes through `EntityWriteRouter` before it binds a write-action. ⚠ The `UXI-29` doc
header still reads *"designed"* — ⛔ **the code is ahead of it here.**

## 2c. 📐 REFRESHED INVENTORY `2026-08-28` — **the five-host wiring matrix, re-measured**

⭐ Replaces §2's table for planning purposes. `Y` = the symbol appears in that subsystem's non-test
production sources.

| host | `GizmoReflectionRegistrar` | `GlobalActionRegistry` | `SelectionInteractionSystem` | `RubberBandState` | `CanvasMenuUpdateSystem` | `DataDrivenGizmoSystem` | `GizmoExecutionController` |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **Editor** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **CGF** | ✅ | 🔴 | 🔴 | 🔴 | ✅ | ✅ | ✅ |
| **IG** | ✅ | 🔴 *(fork)* | ✅ | 🔴 | ✅ | ✅ | ✅ |
| **SimHost** | ✅ | ✅ | ✅ | 🔴 | ✅ | ✅ | ✅ |
| **ReplayBrowser** | ✅ | ✅ | ✅ | ✅ | 🔴 | ✅ | 🔴 |

⭐⭐ **What this CHANGES in the design:**

| | |
|---|---|
| ✅ **§2's headline finding is RETIRED** | *"CGF is missing `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar` entirely"* — **all five hosts now register the gizmo projectors.** ⇒ §3.3's *"CGF: the whole pack"* is wrong; CGF's remaining gap is the **action/selection half**, not the gizmo half |
| ⭐ **the blind rubber band is WIDER than recorded** | §2 named SimHost and IG; measured, **CGF too** — only Editor and ReplayBrowser carry a `RubberBandState`. ⇒ acceptance `23.7` covers three hosts, not two |
| 🔴 **NEW — `ReplayBrowser` has NO `GizmoExecutionController`** | ⇒ `PerspectiveCoordinatorSystem:76-78`'s `incoming.GizmoController?.AddListener()` **silently no-ops** for it *(the `?.` swallows it)*. ⚠ Not in the design; it means the perspective-switch gizmo handover cannot apply to that host at all |
| ⛔⛔ **AND THE ONE THAT MATTERS MOST: `SimHost` HAS EVERY SYMBOL EDITOR HAS except `RubberBandState`, AND STILL EMITS NO ENTITY GIZMOS** *(§2b)* | ⇒ 🔒 **the SimHost gap is NOT a missing registration** — which is consistent with all seven hypotheses eliminated in `CE-123`. It is something about **execution/scheduling in the cluster composition**, and ⭐⭐ **that is exactly the class of fault a per-host wiring matrix CANNOT see** — five hosts each wire this privately, so "does it actually run?" is not answerable by inspection. 📌 **The strongest argument in this document for the pack, and it was found by a user's eyes, not by any table.** |

## 3. The design

### 3.1 🔒 One dispatch mechanism

Retire IG's `switch` into the shared `GlobalActionRegistry`. IG's six local behaviours become six
registered handlers; `HandleContextMenuActionById`/`ExecuteLocalContextAction` go away.

⚠ **IG keeps its ExCon forwarding** — that is a *fallback for unhandled ids*, not a dispatch mechanism,
and it stays as the registry's miss path.

### 3.0 🔴🔴🔴 THE PACK MUST OWN GIZMO **EXECUTION**, NOT ONLY REGISTRATION — *root-caused `2026-08-28`*

> ⭐⭐⭐ **User:** *"we are unifying the map rendering across hosts, so whatever it takes; and rendering =
> gizmos so how can we NOT unify gizmo execution?"*

🔒 **Answer: we cannot — and the SimHost map failure is the proof, because the fault is ENTIRELY on the
execution side.** ⛔ §3.2's pack as drafted is a **registration** entry point *(registrars, the action
registry, `SelectionInteractionSystem`, `CanvasMenuUpdateSystem`, the layer control)*. It names none of the
machinery below, and **that is exactly where the bug lives.**

#### The two halves, concretely

| | what it is | shared today? |
|---|---|---|
| **REGISTRATION** | *which* projectors exist — `GizmoReflectionRegistrar.RegisterAll` populating `GizmoRegistry`/`StatelessGizmoRegistry` | ✅ **yes, all five hosts** *(§2c)* |
| 🔴 **EXECUTION** | *whether they run this frame* — the `DebugPrimitiveBuffer`, `DataDrivenGizmoSystem`, `GlobalGizmoManager`, the **`TogglablePostSimulationGroup`** that wraps them, the **`GizmoExecutionController`** gate over it, its placement in the host's schedule, and `buffer.EndFrame(dt)` | ⛔ **no — hand-wired per host, five different ways** |

#### 📐 THE ROOT CAUSE — a two-part interaction, neither part visible from one host

**Part 1 — the gate's counter is not clamped at zero** *(`FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs`)*:

```csharp
public void AddListener()    { if (Interlocked.Increment(ref _listenerCount) == 1) _group.Enabled = true; }
public void RemoveListener() { if (Interlocked.Decrement(ref _listenerCount) == 0) { …; _group.Enabled = false; } }
```

⇒ ⛔ **`Enabled = true` fires ONLY on the exact 0→1 transition.** A `RemoveListener` before any
`AddListener` drives the count to **−1**, after which `AddListener` yields **0** — and the group is
**never enabled again for the life of the process.**

**Part 2 — the group's INITIAL state differs per host.** 📐 Measured:

| host | initial `gizmoGroup.Enabled` | boot perspective? | outcome |
|---|:--:|:--:|---|
| **IG** *(`IgApplication.cs:861`)* | ⭐ **`true`** | no | ✅ **immune** — already on, so the counter cannot matter |
| **Editor** | ⭐ **`true`** | n/a | ✅ immune |
| **CGF** *(`CgfSubsystem.cs:955`)* | ⚠ **`false`** | no | ✅ works — being *entered* first, its first event is `AddListener` **0→1** ⇒ enabled |
| 🔴 **SimHost** *(`SimHostApp.cs:444`)* | ⚠ **`false`** | 🔴 **YES** | 🔴 **DEAD** — as the boot perspective it is **LEFT before it is ever ENTERED**, so its first event is `RemoveListener` *(0→−1)*; every later `AddListener` yields 0 and never re-enables |
| **ReplayBrowser** | — *(no controller at all)* | no | ⛔ the gate cannot apply |

⭐⭐⭐ **THE POINT, and it is the whole argument for this design:** ⛔ **the defect is not
*"SimHost is misconfigured."*** It is that **one gate has three different initial states across five hosts
plus an unclamped counter that only misbehaves for whichever host happens to BOOT FIRST.**
🔒 **Change the default perspective from `SimHost` to `Scenario` and CGF would break instead** — same code,
different victim. ⇒ ⭐⭐ **no per-host inspection can find this**, which is why it survived a seven-hypothesis
elimination pass *(`CE-123`)* and was found only by a user looking at two screens.

📐 **Confirmed by prediction:** SimHost read **605 primitives / 3 non-`Line`** on visit 1, 2 **and** 3
across alternating switches, while `Scenario` read **739 / 69** on each of its visits — exactly what an
unclamped counter starting from a `RemoveListener` predicts.

#### ⇒ What the pack must therefore own

| # | |
|---|---|
| **①** | ⭐⭐ **construct** the buffer, `DataDrivenGizmoSystem`, `GlobalGizmoManager`, the togglable group and the controller — **one code path, one initial state** |
| **②** | 🔒 **ONE initial-state policy.** ⭐ Recommend **`Enabled = true`** *(IG's and Editor's choice — the two hosts that work)*, with the gate then only ever *narrowing*; ⛔ a host must not choose |
| **③** | 🔒 **clamp the counter** — `RemoveListener` below zero is a **bug to assert on**, not to absorb. ⚠ It is what made a host-ordering accident permanent |
| **④** | ⭐ **schedule it**, with the host supplying only *where* *(its group/kernel handle)* — ⛔ never *whether* |
| **⑤** | ⭐ **`ReplayBrowser` gets a controller too** — §3.1b makes it a member, so the gate must exist there |

⚠ **Scope honesty:** ① – ⑤ widen §3.2 from *registration* to *composition + execution*. ⭐ That is a bigger
pack than drafted and it is what the user's *"whatever it takes"* asks for — ⛔ but it also means the
`RW-M` estimate in `PLAN_Interaction_UX_Backlog` §4 is **light**; re-size when the UML is authored.

### 3.1b 🔒🔒🔒 MEMBERSHIP IS A RULE, NOT A HOST LIST — **every ECS-enabled host** *(user, `2026-08-28`)*

> ⭐⭐⭐ **User, verbatim:** *"both cgf, ig and simhost should show the map (and replaybrowser as well). And
> all the same way, using same gizmos etc, diffing just in the components currently present on the entity,
> no differences, as written in the docs/UX documents. Map needs to be unified, same like many other UI
> componenst we already unified."*
> ⭐⭐ **And, on being offered a SimHost-only fix:** *"chasing SimHost gap means deepening the separation of
> hosts, doesn't it? I can live with SimHost having no map if i knew we would work on unification that
> brings same map capability to all ECS-enabled hosts."*

⛔⛔ **THE SIMHOST-ONLY FIX IS REJECTED, and the reasoning is the user's:** patching SimHost's private map
wiring **adds a line to the very code this pack deletes**. ⇒ 🔒 **a per-host repair is not a step toward
unification, it is a step away from it** — and a mapless SimHost is the *acceptable* cost of not deepening
the divergence. ⚠ **Investigating the SimHost gap is still valuable** — but as **inventory input to this
design** *(what must the pack own so "does it actually run?" stops being per-host?)*, ⛔ **never as a patch.**

⭐⭐⭐ **The sharpening this adds to §3.2.** §3.2 says *"all hosts share the FULL set"* and §2/§3.3 then
enumerate **five named hosts** — ⛔ a list, which is what let the baseline rot and both hosts swap places
unnoticed *(§2b)*. 🔒 **Restate it as a RULE:**

> ⭐ **Every ECS-enabled host that presents a map calls `MapInteractionPack.Register` — membership is
> derived from "has an ECS world + presents a map", never from a maintained host list.**

| ⭐ why the rule beats the list | |
|---|---|
| ⭐⭐ **a new host cannot be forgotten** | there is no table to update, so there is no table to forget |
| ⭐ **`ReplayBrowser` stops being a special case** | it is ECS-enabled and presents a map ⇒ it is a member; its read-only-ness is a **predicate outcome** *(§3.2's applicability)*, not an exclusion |
| ⛔ **and it makes §2/§2c snapshots, not specifications** | ⚠ they document today's drift; **the rule is the contract** |

⚠ **What it does NOT license:** ⛔ it is not *"every host gets every behaviour"*. Per §3.2 + ruling 49,
**membership is uniform and visibility is earned** — an action a host structurally cannot service is not
shown. ⭐ The user's own words already say this: *"diffing just in the components currently present on the
entity"* ⇒ **the entity's components decide what draws, not the host's identity.**

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
| ⭐ **Applicability stays per-host** | via [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s descriptor predicates |

> 🔒 **RULED 2026-08-13 ([ruling 49](UX_RESUME_INTERACTION.md)):** *"permanently grayed item is useless."*
> ⇒ an action this host **structurally cannot service is not shown at all**. This design originally said
> *disabled with a reason, never silently missing* — **inverted**
> ([Correction 39](UX_Tasks_Detail.md#corrections)).
>
> ⭐ **The distinction that survives, and it is a good one:** grey means *"not now, try later"*. That is
> honest only for a **transient** blocker — the [API §5](UX_Interaction_API.md) exclusivity gate, or
> [UXI-08](UX_Feature_Layout_Defaults.md) case 5's *running outside the repo*. A blocker that can never
> clear in this host is **not a state, it is a fact about the host**, so the item does not belong in its
> menu at all.
>
> ⚠ **§3.2's uniform-membership rule still holds** — the **pack** registers the full set; the *menu* then
> shows what this host can actually do. Membership is uniform, **visibility is earned**. That keeps the
> "forgot to call registrar #3" failure impossible without producing dead rows.

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
| 23.5 | 🔒 An id a host **structurally cannot service is not shown at all** — never permanently greyed ([ruling 49](UX_RESUME_INTERACTION.md)). ⚠ **Inverted from this design's original wording** ([Correction 39](UX_Tasks_Detail.md#corrections)) | H |
| 23.6 | Unhandled ids still forward to ExCon from IG — the fallback survives the migration | H |
| 23.7 | `SelectionInteractionSystem` is constructed **with** a `RubberBandState` in every host | H |
| 23.8 | CGF registers `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar` — ring, health bars, menus, layer control all present | H |
| 23.9 | ReplayBrowser's dispatch is **wired to an ingress**, or its ids are deliberately unbound with a reason | H |
| 23.10 | **CGF**: right-click an entity → the shared menu appears with live items | I |
| 23.11 | **SimHost · IG**: rubber-band is **drawn** while dragging | I |
| 23.12 | *Measurement Tool* works in every host that shows it | I |

**9 H · 3 I · 0 V.**

## 5. ✅ CLOSED — the nine orphan ids are a **capability gap**, not a binding choice

> 🔒 **User, 2026-08-13:** *"the actions like MoveHere, Engage, Stop, Properties, Teleport, Repair,
> Reinforce, Resupply, Transfer are unresolved and need a dedicated design pass. **The only supported way
> of commanding entities now is via a mission having a list of conditional behaviors to perform.** This is
> not ExCon-only, must be equally supported by the CGF subsystem (who owns the entity brain)."*

🔴 **The A/B/C options below were ill-posed** and my lean B was wrong on both halves — see
[Correction 33](UX_Tasks_Detail.md#corrections). They are **not** ordinary actions with missing handlers:
commanding enters the cognitive tier through `AssignTacticalIntentEvent` and ends at
`BehaviorIngressSystem`, the sole `BehaviorState` writer. And ExCon is not their home — **CGF** is the
Brain node and the only host running the mission→behavior pipeline.

⇒ 🔒 **Moved to [UXI-32](UX_Issues.md#uxi-32) / [Q29](Architect_Question_29_Entity_Commanding.md)**,
`RW-H`, **architect pass required before any binding**.

**What this design still owes**, once Q29 is answered:

| | |
|---|---|
| ✅ **`Properties` and `Teleport` are unblocked now** | neither is a command — `Properties` is *open the inspector*, `Teleport` is a pose write already owned by [UXI-29](UX_Feature_Authority_Aware_Writes.md). Bind both in the pack |
| ⚠ **The other seven stay unbound** until Q29 rules | ⭐ **and four of them cost nothing today**: `Repair · Reinforce · Resupply · Transfer` sit behind ExCon menu strategies whose `SetStrategy` has **zero callers**, so they are **never emitted** ([Q29 §A](Architect_Question_29_Entity_Commanding.md)). Only `MoveHere · Engage · Stop` are actually on screen — emitted by `ContextMenuProjectorGizmo` |
| 🔒 **Case 23.2 still binds** | *no registered id is inert* — so the seven must be **absent or disabled-with-reason**, never present-and-dead |

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
