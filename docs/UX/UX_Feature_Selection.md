# Feature design — selection

> **Design for [UXI-11](UX_Issues.md#uxi-11) · drafted 2026-08-12.** **Status: ✅ designed — ready to break
> into `UXT` tasks.** Implements [rulings 27-28](UX_RESUME_INTERACTION.md). Feeds
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
| ✅ **Menu handlers need no change** | they still set `PrimarySelected`; it now reaches the component |
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
| ⚠ **Ctrl/Shift additive click does not exist** | verified — click is unconditionally `SetSelected(entity, isPrimary: true)` (`:83`). Rubber-band is the only route to a multi-selection today |
| 🔒 **Menu = items applicable to *all* selected** | the AND/intersection rule, per the ruling |
| **Execution fans out** over the selection — [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s `EntityActionDescriptor` is the layer that carries it | ⚠ **not** `IEntityActionController`, which is single-entity by signature |

⚠ **A heterogeneous selection can empty the menu**, which reads as broken. Reuse the programme's
established principle ([UXI-08](UX_Feature_Layout_Defaults.md) case 5 — *disabled with a reason, not
hidden*), but scaled so it does not become noise:

| Applicable to… | Treatment |
|---|---|
| **all** selected | shown, enabled |
| **some** selected | 🔒 **shown, disabled, with a reason** — *"3 of 12 selected support this"* |
| **none** selected | hidden |

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
| 11.10 | Multi-selection menu = items applicable to **all**; partial ⇒ **disabled with a reason**; none ⇒ hidden | H |
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
