<!--STATUS
state: LIVE
build-state: BUILT — 2026-08-25 (BP-507). All of Q55-A..E built as ruled, with ONE named deviation from
  the classDiagram: the window takes a WatchEntityPicker DELEGATE, not IMapPickService (assembly
  boundary — see "AS-BUILT" below). Carries classDiagram + sequenceDiagram.
updated: 2026-08-25
current-answer: the whole file — how a watch row binds to an ARBITRARY concrete entity (one that is NOT the
  current selection). §"RESOLVED" carries the recommended answers; the sub-questions carry the reasoning.
design-basis: DESIGN_Variable_Watch_Pinning.md §3 (the TWO-KIND binding: concrete NetworkId vs chameleon) + §9c
  (the picker, previously "a lead, not a decision"). This question SETTLES §9c. HANDOFF_Watch_List_Finalization.md
  deferred it here.
known-conflict: none. ⚠ Surfaces two ruling-9 duplicates (two IMapPickService, two MapPickableEntityAttribute) —
  flagged, NOT resolved here; the watch reuse consumes ONE and the reconciliation is a separate cleanup.
-->
# Architect Question 55 — **binding a watch to an arbitrary concrete entity (the picker)**

> 📄 **Settles `DESIGN_Variable_Watch_Pinning.md` §9c**, which named a `MapPickableEntityAttribute` lead but was
> explicitly *"not measured; a lead, not a decision."* The watch-list finalization batch
> *([`HANDOFF_Watch_List_Finalization.md`](batches/HANDOFF_Watch_List_Finalization.md))* ships the
> concrete-vs-chameleon CHOICE via the **current selection**; this question settles how to bind a concrete entity
> that is **not** currently selected — i.e. the picker.

## The question, in one line
When a designer wants to watch a variable on a **specific** entity they are not currently selecting, how do they
pick that entity — and what does the watch store so the binding is restart-stable *(§3: a `NetworkIdentity.Value`,
never a recycled `Entity` handle)*?

## INVENTORY — measured `2026-08-24` *(seam-law: a picker already exists and is adopted)*

```
search_graph(name_pattern=".*(MapPick|PickService|PickableEntity).*", label="Interface")
grep MapPickableEntityAttribute · IMapPickService · PickEntityAsync
```

| exists? | mechanism | where | note |
|---|---|---|---|
| ✅✅ | **`IMapPickService.PickEntityAsync(string[]? filterPresets, ct) : Task<int>`** | `Hrot.Presentation/Facades/IMapPickService.cs` *(in-degree 8)* | ⭐⭐⭐ **returns the picked entity's `NetworkId` (int)** — 📐 `EditorSubsystem.cs:1916` `int targetNetId = await _mapPickAdapter.PickEntityAsync()`. **This IS §3's restart-stable identity, already produced by an existing seam** |
| ✅ | **`EditorMapPickAdapter : IMapPickService`** | `Hrot.Editor/Adapters/EditorMapPickAdapter.cs:33` | the editor's impl; async — enters a map-pick mode, resolves when the user clicks an entity |
| ✅ | **adopted across hosts** | editor · IG · CGF · SimHost · ExCon | the pick affordance is not editor-only — mission/spawn/attribute editing already use it |
| ⚠🔴 | **TWO `IMapPickService` interfaces** | `Hrot.Presentation/Facades` **and** `Hrot.ExCon/Services` *(in-degree 22)* — same signature | ruling-9 duplicate — flagged below |
| ⚠🔴 | **TWO `MapPickableEntityAttribute`** | `Fdp.Toolkits/Behavior/Attributes` **and** `Fdp.Presentation/ImGui/Editing/PickerAttributes` | the DECLARATIVE variant — mark a DTO field ⇒ auto-rendered pick button *(mission/behavior params)*. Also a duplicate |
| ✅ | the watch binding target today | `VariableRowOrigin.Entity` *(an `Entity` handle; `default` = chameleon)* | §3 wants this to become a NetworkId for the concrete case — ④ of the finalization batch |

