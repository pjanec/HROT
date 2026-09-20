<!--STATUS
state: LIVE
build-state: BUILDING (§2.7 is the consolidated TARGET STATE with class + sequence diagrams;
  §2.7.5 carries the slice order S-1..S-6. ☑ S-1 BUILT 2026-09-20 — as-built in §2.7.6.
  ☑ S-2 BUILT 2026-09-20 — as-built in §2.7.7.
  ☑ S-3 BUILT 2026-09-20 — as-built in §2.7.8. S-4..S-6 remain DESIGN.)
verified: 2026-09-10 (measured source scan, graph + grep, coverage checked)
  ⭐ S-1's own inventory re-measured 2026-09-20 on the graph — §2.7.6.
current-answer: ✅ READ §2.7 — the consolidated TARGET STATE (2026-09-10), with the class diagram, the
  request/notify sequence, the delete-list and the S-1..S-6 slice order, THEN §2.7.6 (S-1 as-built)
  and §2.7.7 (S-2 as-built) for what was actually built and where it deviated. §2.1 is its older
  sketch and §2.7 supersedes it where they differ. §2.6 carries the rulings §2.7 encodes.
  ☑ BUILT (S-1): ISelectionState carries Add/Remove/SetMultiple/Clear/Version; EcsSelectionState is
  the read-through view; the editor and CGF hold it instead of a parallel hash set.
  ☑ BUILT (S-2): SelectionChangeRequest (managed, set + mode) + SelectionRequestSystem (the renamed
  SelectEntitySystem), registered on IG too; all FOUR hand-rolled component writers are gone —
  "new SelectionState {" appears in zero production files outside EcsSelectionState.
  ⚠ NOT LITERALLY "the only writer": SelectionInteractionSystem still writes through the view, and
  two editor facade seams (SetSelection2D, Selected2DEntity) stay synchronous — §2.7.7 deviations
  ② and ③, both argued.
  ☑ BUILT (S-3): SelectionChangedNotification (FDP-internal, R-134-clean) published by the one
  writer for every cause; SelectionNotificationSystem points IInspectorContext at it; the entity
  inspector projects the host selection and publishes requests; ChainToMap RETIRED; IG's map->inspector
  hand-sync and its tracker deleted.
  ⚠ Panels PROJECT rather than subscribe (§2.7.8 deviation ②) — a one-frame bus event is unsafe for a
  panel that may not be drawn. The notification is for consumers needing an EDGE (S-5 is the next).
  ❌ STILL NOT BUILT: CGF has no map-input path; ClearAll has 0 callers; SimHostSelectionManager is
  still a separate store (lean recorded in §2.7.8); DerEntityInspectorPanel is S-4's seam.
  ⭐ 2026-09-10 — §2.6 is NEW and carries four user rulings: selection is GLOBAL across every host
  (ChainToMap as an opt-in is retired), a selection CHANGE cancels editing of the previously selected
  entity, clicks during an edit must not select (already true on the map via the capture-anchor filter,
  bypassed by panels), and §2.3 row 1 stands (an already-selected entity keeps the whole selection).
  ⛔ §1's headline is SUPERSEDED by its own re-measurement: FOUR stores and ~11 writers, not two/three.
  ⚠ CORRECTED same day: an earlier version of this block said §2.6's rulings all need §2.1 (one store)
    first. WRONG. Only ruling ① (selection is global) needs it — that ruling IS §2.1. Rulings ② (losing
    selection cancels that entity's edit) and §2.3 row 1 (an already-selected entity keeps the selection)
    are BUILDABLE NOW: ② is a per-entity predicate (ISelectionState.IsSelected + IToolController
    .ModalStack's per-entity Target), and row 1 is a conditional in SelectionInteractionSystem.
    Slice order and per-ruling status: §2.7.5 (this file). Tool_Model §4.14 keeps only the
    TOOL-side obligation and points here — one concept, one document.
  ⭐ "Mark Target for N Units..." is RETIRED; replacement in UX_Feature_Tool_Model.md §4.13.
stale-below: §1's title ("two stores and three writers") — read the corrected inventory at the top of §1.
known-conflict: none open. ✅ "Who owns selection" is RULED (2026-09-10): the global ECS repo on
  ECS-enabled nodes and NO ONE ELSE; one similar central piece on non-ECS nodes (ExCon). Selection is
  HOST-LOCAL. ⇒ ① means one store PER HOST with uniform behaviour, NOT a replicated selection, so ① is
  just §2.1 and is UNBLOCKED. This closes the former conflict between MapMessages.cs:103 ("The IG is
  authoritative for the selection state" — true only of the IG's own map, for remote observers) and §2.1.
  DDS selection traffic exists only for direct 2-D map control (ExCon ↔ IG); it is JUST ANOTHER REQUESTER
  and must translate into the same FDP selection-change request event a panel publishes.
  🔴 Two as-built consequences recorded in §2.6: the CMD_SET_SELECTION case sits inline in IgApplication
  rather than in MapCommandController (whose header already claims that job), and its echo-loop
  suppression works by NOT publishing the notification — which would leave every local panel stale under
  the request/notify protocol. Echo suppression belongs at the egress translator.
  ⚠ ONE OPEN OBSERVATION, deliberately not a verdict: ExCon's ingress hands SelectionChangedEventDto
  straight to ContextMenuLogic without it becoming an FDP event, and ExCon does have an FdpEventBus.
  Either an R-134 shape or a deliberate DDS-client architecture — §2.6's last subsection; not measured.
-->
# Feature design — selection

> **Design for [UXI-11](UX_Issues.md#uxi-11) · drafted 2026-08-12 · target state consolidated `2026-09-10` in §2.7.** **Status: 🟡 BUILDING — ☑ `S-1` (§2.7.6), ☑ `S-2` (§2.7.7) and ☑ `S-3` (§2.7.8) built `2026-09-20`: one store, one request, one writer, one announcement. ❌ `S-4`–`S-6` remain design — right-click does not select everywhere, the DER inspector has no seam, CGF still has no map-input path, `ClearAll` has 0 callers, and SimHost keeps a separate store.** Implements [rulings 27-28](UX_RESUME_INTERACTION.md). Feeds
> [UXI-24](UX_Issues.md#uxi-24) (multi-select) and [UXI-23](UX_Issues.md#uxi-23) (map parity).

## 0. Prior art ([rule 6](UX_Issues.md#rules))

| Exists? | What | Adoption | Bearing |
|:--:|---|---|---|
| ✅ | **`SelectionState`** ECS component — `IsSelected`, `IsPrimarySelection` | the model is **already correct for multi-select** | ⭐ nothing to redesign; one primary, many selected |
| ✅ | **`SelectionInteractionSystem`** — click + rubber-band → writes the component; `OnSelectionChanged` hook for network publishing | IG · ReplayBrowser · SimHost · Editor — ⚠ **not CGF** | **reused as the single input path** |
| ✅ | `SelectionHighlightGizmo` — green primary / yellow secondary ring | Editor · IG · ReplayBrowser — ⚠ **not SimHost, not CGF** | reused |
| 🔴 | **`ISelectionState`** — a **second, parallel** in-memory store (`DefaultSelectionState` HashSet; `SimHostInspectorAdapter`) | Editor · CGF · SimHost | 🔴 **the defect** — see §1 |
| 🔴 | `SelectionInteractionSystem.ClearAll()` — *"call before a world reset"* | **0 callers** | ⇒ **reload does not clear selection today** (ruling 28 ②) |
| ⚠ | `IEntityActionController` | duplicated in 2 projects | **single-entity only** (`long entityId` per method) ⇒ fan-out belongs on the descriptor layer ([UXI-03](UX_Feature_Entity_Action_Vocabulary.md)), not this facade |

## 1. The defect: two stores and **three** writers, synchronised by hand

⚠⚠ **RE-MEASURED `2026-09-10` — the headline of this section UNDERSTATES it. There are FOUR stores and
~11 production writers.** ⛔ The audit below is still correct about what it covers; it simply does not
cover the panel-private stores or the full `PrimarySelected` write set. ⭐ The corrected inventory:

| # | store | measured |
|--:|---|---|
| 1 | **`SelectionState` ECS component** | written by `SelectionInteractionSystem` |
| 2 | **`ISelectionState`** — **2** production implementations ⚠ *(corrected `2026-09-19`: this said **3**, counting the Examples adapter as production; measured on the graph via `IMPLEMENTS` edges)* | `DefaultSelectionState` · `SimHostInspectorAdapter` *(+`CarKinemInspectorAdapter` in Examples, +1 test fake)* |
| 3 | 🔴 **`EntityInspectorPanel._selectedEntities`** — its own multi-select `HashSet` | `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs:377` |
| 4 | 🔴 **`DerEntityInspectorPanel._selectedEntityId`** — its own single `int` | `FDP/Engine/Fdp.Presentation/ImGui/Panels/DerEntityInspectorPanel.cs:77` |

📐 **`PrimarySelected` alone has 9 production write sites:** `CgfSubsystem.cs:1461`, `:2884` ·
`EditorSubsystem.cs:754`, `:789`, `:1946`, `:2008`, `:2013` · `SelectEntitySystem.cs:83`.

⇒ ⚠⚠ **CORRECTED same day — an earlier version of this line said this is why ALL of §2.6's rulings
"cannot be built before §2.1". That is too coarse.** ⭐ Only ruling ① *(selection is global)* needs §2.1 —
that ruling **is** §2.1. Rulings ② and §2.3 row 1 are **per-entity / local** and buildable without it
*(§2.7.5 carries the slice order)*. ⭐ What the four stores DO block is the
**request/notify protocol** of §2.7, because *"the selection changed"* has four possible meanings until
S-1 lands. *(Coverage when measured: index mode `full`, recording complete; no parse gaps in any file
named here.)*

⚠ **Corrected 2026-08-12** ([Correction 28](UX_Tasks_Detail.md#corrections)) — an earlier draft of this
section claimed the two stores are *desynchronised in the Editor*. **They are not.** The audit below is
what the code actually does.

| # | Writer | Writes | |
|--:|---|---|---|
| 1 | `SelectionInteractionSystem:170-172` — click, rubber-band | **component** | |
| 2 | `EditorSubsystem.cs:1226-1230` — the *Select* action handler | **component *and* object** | ⚠ **re-implements `SetSelected` by hand**, including the clear-previous loop |
| 3 | `EditorSubsystem.cs:1292-1297` — `OnSelectionChanged` callback | **object** | deliberate propagation *from* writer 1 |

⇒ 🔒 **The Editor is consistent — by manual effort.** The cost is not a live bug but **duplicated
selection logic in two places and a sync that must be remembered at every new call site.**

| Host | Reality |
|---|---|
| **Editor** | both stores, hand-synced by writers 2 and 3 |
| **CGF** | 🔴 **object only** — no `SelectionState` component exists at all, so nothing to desync and **no ring possible** |
| **SimHost** | component written; `ISelectionState` via `SimHostInspectorAdapter`; ⚠ no ring registered |

⭐ **This strengthens the case for one store rather than weakening it**: writer 2 exists *only* because
there are two stores. A view deletes it.

## 2. The design

### 2.1 One store; `ISelectionState` becomes a *view*

🔒 **The ECS component is the single source of truth.** `ISelectionState` keeps its shape — every shared
panel keeps compiling — but its implementation becomes a **read-through adapter over the component**:

```csharp
sealed class EcsSelectionState : ISelectionState      // replaces DefaultSelectionState in map hosts
{
    public bool IsSelected(Entity e);                  // → component
    public IReadOnlyCollection<Entity> SelectedEntities { get; }
    public Entity? PrimarySelected { get; set; }       // setter routes through the interaction system
}
```

| | |
|---|---|
| ✅ **The desync cannot recur** | there is only one place to write |
| ⚠ **Menu handlers need no change** — 🔴 **amended 2026-08-13 by [UXI-24 §1.4](UX_Feature_Multi_Select.md)** | true for *single* selection only. `PrimarySelected` is the interface's **only** mutator and its setter **collapses the selection to one** (`DefaultSelectionState.cs:24-34`); `AddSelection`/`ClearSelection` are off the interface with **zero callers**. ⇒ `ISelectionState` must gain `Add`/`Remove`/`SetMultiple`/`Clear` |
| ⭐ **ExCon fits without an exception** | 🔒 [ruling 27](UX_RESUME_INTERACTION.md) — **same interface, DDS-backed implementation**, no ECS. The interface is the seam; the store is the binding |

### 2.2 One pipeline, identical in every map subsystem

```
pick box  (per entity, emitted by the presentation gizmo)
   ↓  GizmoInteractionStartedEvent
SelectionInteractionSystem        ← click · right-click · rubber-band
   ↓  writes
SelectionState component          ← single source of truth
   ↓  read by
ring gizmo   ·   ISelectionState view   ·   actions
```

🔒 **Selection is subsystem-local** — each subsystem owns its world, so this needs no machinery and
carries nothing across a perspective switch.

| | Pick box | Interaction system | Ring gizmo | Today |
|---|:--:|:--:|:--:|---|
| Editor · IG · ReplayBrowser | ✅ | ✅ | ✅ | works |
| **SimHost** | ✅ | ✅ | ❌ | selects, **invisibly** |
| **CGF** | ❌ | ❌ | ❌ | **nothing** — no click target at all |
| ExCon | — | — | — | 🔒 correct: no map, remote selection |

⭐ CGF's first link — the pick box — is already in the [UXI-10 design](UX_Feature_Entity_Symbology.md).
SimHost's gap is one registrar call.

### 2.3 🔒 Click semantics — RULED (user, 2026-08-12)

| Right-click on… | Selection afterwards | Menu shows |
|---|---|---|
| an entity **already selected** | 🔒 **unchanged** — the whole selection survives | items applicable to **all** selected |
| an entity **not selected** | 🔒 **cleared, then that entity selected** (primary) | items for that one entity |
| **empty space** | 🔒 **cleared** | the canvas menu |

⚠ **Ordering constraint:** the selection mutation must happen **before** the menu is populated, in the
same frame — ImGui builds a context menu on the frame the click arrives, so a menu built from the old
selection would be wrong exactly once, which is the hardest kind of bug to see.

**Clearing** — the complete list: empty-space click · **scenario reload / world reset** (⇒ call the
existing `ClearAll()`, which today has **no callers**) · entity despawn (automatic — the component dies
with the entity, but ⚠ **the view must not cache a stale primary**).

### 2.4 Multi-select: AND for visibility, fan-out for execution

The component already models it; what is missing is input and menu policy.

| | |
|---|---|
| ⚠ **Ctrl/Shift additive click does not exist — 🔴 [corrected 2026-08-13](UX_Tasks_Detail.md#corrections)** | true **on the map** — click is unconditionally `SetSelected(entity, isPrimary: true)` (`:83`), and rubber-band is the only route there. **False as written**: the *inspector list* has ctrl-toggle **and** shift-range (`EntityInspectorPanel.cs:410-437`). [UXI-24](UX_Feature_Multi_Select.md) owns the reconciliation |
| 🔒 **Menu = items applicable to *all* selected** | the AND/intersection rule, per the ruling |
| **Execution fans out** over the selection — [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s `EntityActionDescriptor` is the layer that carries it | ⚠ **not** `IEntityActionController`, which is single-entity by signature |

| Applicable to… | Treatment |
|---|---|
| **all** selected | shown, enabled |
| **some** selected | 🔒 **absent** |
| **none** selected | 🔒 **absent** |

> ✅ **RULED 2026-08-13 ([ruling 47](UX_RESUME_INTERACTION.md))** — *"incompatible ones should simply not
> be present… we do not need a huge menu full of grayed items, each with different reasoning for a
> different incompatible entity."*
>
> ⚠ **An earlier draft of this section said the middle row was *"shown, disabled, with a reason —
> 3 of 12 selected support this"***, borrowing [UXI-08](UX_Feature_Layout_Defaults.md) case 5's
> *disabled-with-a-reason* principle. That **relaxed a P0 requirement** ([UXR-91](UX_Requirements.md#uxr-91))
> without flagging it, and it is now **refuted** — [Correction 38](UX_Tasks_Detail.md#corrections).
>
> ⭐ **Why hiding is right, not merely simpler:** a reason does not aggregate. Twelve selected entities can
> be incompatible for twelve different reasons, so there is no single sentence to display — which is
> exactly why the greyed-item design produced no usable text.

### 2.4b 🔒 One pick box, one ring — shared everywhere

> **User, 2026-08-12:** *"CGF pick box and ring appearance should be the same in all map subsystems,
> shared and reused."*

⭐ **They already are single implementations — the gap is adoption, not variants.** Verified:

| | What | Where | Adopted by |
|---|---|---|---|
| **Pick box** | `EntityPresentationGizmoShared.EmitPickBox` — 8 px box, fully transparent, `PipelineTarget.Map2D` | one shared static (`:21-32`) | IG · SimHost — ⚠ **not CGF** |
| **Ring** | `SelectionHighlightGizmo` — 20 px radius, 2 px thickness, **green** primary / **yellow** secondary, `SizeMode.ScreenPixels` | one shared class (`:26-51`) | Editor · IG · ReplayBrowser — ⚠ **not SimHost, not CGF** |

🔒 **No host may have its own.** Neither type takes a per-host parameter today, and none is to be added —
the pick box comes from the merged presentation gizmo ([UXI-10](UX_Feature_Entity_Symbology.md) §3.3), so
adopting that gizmo *is* adopting the pick box. ⇒ **CGF and SimHost need registration, not code.**

#### ⚠ And a mismatch worth fixing once, centrally

| | Size |
|---|---|
| pick box | **8 px** |
| selection ring | **20 px** radius |

⇒ **The visible affordance is larger than the clickable one** — the ring you see is more than twice the
target you can hit. ⚠ Both are screen-space, so this is zoom-independent and identical in every host.
🔒 **Fix it in the shared constants**, so all five agree by construction; do not tune per host.

### 2.5 🔒 Structural changes go through the command buffer

> **User, 2026-08-12:** *"So why not modify ECS using command buffers, which is safe always?"*

🔒 **Adopted — and the objection I raised against it does not survive checking**
([Correction 29](UX_Tasks_Detail.md#corrections)).

**What I claimed:** that a view-backed `PrimarySelected` risks corrupting chunk arrays, because
`SetSelected` adds the component when absent (`:170-171`) and the guide says *"Never add/remove ECS
components inside … chunk iteration"*.

**What the code says:**

| | |
|---|---|
| The guide's rule is **scoped** | it reads *"Never add/remove ECS components inside **HSM/BTree `SharedAi` thunks** — they write directly during chunk iteration"* (`HROT-PROGRAMMERS-GUIDE.md:483-484`). It is about a **different iteration mechanism** |
| `EntityQuery` does **not** walk chunks | `EntityEnumerator` is an **index scan** — `_currentIndex` from `-1` to a `_maxIndex` **snapshotted at construction** (`EntityQuery.cs:106-123`), testing masks per entity |
| ⇒ adding a component mid-iteration **does not invalidate it** | and `SelectionState` is not in any live filter, so it cannot change which entities match |
| ⇒ the **existing** rubber-band code is fine | `SetSelected` inside `foreach (var e in q)` (`:205-217`) is safe, not a latent defect |

⇒ **The "pre-add the component" option loses its justification, and the command buffer wins on its own
merits:**

| | |
|---|---|
| ✅ **The documented path** for structural changes (`HROT-PROGRAMMERS-GUIDE.md:64`) — no new convention |
| ✅ **Uniform** — no call site needs to know whether it is inside an iteration; the question stops existing |
| ✅ **Already the rule for action handlers** — [ruling 15](UX_RESUME_INTERACTION.md) gives every action a per-action ECB, and a menu item that changes selection **is** an action handler. Selection stops being a special case |
| ⚠ **Cost: one frame** before the ring moves — *"one-frame latencies are structural, not bugs"* (guide `:114`), and 16 ms is below notice |

🔒 **The one exception stays as-is:** `SelectionInteractionSystem` writes the repository directly during
its own main-thread `Tick`. That is correct today, immediate, and has no reason to change — the ECB rule
applies to **callers outside the tick**, which is where the view's setter lives.

> ⚠⚠ **AS-BUILT `2026-09-20` — `S-1` DID NOT DO THIS, deliberately.** `EcsSelectionState`'s mutators
> write the repository **directly**. 📐 The paragraph above already establishes that the corruption
> risk is not real *(`EntityQuery` is an index scan)*, so the ECB was winning on uniformity alone — and
> ⛔ **the one-frame deferral is not free here**: `EditorStrideSubsystem.SyncSelection2D3D` reads
> `Selection2DVersion` back **in the same frame** it writes the selection, which the risk table below
> already flags. ⭐⭐ **`S-2` retires the question rather than answering it** — once every caller
> publishes a `SelectionChangeRequest` and `SelectionRequestSystem` applies it during its own tick,
> the only writer IS the blessed exception and no caller sits outside a tick. ⇒ an ECB path built at
> `S-1` is a path `S-2` deletes. 📄 §2.7.6 deviation ③.

### 2.6 🔒🔒🔒 SELECTION IS GLOBAL, AND ONLY THE SELECTED ENTITY IS EDITABLE *(user rulings, `2026-09-10`)*

| # | 🔒 the ruling | consequence |
|---|---|---|
| **①** | *"inspector selection changes global entity selection state. not just map, not just editor, everywhere, every host, unified behavior."* | ⛔ **`EntityInspectorPanel.ChainToMap` as an opt-in is RETIRED.** 📐 It defaults to `false` (`:148`); the only production host that sets it true is **ReplayBrowser** (`:676-677`), and there is an operator toggle at `:663-667` ⇒ **in the editor, inspector selection does not reach the map today.** ⭐ Under §2.1 the question disappears: there is one store, so a panel writing selection IS the global selection |
| **②** | *"Changing selection to another entity should cancel any currently active editing of the previously selected entity as we want just selected entity be editable."* ⭐ **restated by the user the same day, and this form is the buildable one:** *"if entity becomes unselected, it should cancel any editing on the entity losing the selection"* | ⭐⭐ **a PER-ENTITY predicate, not a global change event** — *"is THIS entity still selected?"* asked of the store the ARMING used. ⛔ **It does NOT require §2.1.** 📄 the measurements and the shape are in [`UX_Feature_Tool_Model.md` §4.14](UX_Feature_Tool_Model.md) |
| **③** | clicks while an edit is in progress must not select another entity — *"something like mouse capture"*, defined by the gizmo | ✅ **already true, and it is a MAP-INPUT rule only** (`GizmoMap…/DebugGizmoLayer.cs:213` + `:491` — the capture-anchor filter). ⛔ It does **not** extend to panels |
| **④** | 🔒 *"panels should not need to read tool state. panel can force selection change. they should stay unaware of any map or map tools whatsoever."* | ⭐⭐ **panels WRITE selection and know nothing else.** ⇒ ② is the whole mechanism for a panel-driven change, with no panel involvement. ⛔ An earlier version of this table said panels must consult tool state — **wrong, it would couple every shared panel to the map.** 📐 Measured: the only map/tool reference in all of `FDP/…/ImGui/` is `ChainToMap` *(5 sites in `EntityInspectorPanel`)*, which ruling ① already retires ⇒ **①  and ④ converge on one deletion.** ⭐ **This row is the SPECIFICATION** — `UX_Feature_Tool_Model.md` §4.14 keeps only the TOOL-side half and points here. |

⭐⭐ **② and ③ do not conflict:** while a tool is armed the map cannot change the selection at all, so ②
never fires from a map click. ② governs the surfaces that are not captured — and it does so **without those
surfaces knowing anything about tools.**

#### ⚠ AS-BUILT DIVERGENCE from §2.3, measured `2026-09-10`

| §2.3 row | as-built |
|---|---|
| right-click an **unselected** entity ⇒ cleared, then selected | ✅ on the **map** — right-release emits a `Started` event (`GizmoMap…/DebugGizmoLayer.cs:227`) which `SelectionInteractionSystem` turns into clear+select. 🔴 **NOT in the inspector** — `EntityInspectorPanel.cs:398-403` only opens the popup; left-click selects (`:385-395`), right-click does not |
| right-click an **already-selected** entity ⇒ **selection unchanged** | 🔴 **VIOLATED** — the clear+select is unconditional, so right-clicking one of five selected **collapses the selection to one** |
| mutate selection **before** the menu is populated | ⚠ **not asserted anywhere** — no rail found |

🔒 **The second row is load-bearing, not cosmetic:** the `2026-09-10` fan-out ruling (*"context menu opened
on selection … should affect all selected entities as long as they support that"*) is impossible if the
gesture that opens the menu destroys the multi-selection. ⇒ **§2.3 row 1 stands and the unconditional clear
is a defect.**

#### 📐📐 EVERY SELECTION-FORCING SURFACE, measured `2026-09-10` — **the inventory ruling ① needs**

🔒 **User, `2026-09-10`:** *"der entity inspector as well should force entity selection change (as it plays
similar role as the ecs entity inspector)"* · *"[the orbat rule] should apply to orbat panel for editor and
everywhere else the orbat panel is (cgf, stride editor, everywhere brain role is)"*.

| surface | forces selection today? |
|---|---|
| ⭐⭐ **orbat — the SHARED seam** `IOrbatController.SelectEntity(int networkId)` | ✅ **the seam already exists**, 2 production impls |
| ↳ `ScenarioOrbatAdapter.cs:156-160` *(ECS hosts)* | ✅ publishes `ActivateEditorToolEvent(Select)` **then** `SelectEntityCommand`. ⚠ It was `ActivateTool(Select)` ALONE — *"it activated the SELECT TOOL and **ignored `entityId` entirely**, so clicking an ORBAT row selected nothing"* (`CE-060`, fixed `2026-08-27`, recorded at `:137-155`) |
| ↳ `ExConOrbatAdapter.cs:124` *(ExCon, DDS)* | 🔴 `_logic.SelectEntity(id)` ⇒ `ExConLogic.cs:359-363` sets `SelectedEntityId` **LOCALLY ONLY**. ⛔ `SendSetSelection` (`:366-377`) is the one that also writes `CMD_SET_SELECTION` over DDS — see `CE-259t` |
| **ECS entity inspector** `EntityInspectorPanel` | ⚠ **left-click yes** (`:385-395`, via `IInspectorContext.SelectedEntity` + `OnEntitySelected`); 🔴 **right-click NO** (`:398-403`) |
| 🔴 **DER entity inspector** `DerEntityInspectorPanel` | ⛔ **no selection seam AT ALL** — only a private `_selectedEntityId` (`:77`) and the context-menu handler list. ⚠ Unlike its ECS twin it has **no `IInspectorContext`, no `OnEntitySelected`, no callback** ⇒ ruling ① needs a seam ADDED here, not just a right-click arm |
| **map** | ✅ left-press and right-release both emit `Started` → `SelectionInteractionSystem` |

##### 📐 Where the orbat panel actually is — **and where it is NOT**

`SharedOrbatPanel` is constructed in **CGF** (`CgfSubsystem.cs:1408`, registered `:1605`), the **Editor**
(`EditorSubsystem.cs:2461`, registered `:4890`) and **`ExConMock.cs:111`**.
⛔⛔ **No Stride host registers it** — 📐 `grep` over `Stride/` finds none. ⇒ ⚠ *"everywhere the orbat panel
is … stride editor"* is **not** satisfied today; the Stride editor has no orbat panel to apply the rule to.
⭐ That is a PORT, not a fix, and it is out of this document's scope — recorded so nobody assumes coverage.

⚠⚠ **And ExCon's PRODUCTION orbat is its own private panel, not the shared one:** `ExConSubsystem.cs:498`
constructs `Hrot.ExCon/Panels/OrbatPanel.cs` and `:570` registers `ExConOrbatWindow`; that panel calls
`logic.SendSetSelection(...)` (`OrbatPanel.cs:312`) — the **correct** local+DDS path. `ExConWindows.cs:57`
acknowledges the split in its own words: *"Not the same panel as `SharedOrbatPanel` (group 5)"*.
⇒ 🔴 **the shared path and the private path have DIFFERENT selection semantics, and the SHARED one is the
weaker** — `CE-259t`.

##### ⭐⭐ THE PRECEDENT FOR RULING ② ALREADY EXISTS — **and it argues for doing it CENTRALLY**

📐 `ScenarioOrbatAdapter.SelectEntity` arms the **null `Select` tool** before changing the selection, and
`Select` is `ToolArbiter.None`, so `Activate` runs `CancelActiveModalWithoutNotify()` **and**
`CancelOtherArbiter(None)` — which clears BOTH arbiters. ⇒ ⭐ **selecting from the orbat already cancels any
armed editing.** ⚠ But as a **per-call-site side effect**, not a rule, and **only the orbat does it**.

⇒ ⭐⭐⭐ **Ruling ② should be the central per-entity predicate, NOT this idiom copied to every surface.**
🔒 Copying it would (a) make every selection-forcing surface arm a *tool* — which ruling ④ forbids, since a
panel may not know tools exist — and (b) be the duplication this programme keeps finding. ⭐ The orbat's own
line then becomes redundant and can drop the `ActivateEditorToolEvent`.
⚠ `CE-259s`'s stale-target ordering does **not** bite here: `Select` ignores its target.

#### 🔒🔒🔒 THE PANEL↔SELECTION PROTOCOL — **request / notify, and no tool knowledge** *(user ruling, `2026-09-10`)*

🔒 **User, verbatim:** *"panels should be sending selection change request fdp events and listen and react
to selection changed (by changing their own selection indicators). no tool knowledge should be needed. only
selection concept knowledge, including multiselect."*

| ⭐ the protocol | |
|---|---|
| a panel **PUBLISHES A REQUEST** — *"select these"* | ⛔ it does not write a store, and it does not hold the truth |
| a panel **LISTENS FOR "selection changed"** and repaints its own indicator | ⇒ **panel-private selection becomes VIEW STATE**, derived from the notification — ⛔ never a source |
| a panel knows **the selection concept, including MULTI-select** | ⛔ and nothing else — no tools, no map, no arbiters *(ruling ④)* |

##### 📐 What exists, measured `2026-09-10` — **the request half exists; the notify half does NOT**

| half | state |
|---|---|
| ⭐ **request** | ✅ `SelectEntityCommand` is already an FDP-internal bus event *(`Hrot/Engine/Hrot.Core/Events/Common/SelectEntityCommand.cs`)* consumed by `SelectEntitySystem`. 🔴 **but SINGLE-entity — `long NetworkId`, nothing else** ⇒ it cannot express *"select these three"* |
| 🔴 **notify** | ⛔⛔ **THERE IS NO FDP-INTERNAL SELECTION-CHANGED EVENT.** 📐 Both existing types are NETWORK: `Hrot.Network.NED/MapMessages.cs:106` is `[DdsTopic("SelectionChangedEvent")]` `[DdsIdlFile("hrot-map-msgs")]`, and `Hrot.Core/Network/Commands.cs:117` `SelectionChangedEventDto` is the adapter-boundary DTO *(egress `NedIgNetworkAdapter.WriteSelectionChanged:92`; ingress `NedExConIngressTranslators:67-77` → ExCon's queue)* |
| the existing "notification" | ⚠ `SelectionInteractionSystem.OnSelectionChanged` is an `Action<Entity, Vector3>` **CALLBACK**, not a bus event — ⭐ and single-entity. It is the seam that becomes the publisher |
| multi-select mutators | 🔴 `ISelectionState` has only `IsSelected` · `SelectedEntities` · `PrimarySelected{get;set}` · `HoveredEntity{get;set}` ⇒ **no `Add`/`Remove`/`SetMultiple`/`Clear`** — exactly as §2.1's own amendment warned |

⛔⛔ **AND `R-134` DECIDES WHERE THE NOTIFY EVENT MAY COME FROM:** *network is strictly separated from
internal FDP event processing; no DDS type crosses into the FDP-internal path; the egress translator is the
sole boundary.* ⇒ ⭐⭐ **panels may NOT listen to `SelectionChangedEvent` or `SelectionChangedEventDto`.** The
FDP-internal notification is a **new, internal** record with its own types, and the translator converts —
🔒 *"even at the cost of keeping the same enum duplicated in two namespaces"* is the pattern `R-134` already
blesses for exactly this shape.

⭐⭐ **A pleasing asymmetry worth keeping in mind:** the **WIRE** model is already multi-select — the DDS
event carries `List<int> SelectedEntityIds`, *"the complete list of currently selected Entity IDs. Replaces
any previous selection state"* — while the **internal** request is single-entity. ⇒ **the internal model is
the one that is behind**, not the network.

##### ✅✅ RESOLVED — **SELECTION IS HOST-LOCAL** *(user ruling, `2026-09-10`)*

🔒 **User, verbatim:** *"selection is host local, no dds entity selection stuff needs to exist (unless this
is a part of direct 2d map control which is a different story and even this one needs to be translated to
fdp events)"*.

⇒ ⭐⭐⭐ **THE THREE-OWNER CONFLICT DISSOLVES rather than being settled**, and it re-reads ruling ①:

| ⭐ what ① means | |
|---|---|
| *"global … every host, unified behavior"* = **one store PER HOST, and every host behaves the same** | ⛔ **NOT** one selection replicated across the cluster |
| ⇒ ① is exactly **`UXI-11` §2.1** *(the ECS component is the source of truth; `ISelectionState` is a view)* | ⭐ with **no cluster-authority question attached** |
| ⇒ ⭐⭐ **① IS UNBLOCKED** | the `known-conflict` in this file's STATUS block is closed by this ruling |

⛔ **And it retires the general reading of `MapMessages.cs:103`** — *"The IG is authoritative for the
selection state"*. ⭐ That is true only of **the IG's own map**, for the benefit of remote observers; it is
**not** a statement about who owns selection in the system.

##### ⭐ THE CARVE-OUT: direct 2-D map control — **it stays, and it must translate**

🔒 *"unless this is a part of direct 2d map control which is a different story and even this one needs to
be translated to fdp events"*.

📐 **Measured — that is precisely what the existing DDS selection traffic is:**

| direction | site |
|---|---|
| **IG → observers**: the IG publishes when **its own map** selection changes | `IgApplication.cs:2160-2172` — `WriteSelectionChanged(new SelectionChangedEventDto { MapId, SelectedEntityIds })` |
| **ExCon → IG**: *"select this on your map"* | `ExConLogic.SendSetSelection:366-377` → `CMD_SET_SELECTION` |

⇒ ⭐ **Remote map control, not a general selection mechanism.** It stays; ⛔ it does **not** become the
selection concept, and no host needs DDS to know what it has selected.

##### 🔒🔒🔒 WHO OWNS SELECTION — **RULED `2026-09-10`**

🔒 **User, verbatim:** *"the global ecs repo on ecs enabled nodes, no one else. Some similar central piece
on non ecs nodes like excon. map control message for selection change need to be handled by ig map
controller handling all similar map control messages, changing the selection via the some global
selectionstate owner (i.e likely by issuing fdp selection change request event as everyone else does)"*.

| ⭐ the owner | |
|---|---|
| **ECS-enabled nodes** | 🔒 **the global ECS repo. NO ONE ELSE.** ⇒ exactly §2.1: the `SelectionState` component is the truth, `ISelectionState` is a view |
| **non-ECS nodes** *(ExCon)* | 🔒 **one similar central piece** — ⛔ not per-panel state |
| ⭐⭐⭐ **remote map control is JUST ANOTHER REQUESTER** | the DDS command is translated and then *"issu[es an] fdp selection change request event as everyone else does"*. ⇒ **no privileged path** |

##### 📐 The IG map-control path, measured — **the piece exists and the selection case is in the wrong place**

⭐⭐ **`MapCommandController` IS the controller this ruling names.** Its own header: *"Orchestrates IG-side
tool activation from `MapCommandRequest` messages and bridges the results back to the ExCon via
`MapCommandAck`. **This control layer decouples IG map tools from any specific network protocol.**"*

🔴 **But the map-command switch lives INLINE IN `IgApplication`** *(`:1120-1160`)*, not in it — cases
`CMD_START_EDITING` · `CMD_PICK_LOCATION` · `CMD_PICK_ENTITY` · **`CMD_SET_SELECTION`** · `CMD_SET_VIEW` ·
`CMD_DRAW_PERSONAL_ROUTE`.

⚠⚠ **CORRECTED `2026-09-10`, same day — an earlier version of this subsection called the ruling "a
RELOCATION into a class whose stated purpose already covers it". That was imprecise.** 📐 Measured: the
class's **HEADER** claims the dispatch role; its **ACTUAL SURFACE is session management**, and the
dispatcher role does not exist as a class anywhere today.

| 📐 `MapCommandController`'s real responsibility | |
|---|---|
| `ActivatePlacementCommand` `:185` · `BeginAreaAuthoringSession` `:276` · `OnAreaEntityCreated` `:294` · `OnAreaToolCancelled` `:318` · `OnCreateEntityAck` `:340` | ⭐ **remote-driven entity-creation SESSIONS** — placement and area authoring — with request↔ack correlation and three ExCon status codes |
| single-session state | `_sessionRequestId` · `_sessionContextId` · `_toolFinished` · `_activePlacementId` ⇒ **one active session at a time** |
| ⛔ it handles **none** of the selection/view/pick/edit commands | those are the inline switch |
| **IG-specific?** | ⛔ **only by namespace and wiring.** Dependencies are host-neutral — `MapCanvas`, `FdpEventBus`, `Action<MapCommandAckDto>`, `long localNodeId`, `GlobalGizmoManager?` — and it is constructed at exactly one site *(`IgApplication.cs:863`)* |
| **DDS-specific?** | ⛔ **NO, and it is checkable:** every `Hrot.NED.Messages.*` reference is in a DOC COMMENT *(`:20`, `:66`, `:84`, `:168`)* and **none in code**. ⇒ `R-134`-clean, exactly as its header claims |

⇒ ⭐ **LEAN — widen `MapCommandController` into the dispatcher the ruling names** *(its name and header
already promise it, and its host-neutral dependencies mean it could later be SHARED rather than IG-only,
which matters because `CMD_PICK_*` and `CMD_START_EDITING` have direct analogues in the editor's own tool
model)*.
⛔⛔ **WITH ONE CONSTRAINT:** the **session state must stay separate from dispatch.** Selection and view
commands are **stateless one-shots**; placement and area authoring are **stateful single-session**. ⇒ folding
stateless cases into a class that guards `_sessionContextId` is how a selection command ends up silently
refused because a placement session happens to be open.

##### 🔴🔴 AND THE ECHO-LOOP SUPPRESSION IS IMPLEMENTED IN THE WRONG PLACE

📐 `ParseCommandAndSetSelection` *(`IgApplication.cs:2949-2976`)* resolves the id and calls
`SelectEntityOnMap`. 🔒 **Its own doc comment:** *"Selects the entity identified by `entityId` in the ECS
**without publishing a `SelectionChangedEvent`** (to avoid ExCon→IG→ExCon echo loops)."*

⇒ ⛔⛔ **echo avoidance is achieved by NOT NOTIFYING** — which under the request/notify protocol above would
leave **every local panel stale** after a remote selection change, because the notification they listen for
is precisely the thing being suppressed. ⭐⭐ **Echo suppression belongs at the EGRESS translator** *(do not
re-publish outward what ingress produced)*, ⛔ never by muting the internal notification.

📐 **And `SelectEntityOnMap` (`:1596-1614`) is a THIRD hand-rolled `SetSelected`:** it clears
`SelectionState` on every entity in a loop, sets it on the target, **and** hand-syncs
`_fdpInspectorState.SelectedEntity` + `_fdpLastMapSelection`. ⇒ 🔒 exactly the *"duplicated selection logic
and a sync that must be remembered at every new call site"* §1 describes — alongside
`SelectionInteractionSystem`'s and `EditorSubsystem`'s. ⭐ Under this ruling all three collapse into
*request → owner applies → notification*, and those two fields become notification CONSUMERS.

##### ⚠ OPEN, and recorded as an OBSERVATION rather than a verdict

📐 `ExCon` **does have `FdpEventBus`** *(`ExConSubsystem.cs:152`, `:280`; observer bus `:172`, `:296`)*, so
*"translated to fdp events"* is applicable there. 📐 Today the ingress does **not** do that: DDS
`SelectionChangedEvent` → `SelectionChangedEventDto` *(`NedExConIngressTranslators:67-77`)* →
`ConcurrentEventQueue<SelectionChangedEventDto>` → `ExConLogic._selectionQueue` →
`ContextMenuLogic.OnSelectionChanged(SelectionChangedEventDto evt, …)` — ⇒ **the network DTO reaches ExCon
application logic without becoming an FDP-internal event.**

⚠⚠ **TWO READINGS, and I am not choosing between them here:** either this is the `R-134` shape *(a network
type inside the internal path — the same class as the `AttributeRecord` finding that row already records)*,
**or** ExCon's queue-based ingress is a deliberate architecture for a DDS client and the FDP bus is used for
other concerns. ⛔ **Not measured:** what ExCon's two buses actually carry. ⇒ settle that before treating it
as a defect.

#### 📄 What this retires elsewhere

⛔ **`"Mark Target for N Units…"` is retired** and replaced by *"Pick target…"* on the selected perceiver's
menu — 🔒 user ruling `2026-09-10`, recorded with its measurements in
[`UX_Feature_Tool_Model.md` §4.13](UX_Feature_Tool_Model.md). ⭐ It was already incoherent: it **gates** on
the right-clicked entity holding `TargetMemory` and then **acts** on the selected entities instead.

### 2.7 ⭐⭐⭐ TARGET STATE — **consolidated `2026-09-10`** *(supersedes §2.1's sketch where they differ)*

⭐ §2.1 got the *store* right and predates every ruling of `2026-09-10`. This section is the **whole target**:
one owner, two events, four stores collapsing to one, and every surface a requester.

#### 2.7.1 The classes

```mermaid
classDiagram
    class SelectionState {
        <<ECS component - THE TRUTH>>
        +bool IsSelected
        +bool IsPrimarySelection
    }
    class ISelectionState {
        <<interface - becomes a VIEW>>
        +IsSelected(e) bool
        +SelectedEntities
        +PrimarySelected
        +Add(e)
        +Remove(e)
        +SetMultiple(set)
        +Clear()
    }
    class EcsSelectionState {
        <<read-through over the component>>
    }
    class DdsBackedSelectionState {
        <<non-ECS hosts - ExCon>>
    }
    class SelectionRequestSystem {
        <<THE ONLY WRITER>>
        +Execute(view, dt)
    }
    class SelectionChangeRequest {
        <<FDP event - EXISTS but single-entity>>
        +Entities
        +Mode
    }
    class SelectionChangedNotification {
        <<FDP event - DOES NOT EXIST YET>>
        +Selected
        +Primary
    }
    class ISelectionRequester {
        <<every surface - panels, map, orbat, dispatcher>>
    }

    ISelectionState <|.. EcsSelectionState
    ISelectionState <|.. DdsBackedSelectionState
    EcsSelectionState ..> SelectionState : reads
    ISelectionRequester ..> SelectionChangeRequest : publishes
    SelectionRequestSystem ..> SelectionChangeRequest : consumes
    SelectionRequestSystem ..> SelectionState : the ONLY writer
    SelectionRequestSystem ..> SelectionChangedNotification : publishes
    ISelectionRequester ..> SelectionChangedNotification : reacts - repaints
```

| element | state today |
|---|---|
| `SelectionState` component | ✅ exists, already models multi-select |
| `ISelectionState` | ☑ **`S-1`, `2026-09-20`** — carries `Add`/`Remove`/`SetMultiple`/`Clear`, plus `Version` *(§2.7.6 deviation ①)*. ⛔ *Was: only `IsSelected`, `SelectedEntities`, `PrimarySelected`, `HoveredEntity`.* |
| `EcsSelectionState` | ☑ **`S-1`, `2026-09-20`** — `Hrot.Presentation`, a read-through over the component; the editor and CGF hold it. ⛔ *Was: did not exist; `DefaultSelectionState` was a parallel `HashSet` store on both hosts.* |
| `DdsBackedSelectionState` | 🔴 does not exist; 🔒 ruling: *"some similar central piece on non ecs nodes"* |
| `SelectionRequestSystem` | ☑ **`S-2`, `2026-09-20`** — `SelectEntitySystem` **renamed** *(Roslyn)* and extended; consumes both request forms and is the only thing that serves them. ⚠ One other writer survives *(§2.7.7 deviation ②)*. |
| 🔴 **notification** event | ☑ **`S-3`, `2026-09-20`** — `SelectionChangedNotification`, a plain managed FDP record in `Fdp.Toolkits`. ⛔ *Was: DOES NOT EXIST; both `SelectionChangedEvent` (`[DdsTopic]`) and `SelectionChangedEventDto` are NETWORK types.* |
| **request** event | ☑ **`S-2`, `2026-09-20`** — `SelectionChangeRequest` *(managed, entity-addressed, `Entities` + `Mode`)*. ⚠ `SelectEntityCommand` is **kept** as the network-id boundary *(§2.7.7 deviation ①)*. ⛔ *Was: only `SelectEntityCommand`, a single `long NetworkId`.* |

#### 2.7.2 The flow

```mermaid
sequenceDiagram
    participant S as Surface
    participant Bus as FdpEventBus
    participant Sys as SelectionRequestSystem
    participant ECS as SelectionState
    participant Tools as ToolController
    participant All as All surfaces

    S->>Bus: SelectionChangeRequest(entities, mode)
    Note over S: a panel knows ONLY this
    Bus->>Sys: consumed
    Sys->>ECS: apply - the ONLY writer
    Sys->>Bus: SelectionChangedNotification
    Bus->>All: repaint indicators
    Bus->>Tools: entity lost selection?
    Tools->>Tools: NotifyToolEnded for its armed tool
```

#### 2.7.3 The rules this encodes

| # | rule | source |
|---|---|---|
| **1** | ⭐⭐ **ONE writer.** `SelectionRequestSystem` is the only thing that mutates the truth | 🔒 *"the global ecs repo … no one else"* |
| **2** | **Every surface is a requester** — map, ECS inspector, DER inspector, orbat, **and the remote-map-control dispatcher** | 🔒 *"just another requester"* |
| **3** | ⛔ **No surface reads tool state**, and none writes the store | 🔒 *"panels … stay unaware of any map or map tools whatsoever"* |
| **4** | ⭐ Panels repaint from the **notification**; their own sets are VIEW state | 🔒 *"listen and react to selection changed"* |
| **5** | **Losing selection cancels that entity's edit** — the tool side observes, via `NotifyToolEnded` per `Tool_Model` §4.7h | 🔒 ruling ② |
| **6** | ⭐ Right-click SELECTS *(§2.3, and row 1 — an already-selected entity keeps the whole selection)* | 🔒 `2026-08-12` + `2026-09-10` |
| **7** | Selection is **HOST-LOCAL**; DDS carries it only for direct 2-D map control, translated at the boundary | 🔒 ruling ⑤/⑥ |
| **8** | ⛔ **Echo suppression at the EGRESS translator**, never by muting the notification | §2.6 |

#### 2.7.4 What must be deleted, not merely added

| 🔴 | |
|---|---|
| `DefaultSelectionState`'s own `HashSet` | ☑ **`S-1`** — becomes `EcsSelectionState`, a read-through *(the class survives for world-less hosts; §2.7.6 deviation ②)* |
| the **3 hand-rolled `SetSelected`** | ☑ **`S-2`** — and 🔴 **there were FOUR**: `SelectionInteractionSystem` *(discharged at `S-1`)* · `EditorSubsystem`'s `GlobalActionIds.Select` · `IgApplication.SelectEntityOnMap` · ⚠ **`EditorSubsystem.SetSelection2D`, which this list had MISSED**. 📄 §2.7.7 |
| `EntityInspectorPanel._selectedEntities` · `DerEntityInspectorPanel._selectedEntityId` | ☑/❌ **`S-3`** — the entity inspector's set is now a projection of `ISelectionState`; ⚠ the **DER** panel is untouched, it speaks DER ids and is `S-4`'s seam |
| `EntityInspectorPanel.ChainToMap` | ☑ **`S-3`** — retired with its operator toggle |
| `ScenarioOrbatAdapter`'s `ActivateEditorToolEvent(Select)` | ⭐ redundant once rule 5 is central |
| `IgApplication._fdpInspectorState` hand-sync | ☑ **`S-2`** — deleted with `SelectEntityOnMap`'s rewrite; `DrawUI`'s own change-detector does the follow-through *(§2.7.7 deviation ④)* |

#### 2.7.5 Sequencing — **derived from the measurements, not preference**

| # | slice | gate |
|---|---|---|
| ☑ **S-1** *(`2026-09-20`, §2.7.6)* | `ISelectionState` gains the multi mutators; `EcsSelectionState` replaces `DefaultSelectionState` | 🟡 the 4 stores become 1 + views — **2 of 4**; the rest lands at `S-3` |
| ☑ **S-2** *(`2026-09-20`, §2.7.7)* | the **request** event gains a set + mode; `SelectionRequestSystem` becomes the only writer | ☑ the hand-rolled writers are deleted *(4, not 3)*; 🟡 one writer survives — §2.7.7 deviation ② |
| ☑ **S-3** *(`2026-09-20`, §2.7.8)* | the **notification** event *(new, FDP-internal)*; panels subscribe | ☑ `R-134` met; 🟡 panels PROJECT rather than subscribe, and 🟡 the stores are **3 of 4** — SimHost remains |
| ⭐ **S-4 — NEXT** | right-click selects on every surface *(§2.3 incl. row 1)*; DER inspector gains a seam | ☑ `CE-259s`'s ordering **already discharged at `S-2`** — it stopped being latent the moment the write was deferred |
| **S-5** | rule 5 — losing selection cancels that entity's edit | ⭐ needs **S-4** *(a targeted arming does not select — `Tool_Model` §4.14)* |
| **S-6** | remote-map-control dispatcher becomes a requester; echo suppression moves to egress | 📄 `DESIGN_Remote_Map_Control.md` |

⚠ **`UXI-24` (multi-select) rides on S-1 + S-2** — its map additive-click needs the mutators and the mode.

#### 2.7.6 ☑ **`S-1` AS-BUILT — `2026-09-20`**

⭐ **What shipped, against §2.7.1's element table:**

| element | designed | as-built |
|---|---|---|
| `ISelectionState` mutators | `Add`/`Remove`/`SetMultiple`/`Clear` | ☑ as designed |
| `ISelectionState.Version` | ⛔ **not in the design** | ☑ **ADDED — see deviation ① below** |
| `EcsSelectionState` | *"read-through over the component"* | ☑ `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Selection/EcsSelectionState.cs` |
| `DefaultSelectionState` | *"becomes `EcsSelectionState`"* | ⚠ **kept, demoted** — see deviation ② |
| the 4 stores | *"become 1 + views"* | 🟡 **2 of 4 collapsed** — see the gate below |

```mermaid
classDiagram
    class ISelectionState {
        <<interface - the VIEW>>
        +IsSelected(e) bool
        +SelectedEntities
        +PrimarySelected
        +HoveredEntity
        +Version int
        +Add(e)
        +Remove(e)
        +SetMultiple(set)
        +Clear()
    }
    class EcsSelectionState {
        <<NEW - Hrot.Presentation>>
        +ClearCore()
        +SetSelectedCore(e, isPrimary)
    }
    class DefaultSelectionState {
        <<demoted - world-less hosts and tests only>>
    }
    class SimHostInspectorAdapter
    class SelectionStateComponent {
        <<ECS component - THE TRUTH>>
    }
    class SelectionInteractionSystem {
        <<delegates its writes now>>
    }
    class EditorSubsystem
    class CgfSubsystem

    ISelectionState <|.. EcsSelectionState
    ISelectionState <|.. DefaultSelectionState
    ISelectionState <|.. SimHostInspectorAdapter
    EcsSelectionState ..> SelectionStateComponent : reads AND writes
    SelectionInteractionSystem ..> EcsSelectionState : ClearCore / SetSelectedCore
    EditorSubsystem --> EcsSelectionState
    CgfSubsystem --> EcsSelectionState
```

⭐ *What the picture shows that the prose hid: `SelectionInteractionSystem` no longer writes the
component itself — it went from a parallel writer to a caller of the same view the hosts hold. That is
what collapses the writers, and it is invisible in a list of classes.*

##### ⚠ Deviations, and why

| # | deviation | why |
|---|---|---|
| **①** | ⭐ **`Version` added to the interface** — a per-observer change token | 📐 `EditorSubsystem.Selection2DVersion` exposed `DefaultSelectionState.Version`, so the host had to hold the CONCRETE type to reach it — the coupling `S-1` exists to remove. ⭐⭐ **And deriving it from the observed ECS truth is a free win**: `EditorStrideSubsystem.SyncSelection2D3D` polls it, so a MAP click now reaches the 3-D view. 🔴 It could not before — the hash set only bumped when editor code assigned through it |
| **②** | ⚠ **`DefaultSelectionState` kept, not deleted** | §2.7.1 itself specifies `DdsBackedSelectionState` for hosts with no world (ExCon) — **the same shape**. ⇒ deleting the one in-memory implementation and re-adding it at `S-3` is churn. ⭐ **The gate is enforced instead of the deletion**: `NoProductionHostKeepsAParallelSelectionStoreTests` fails if any host under `Hrot/` or `Stride/` constructs one, and it carries a negative control proving the scan reaches `CgfSubsystem.cs`/`EditorSubsystem.cs` |
| **③** | ⚠ **writes go DIRECT to the repository, not through a command buffer** — a deviation from **§2.5** | 📐 §2.5's own analysis already shows the corruption risk does not exist (`EntityQuery` is an index scan). ⛔ Deferral would put a one-frame lag inside `SyncSelection2D3D`'s same-frame read-back of `Selection2DVersion`. ⭐⭐ **`S-2` deletes the question**: every caller publishes a request, `SelectionRequestSystem` writes during its own tick — which §2.5's stated exception already blesses. ⇒ building an ECB path here is work `S-2` throws away |
| **④** | ⭐ **`SelectionInteractionSystem` delegates its component writes** to `EcsSelectionState.ClearCore`/`SetSelectedCore` | ⛔ not in the slice text, but it had its own private `SetSelected`/`ClearAllSelections` — **two implementations of "what selected looks like on the component"** (ruling 9). ⭐ Safe because the view is a read-through HANDLE with no store: two instances over one world cannot disagree |

##### 🟡 The gate, reported honestly — **2 of the 4 stores collapsed, not 4**

| store | after `S-1` |
|---|---|
| `DefaultSelectionState` × 2 hosts *(editor, CGF)* | ☑ **gone** — both now read through the ECS component |
| the `SelectionState` component | ☑ the one truth on those hosts |
| `SimHostSelectionManager` | ❌ **still separate** — SimHost has no FDP world behind its view |
| `SharedEntitySelection` *(`Hrot.Editor.AiShared.Selection`, wrapped per-editor by `EditorSelectionStore`; `CgfSubsystem.cs:199`)* | ❌ **still separate** |
| the Stride 3-D `SelectionState` | ❌ **still separate** — ⚠ **not in §2.7.4's delete-list**; it syncs through `Version` and is out of scope until someone rules on it |

⇒ 🔒 **The *"4 stores become 1 + views"* gate lands at `S-3`, not here** — the remaining three need the
notification event before they can become views. ⛔ Do not read `S-1` as having met it.

##### ⭐ Rails

| suite | |
|---|---|
| `SelectionInteractionSystemTests` *(the feature's own, `T-1`)* | **8/8 green** after the delegation — the behaviour-preserving check on deviation ④ |
| `EcsSelectionStateTests` *(new, 17)* | acceptance **11.1 · 11.2 · 11.3 · 11.9**, the `Version` contract, and the cached-query-sees-later-entities trap |
| `NoProductionHostKeepsAParallelSelectionStoreTests` *(new, 2)* | the gate, **red-proved** by re-introducing `new DefaultSelectionState()` in `CgfSubsystem` — it named the exact line |

#### 2.7.7 ☑ **`S-2` AS-BUILT — `2026-09-20`**

⭐ **What shipped, against §2.7.1's element table:**

| element | designed | as-built |
|---|---|---|
| **request** event | *"`SelectEntityCommand` exists but is `long NetworkId` — needs a set + a mode"* | ☑ **`SelectionChangeRequest`** *(NEW, managed, entity-addressed, `Entities` + `Mode` + `Reason`)*. ⚠ `SelectEntityCommand` **KEPT** as the network-id boundary — deviation ① |
| `SelectionChangeMode` | *"replace · add · remove · clear"* | ☑ as designed |
| `SelectionRequestSystem` | *"`SelectEntitySystem` is its single-entity ancestor"* | ☑ **renamed** *(Roslyn)* and extended; consumes **both** events |
| the 3 hand-rolled `SetSelected` | *"all become requests"* | ☑ **all four** — ⚠ the delete-list had **missed one**, see below |

```mermaid
sequenceDiagram
    participant S as Surface (menu, orbat, IG remote)
    participant Bus as FdpEventBus
    participant Req as SelectionRequestSystem
    participant View as EcsSelectionState
    participant Drain as ToolActivationDrainSystem

    S->>Bus: SelectionChangeRequest(entities, mode)
    S->>Bus: ActivateEditorToolEvent(tool)
    Note over S: both in ONE frame
    Bus->>Req: consumed FIRST
    Req->>View: SetMultiple / Add / Remove / Clear
    View->>View: writes SelectionState component
    Bus->>Drain: consumed SECOND
    Drain->>View: reads the NEW primary
```

⭐ *What the picture shows that the prose hid: the two systems' ORDER is part of the contract. The drain
reads the selection the request just wrote, so registering them the other way round arms the tool on the
previous selection — silently.*

##### 🔴 A finding: **the delete-list was short by one**

📐 §2.7.4 named three hand-rolled `SetSelected`. Measured `2026-09-20`, there were **four**:

| site | disposition |
|---|---|
| `SelectionInteractionSystem` | ☑ discharged at **`S-1`** *(delegates to the view)* |
| `EditorSubsystem`'s `GlobalActionIds.Select` handler | ☑ publishes a request |
| `IgApplication.SelectEntityOnMap` | ☑ publishes a request |
| 🔴 **`EditorSubsystem.SetSelection2D`** — **not in the delete-list** | ☑ reduced to a one-line view write — deviation ③ |

⇒ ⭐ **that is why the gate is now a SOURCE SCAN** *(`OnlyTheViewWritesTheSelectionStateComponent`)* and
not a checklist: a checklist is only as complete as the sweep that wrote it.

##### ⚠ Deviations, and why

| # | deviation | why |
|---|---|---|
| **①** | ⭐ **`SelectEntityCommand` kept** beside `SelectionChangeRequest` | 🔒 §2.7.3 rule 7 — selection is host-local and the wire form is **translated at the boundary**. Panels and the orbat address entities by **network id** and must not learn ECS handles; the internal request speaks the host's own handle. ⇒ two *addressings* of one concept, **one consumer, one writer** — ⛔ not two implementations |
| **②** | ⛔ **`SelectionInteractionSystem` still writes** *(through the view)*, so `SelectionRequestSystem` is **not literally the only writer** | 📄 §2.5's own blessed shape is *"a system writing during its own main-thread tick"*, which is what both are. ⛔ Converting it needs the request system on **ReplayBrowser and SimHost** *(which run it and have no request system)* and a rework of `OnSelectionChanged`'s immediate-callback contract — real regression risk on hosts this lane cannot run. ⭐ `S-4` touches this path anyway *(right-click selects on every surface)*; it lands there |
| **③** | ⚠ **`SetSelection2D` and the `Selected2DEntity` setter stay synchronous** view writes | 📐 `SetSelection2D`'s ONE caller is `EditorStrideSubsystem.SyncSelection2D3D`, which reads `Selection2DVersion` **back in the same frame** to arm its anti-bounce tracker; a deferred request has not bumped it by then. ⛔ `HrotStrideApp.Game` is `net8.0-windows` and **cannot be built or tested on this lane** ⇒ the change with the least untestable risk wins. ⭐ Both are now **one line over the shared view** instead of a hand-rolled loop |
| **④** | ⭐ **`IgApplication`'s two hand-syncs deleted**, not merely bypassed | 📐 `SelectEntityOnMap` pre-set `_fdpInspectorState` and `_fdpLastMapSelection` so `DrawUI`'s change-detector would see *"no change"*. ⛔ Under a one-frame deferral that pre-set **inverts** — for one frame the component holds the OLD entity and the tracker the new one, so the detector pushes the OLD one back. ⭐ Letting the detector do its own job is correct by construction |
| **⑤** | ⭐ **`SelectionRequestSystem` registered on IG** | 📐 Measured: `ScenarioEditorModule` is registered by the **editor and CGF only** ⇒ on IG `SelectEntityCommand` had **no consumer at all** — the same silent no-op `CE-051` found elsewhere. ⛔ Publishing a request from IG without this would have recreated it exactly |

##### ☑ `CE-259s` discharged — **it stopped being latent the moment the write was deferred**

📌 `CE-259s` *(the tool drain registered before the select system ⇒ the menu arms on the pre-menu
selection)* was parked with the lean *"resolves with `S-4`"*. ⛔ **It could not wait:** *"select this,
then arm Rotate"* is two events in one frame, and once the select became a request the drain read the
**previous** selection. ⇒ `ScenarioEditorModule` now registers **`SelectionRequestSystem` first**, and
the registration-order rail is the only thing holding it.

##### 🟡 The gate, reported honestly

| §2.7.5 gate for `S-2` | result |
|---|---|
| *"the 3 hand-rolled writers are deleted"* | ☑ **met, and it was 4** — `new SelectionState {` now appears in **zero** production files outside `EcsSelectionState` |
| *"`SelectionRequestSystem` becomes the ONLY writer"* | 🟡 **one writer remains**: `SelectionInteractionSystem`, via the shared view — deviation ② |
| the 4 stores | unchanged from `S-1` — **2 of 4**; the rest needs `S-3`'s notification |

##### ⭐ Rails

| suite | |
|---|---|
| `TheViewportInteractionIsSharedTests` *(the system's own suite, `T-1`)* | **+5**: replace-a-set · add/remove modes · clear · a dead entity in the request · **the registration order** |
| `NoProductionHostKeepsAParallelSelectionStoreTests` | **+1**: `OnlyTheViewWritesTheSelectionStateComponent`, a whitelist scan |
| `TheSharedViewportEventsArePublishableAfterOnlyTheSharedRegistry` | extended with the new event — ⛔ under strict mode an unregistered publish throws, which was the `2026-08-27` CGF crash |
| 🔴 **`SetSelectionCommandTests` *(IG's own, `OC1-G001`)* — it CAUGHT the change** | ⭐⭐ **the `T-1` payoff, and I had missed this suite on the first sweep.** It asserted the component **immediately** after `CMD_SET_SELECTION`; a deferred request had not landed. ⭐ Fixed by pumping one kernel frame, which makes it **stronger**: it now proves request → bus → *the system being registered on IG at all* → view, where before it proved only that a private method wrote two booleans |

##### ⚠ A finding filed, not fixed — **a process-global flag under parallel tests**

📐 `TheSharedViewportEventsArePublishableAfterOnlyTheSharedRegistry` must flip
`FdpConfig.EnforceExplicitEventRegistration`, a **process-global**, because registration can only be
observed by publishing under strict mode *(⛔ `FdpEventBus.HasEvent`/`HasManagedEvent` do **not** answer
"is it registered" — they answer *"was one published THIS FRAME"*; read the body before reaching for
them)*. xUnit serialises within a collection but runs other collections in parallel ⇒ whichever class
happens to publish a managed event during that window throws.

📌 Documented `2026-09-09` against `JsonEntityContextMenuHandlerTests`; `S-2`'s five added tests changed
the scheduling and it surfaced on `EditorOrbatAdapterTests` and once on `EditorMapPickAdapterTests`.
⭐ `EditorOrbatAdapterTests` joined the serialized collection; ⛔ **that treats a victim, not the cause.**

🔒 **Lean, for whoever picks it up:** `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
on `Hrot.Editor.Tests` *(~4 s → ~10 s)*, or give `FdpConfig` an `AsyncLocal` override so a test's flip
cannot escape its own flow. ⚠ Out of `S-2`'s scope — it is a suite-wide policy call.

#### 2.7.8 ☑ **`S-3` AS-BUILT — `2026-09-20`**

| element | designed | as-built |
|---|---|---|
| **notification** event | *"DOES NOT EXIST. Both `SelectionChangedEvent` and `SelectionChangedEventDto` are NETWORK types ⇒ `R-134` requires a NEW FDP-internal record"* | ☑ **`SelectionChangedNotification`** — a plain managed record carrying the **whole** selection + primary. ⛔ Neither DDS type is touched |
| panels subscribe | *"panels repaint from the notification; their own sets are VIEW state"* | 🟡 **panels PROJECT, they do not subscribe** — deviation ② |
| `EntityInspectorPanel._selectedEntities` | *"view state fed by the notification"* | ☑ a projection of `ISelectionState`, refreshed every draw; clicks publish requests |
| `EntityInspectorPanel.ChainToMap` | ⛔ *"retired — a panel may not know a map exists"* | ☑ **gone**, with its operator toggle |
| `IgApplication._fdpInspectorState` hand-sync | *"a notification consumer"* | ☑ replaced by `SelectionNotificationSystem`; IG's map-selection tracker deleted |
| `DerEntityInspectorPanel._selectedEntityId` | *"view state fed by the notification"* | ❌ **not done** — it is `S-4`'s seam *(it speaks DER ids, not `Entity`)* |

```mermaid
graph TD
    Panel["EntityInspectorPanel<br/>(Fdp.Presentation)"] -->|SelectionChangeRequest| Bus[FdpEventBus]
    Map["map gizmos / context menus / ExCon"] -->|SelectionChangeRequest| Bus
    Bus --> Req[SelectionRequestSystem]
    Req -->|writes| View[EcsSelectionState]
    Req -->|SelectionChangedNotification| Bus2[FdpEventBus]
    Bus2 --> Notify[SelectionNotificationSystem]
    Notify -->|SelectedEntity| Ctx[IInspectorContext]
    View -.->|projected every draw| Panel
    DER["DerEntityInspectorPanel<br/>NOT WIRED - S-4"]:::dead
    classDef dead stroke-dasharray: 5 5,color:#888
```

⭐ *What the picture shows that the prose hid: the panel has TWO edges of different kinds — a solid one
out (it requests) and a dotted one in (it re-reads, it does not listen). The dashed box is the surface
still outside the protocol.*

##### 🔴 A layering finding: **`S-2` put the request event in the wrong assembly**

📐 `Fdp.Presentation` references `Fdp.Core`, `Fdp.Toolkits` and the ExtDeps — ⛔ **never `Hrot.Core`**.
`S-2` declared `SelectionChangeRequest` in `Hrot.Common.Events`, which made §2.7.3 **rule 2**
*("every surface is a requester")* **unbuildable for the ImGui panels** — half the surfaces in the design.
⇒ ⭐ both events now live in **`Fdp.Toolkits`** *(`Fdp.Toolkit.Vis2D`)*, the one layer the panels **and**
`Hrot.Core`'s registry can both see. ⚠ `SelectEntityCommand` stays in Hrot: it is the **network-id**
boundary and a network id is a Hrot concept.

##### ⚠ Deviations, and why

| # | deviation | why |
|---|---|---|
| **①** | ⭐ **`SelectionNotificationSystem` is a SYSTEM, not a panel subscription** | ⛔ A bus event is readable for exactly one frame. A system runs every frame by construction; an ImGui panel does not — collapsed, on a hidden tab, or not drawn, it misses the event and is stale **forever** |
| **②** | 🟡 **Panels PROJECT from `ISelectionState` each draw rather than subscribing** | same hazard, same answer: re-reading is idempotent and cannot miss. ⭐ The design's intent — *"their own sets are VIEW state"* — is met; ⛔ the mechanism is a pull, not a push. The notification still exists and has real consumers: the ones that need an **edge** *(`S-5`'s "losing selection cancels that entity's edit" is the next)* |
| **③** | ⚠ **BEHAVIOUR CHANGE on IG: clearing the selection now clears the inspector** | 📐 IG's retired detector *deliberately refused* to clear, to protect a list-made selection from a map clear. 🔒 Ruling ① removes the premise — one selection per host means a list selection and a map selection **are the same thing**. ⭐ Railed by `ClearingTheSelectionClearsTheInspectorContext` |
| **④** | ⛔ **ReplayBrowser is deliberately NOT wired** | it inspects a **recording**; there is no global selection for it to agree with, and inventing one would be the parallel store `S-1` removed. ⭐ It keeps its own set and its `OnEntitySelected` — now fired unconditionally, since `ChainToMap` was the gate |

##### 🔴 The defect the rails caught — **a `Clear` was never announced**

📌 `Apply`'s `Clear` branch returned **before** the announcement, so emptying the selection told nobody
and every subscriber kept painting what had just been cleared — the *"accepted and silently discarded"*
shape this programme keeps producing. ⭐ Caught by `ClearingTheSelectionClearsTheInspectorContext`
within minutes of being written. ⚠ **Note what did NOT catch it:** the build, and every other rail.

##### 🟡 The gate

| | |
|---|---|
| ☑ *"⛔ `R-134`: no DDS type in the internal path"* | **met** — a plain managed record; neither `SelectionChangedEvent` (`[DdsTopic]`) nor `SelectionChangedEventDto` is referenced |
| 🟡 the 4 stores → 1 + views | **3 of 4.** ☑ editor · CGF · IG; ❌ **`SimHostSelectionManager` remains.** 📐 Measured: SimHost has a world **and** runs `SelectionInteractionSystem` (writing the component) **and** keeps a separate manager for its panels ⇒ the same desync, on that host. 🔒 **Lean: give SimHost an `EcsSelectionState` and retire `SimHostSelectionManager`** — ⛔ §2.7.1's `DdsBackedSelectionState` is for hosts with **no** world, which SimHost is not. ⚠ Not built here: it retires a type with its own `IInspectorContext` wiring, and this lane cannot run SimHost |
| ❌ `DerEntityInspectorPanel` | untouched — §2.7.5 assigns its seam to **`S-4`** |

## 3. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 11.1 | `PrimarySelected` set through the view is visible in the **component** — the desync regression guard | H |
| 11.2 | A component change is visible through the **view** — both directions | H |
| 11.3 | Exactly one entity has `IsPrimarySelection` after any selecting operation | H |
| 11.4 | 🔒 Right-click on a **selected** entity leaves the selection **unchanged** | H |
| 11.5 | 🔒 Right-click on an **unselected** entity clears the selection and selects **only** it | H |
| 11.6 | 🔒 Right-click on **empty space** clears the selection | H |
| 11.7 | The menu is populated **after** the selection mutation, same frame | H |
| 11.8 | 🔒 **Reload clears the selection** — `ClearAll()` is actually called (it never is today) | H |
| 11.9 | Despawning the primary leaves **no stale primary** in the view | H |
| 11.10 | 🔒 Multi-selection menu = items applicable to **all**; anything applicable to only *some* is **absent** — not greyed, no reason ([ruling 47](UX_RESUME_INTERACTION.md)) | H |
| 11.11 | An action on a 12-entity selection executes **12 times**, once per entity | H |
| 11.12 | Ctrl/Shift click **adds to** the selection instead of replacing it | H |
| 11.13 | Selection in one subsystem does not appear in another (subsystem-local) | H |
| 11.14 | ExCon's `ISelectionState` implementation satisfies the same tests with **no ECS world** | H |
| 11.15 | **CGF**: click an entity → ring appears, inspector follows — the full chain | I |
| 11.16 | **SimHost**: the ring is visible (today it selects invisibly) | I |
| 11.17 | Rubber-band over 5 entities → 5 selected, 1 primary, ring on all | I |
| 11.18 | 🔒 **Multi-delete raises a modal confirmation** naming the count; Cancel deletes **nothing** | H |
| 11.19 | A view-driven selection change is **recorded on the command buffer**, not written directly, and is visible after the next flush | H |
| 11.20 | 🔒 **Every map subsystem emits the identical pick box** — same size, same transparency, same pipeline target — from the one shared helper | H |
| 11.21 | 🔒 **Every map subsystem registers the identical ring** — same radius, thickness and colours; **no host-specific override exists** | H |
| 11.22 | The pick target is **not smaller than the ring** — what is visible is clickable | H |

**19 H · 3 I · 0 V.**

## 4. 🔒 Out of scope

| | |
|---|---|
| The action vocabulary itself | [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) / [UXI-04](UX_Feature_Cross_Surface_Actions.md) |
| CGF's pick box | [UXI-10](UX_Feature_Entity_Symbology.md) — this design **depends** on it |
| The rest of CGF/SimHost map parity | [UXI-23](UX_Issues.md#uxi-23) |
| Selection over the wire for ExCon | its transport already exists; only the interface binding is in scope |
| **Re-designing** how the ring or pick box *looks* | 🔒 but their **sharing is in scope** (§2.4b) — one implementation, registered everywhere, no per-host variants |

## 5. Risks

| | |
|---|---|
| ⚠ **`ISelectionState` becomes a view — writes now have side effects** | ✅ **closed, §2.5** — writes go through the command buffer, so no call site can be wrong. ⚠ Callers that read back the selection **in the same frame** will see the old value |
| ⚠ **Same-frame ordering (§2.3)** is invisible when wrong | 11.7 is the guard; prefer an explicit "selection settled" point over relying on call order |
| ⚠ **Right-click-selects changes Editor behaviour** for anyone who relied on right-click *not* disturbing a selection | it is the ruling, and it matches file-manager convention; note it in the changelog |
| 🔒 **Fan-out multiplies side effects** — *Delete* on 12 entities is 12 deletes | 🔒 **RULED (user, 2026-08-12): multi-delete gets a modal confirmation dialog.** ⭐ Intersects [UXI-16](UX_Issues.md#uxi-16) (no `Delete` confirms anywhere, in any host) — that issue now has a hard requirement rather than a preference, and [ruling 13](UX_RESUME_INTERACTION.md)'s modal-dialog machinery is the vehicle |
| ⚠ **CGF depends on UXI-10 landing first** | strict order: pick box → interaction system → component → ring |