⇒ ⭐⭐⭐ **The seam law holds exactly:** *"we need a picker"* = a picker exists *(`IMapPickService.PickEntityAsync`)*,
is adopted everywhere, and **already returns the NetworkId the watch binding needs.** This is a reuse question,
not a build.

## ✅ RESOLVED — recommended answers *(✅ APPROVED by the user 2026-08-24)*

| # | sub-question | ✅ recommended answer |
|---|---|---|
| **Q55-A** | Build a new picker, or reuse? | ✅ **REUSE `IMapPickService.PickEntityAsync`.** It already returns a `NetworkId`, is host-agnostic, and its async "enter pick mode → click → resolve" shape is exactly the gesture. ⛔ Do not build a watch-specific picker — that would be a third pick path |
| **Q55-B** | Which `IMapPickService` *(two exist)*? | ✅ **the `Hrot.Presentation/Facades` one** — it lives in the shared presentation assembly the watch panel already depends on, and `EditorMapPickAdapter` implements it. ⚠ The `Hrot.ExCon/Services` twin is a **ruling-9 duplicate**; ⛔ **not reconciled here** — filed as a follow-up. The watch consumes the Facades one |
| **Q55-C** | How is the pick invoked from the watch UI, and what is stored? | ✅ **a "pin on entity…" watch action** *(beside the existing "pin chameleon")*: call `PickEntityAsync()`, take the returned `NetworkId`, and create a **concrete** `WatchPin` carrying that `NetworkId` *(identity/persistence)* + the resolved in-session `Entity` *(display)*. ⛔ **Direct call, NOT the attribute path** *(Q55-D)* — a watch pin is a first-class gesture, not a DTO form field |
| **Q55-D** | Reuse the declarative `MapPickableEntityAttribute` instead? | ✅ **NO for the watch pin** — the attribute auto-renders a pick button on a **DTO field** *(mission/spawn params)*; a watch binding is not a serialized DTO field, so the direct `PickEntityAsync` call is the right fit. ⚠ The **two** `MapPickableEntityAttribute` definitions are a separate ruling-9 duplicate — flagged, not this batch |
| **Q55-E** | Filter what is pickable? | ✅ **pass no filter for v1** *(`PickEntityAsync()` with `filterPresets = null` = any entity)*. ⭐ `filterPresets` is there if we later want "only entities of this asset/type" — a cheap round-out, not required now |

⭐⭐ **Reuse-vs-build tradeoff, per sub-question:** every answer is REUSE except the thin new UI action (Q55-C) —
one call site into an existing service. ⛔ **The only genuinely new code is the "pin on entity…" action and the
concrete-`WatchPin` construction**, both of which the finalization batch's ④ already introduces the binding model
for. So AQ55 is the *"how to set the concrete binding to a non-selected entity"* cap on that item.

## ⭐ UML

```mermaid
classDiagram
    direction LR
    class IMapPickService {
        <<exists · Hrot.Presentation/Facades · REUSE>>
        +PickEntityAsync(filterPresets, ct) Task_int
    }
    class EditorMapPickAdapter {
        <<exists · enters map-pick mode, resolves on click>>
    }
    class WatchPin {
        <<the two-kind binding · finalization item 4>>
        +long NetworkId
        +bool IsChameleon
    }
    class AiWatchWindow {
        <<exists · adds the pin-on-entity action>>
        +PinOnPickedEntity()
    }
    class NetworkEntityMap {
        <<exists · resolves NetworkId to Entity in-session>>
    }
    IMapPickService <|.. EditorMapPickAdapter
    AiWatchWindow ..> IMapPickService : PickEntityAsync
    AiWatchWindow ..> WatchPin : creates concrete
    WatchPin ..> NetworkEntityMap : resolve NetworkId in-session
    note for WatchPin "concrete = NetworkId (restart-stable) + in-session Entity; chameleon = follows selection"
```

```mermaid
sequenceDiagram
    autonumber
    participant U as designer
    participant W as AiWatchWindow
    participant P as IMapPickService
    participant Pin as WatchPin

    U->>W: pin-on-entity action
    W->>P: PickEntityAsync
    Note over P: enters map-pick mode, waits for a click
    U->>P: clicks an entity on the map
    P-->>W: NetworkId of the picked entity
    W->>Pin: create concrete pin with that NetworkId
    Note over Pin: identity is the NetworkId — in-session value resolved via NetworkEntityMap
```

## ⚠ Two ruling-9 duplicates this question SURFACES but does NOT resolve

⭐ Filed so they are not forgotten; ⛔ **out of scope for the watch reuse** *(which consumes exactly one of each)*:
1. **`IMapPickService` ×2** — `Hrot.Presentation/Facades` vs `Hrot.ExCon/Services`, identical signature. Reconcile to one shared seam *(its own cleanup batch)*.
2. **`MapPickableEntityAttribute` ×2** — `Fdp.Toolkits/Behavior` vs `Fdp.Presentation/ImGui`. Reconcile *(its own cleanup)*.

## ✅ AS-BUILT — `2026-08-25` (`BP-507`) *(obligation ⑤)*

📄 The full as-built lives in
**[`DESIGN_Variable_Watch_Pinning.md` § AS-BUILT — the entity-pinning finish](DESIGN_Variable_Watch_Pinning.md)**.
Here: what matched, and the one thing that did not.

| ruling | as built |
|---|---|
| **`Q55-A`** REUSE the existing pick service | ✅ `EditorSubsystem.PickWatchEntityBindingAsync` calls `IMapPickService.PickEntityAsync()` and resolves the returned id through the existing `FindEntityByNetworkId`. ⛔ No second picker |
| **`Q55-B`** the `Hrot.Presentation/Facades` twin | ✅ — via `EditorMapPickAdapter`, the editor's impl of that interface. ⛔ The `ExCon` twin untouched |
| **`Q55-C`** a "pin on entity…" action creating a CONCRETE pin | ✅ `VariableWatchGesture.PinOnEntityLabel` = *"Watch this variable on entity…"*, on the variable row menu beside *"Watch this variable"* — ⭐ the only surface where a VARIABLE is chosen. The pin carries the `NetworkId` + the resolved in-session `Entity` |
| **`Q55-D`** ⛔ not the attribute path | ✅ direct call |
| **`Q55-E`** no filter in v1 | ✅ `PickEntityAsync(null, ct)` |

### ⛔ The ONE deviation — from the `classDiagram`, argued

⭐ **Drawn:** `AiWatchWindow ..> IMapPickService : PickEntityAsync`.
⛔ **Built:** `AiWatchWindow` takes a **`WatchEntityPicker` delegate**; the composition root implements it.

📐 **Why:** `IMapPickService` lives in `Hrot.Presentation`, which **`Hrot.Editor.AiShared` does not
reference** — and adding that edge points the shared editor library at the application layer that
composes it. ⭐ The codebase's settled shape for *"an AiShared window needs a host capability"* is a
host-installed delegate — `SetRunStateSource`, `SetFacetEditService`, `SetFacetDispatcher`.
⇒ ⭐⭐ **the SERVICE is reused exactly as `Q55-A` ruled; only the way its type crosses the assembly
boundary differs.**

⭐ **Two behaviours the design did not state, decided while building and worth pinning:** a **cancelled**
pick pins NOTHING and ⛔ does not fall back to the selection; a **chameleon** returned by a picker is
REFUSED — the gesture promised a specific entity.

⚠ **The two ruling-9 duplicates below are still OPEN and untouched**, as this question directed.

## ⛔ NOT this question
⭐ Restart SURVIVAL of the concrete binding *(resolving the stored `NetworkId` back to a live `Entity` after a
scenario restart, via the remap)* is **slice `94g`** — HN-037's lane *(`DESIGN_Deterministic_Network_Ids.md` §11
territory)*. AQ55 stores the `NetworkId`; 94g makes it survive a restart.
