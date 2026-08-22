<!--STATUS
state: LIVE
build-state: DESIGN (OPEN architect question — a decision to resolve with the user; not yet buildable)
updated: 2026-08-22
current-answer: the whole file — how the subtree-sync IDENTITY (`_syncNodeMeta`) survives a reload so
  Approach-B orchestrator emission works after load WITHOUT re-opening the Inspector on every node. This is
  the deferred decision behind BP-399's S4 (details.parametersync) and BP-342 gap ①. Options A/B/C with a
  recommended lean; resolve with the user, then it unblocks S4+S5 and closes BP-399.
design-basis: BP-342 (open) · R-99 ("promoting an inert panel is worse than leaving it buried") ·
  DESIGN_Details_Panel_View_Switching.md §7.6 ④ · the UI session's S4-deferral note (2026-08-22).
known-conflict: none.
-->
# Architect Question 49 — **how does the subtree-sync identity survive a reload?**

> 🔴 **The bug, in one line:** after a reload, BTree **Approach-B** subtree-sync emits **nothing** until a
> designer re-opens the Inspector panel for every affected node. ⇒ the sidecar the panel authors doesn't
> reliably reach the runtime, which is why **S4** *(promote `details.parametersync` to the Details panel)* is
> deferred by **R-99** — *"promoting an inert panel is worse than leaving it buried."*

## Why it's inert

`GetApproachBSyncGroups()` needs **two** things per subtree node: the **bindings** *(which vars copy in/out)*
and the **identity** *(which subtree, which param DTO)*. The bindings survive a reload; the identity does not.

## INVENTORY — measured 2026-08-22

| symbol | where | role |
|---|---|---|
| **`_syncBindings`** → persisted as **`SubtreeSyncBindings`** | `BehaviorTreeAsset` · `BehaviorTreeAssetDto:354` | ✅ **survives reload** — the copy-in/out bindings |
| ⛔ **`_syncNodeMeta`** `Dictionary<Guid,(SubtreeName, SubDtoTypeName, SubDtoTypeNs)>` | `BehaviorTreeAsset:243` | ⛔ **session-local — LOST on reload.** Its **only writer** is `RecordSubtreeNodeMeta` *(→ `InspectorWindow:590`, a UI draw)* |
| **`GetApproachBSyncGroups()`** | `BehaviorTreeAsset:707` | iterates `_syncBindings`; ⛔ **skips any node whose `_syncNodeMeta` is absent** *(:719 `continue`)* ⇒ after reload, all skipped |
| **`ApproachBSyncGroup(nodeId, SubtreeName, SubDtoTypeName, SubDtoTypeNs, bindings)`** | `Hrot.Editor.AiShared/Blackboard/ApproachBSyncGroup.cs:25` | the emitted group — **4 identity fields + the bindings** |
| **`BTreeDtoRuntimeFieldExclusionTests`** | `…AiEditor.Persistence.Tests/BTree/` | ⛔ **the rail that DELIBERATELY excludes `_syncNodeMeta` from the DTO** — the thing Option A reddens |
| consumer | `BTreeOrchestratorEmitter:48` → `BTreeOrchestratorEmitCore` | the editor passes its `_syncNodeMeta`-derived groups; **the source generator passes EMPTY** *(by design; `BTreeJsonGenerator:286`)* |

⭐⭐ **The load-bearing fact:** `SubDtoTypeName`/`SubDtoTypeNs` describe the **subtree the node calls** — they
belong to the **callee**, not the calling node. The node already persists **which subtree it calls** *(core node
data)*. ⇒ the identity is **derivable from the subtree reference**; it is not authored data unique to the caller.

## The options

| # | option | how | ⭐ pro | ⛔ con / blast radius |
|---|---|---|---|---|
| **A** | ⭐ **PERSIST `_syncNodeMeta` in the DTO** | widen `BehaviorTreeAssetDto`; reclassify it from "session-local" to "authoring data" | direct; identity survives trivially | ⛔ **reddens `BTreeDtoRuntimeFieldExclusionTests`** *(must be updated with a documented reclassification)*; **duplicates the subtree's DTO-type in the caller** ⇒ can DRIFT if the subtree's DTO changes; DTO schema change + goldens + migration |
| **B** | **recover from `SubtreeSyncBindings`** | rebuild identity from the persisted bindings | no DTO change | ⛔ **INSUFFICIENT alone** — the bindings carry the node id and var names, **not** `SubDtoTypeName`/`Ns`; only ~2 of the 4 fields recover |
| **C** | ⭐⭐⭐ **RECOMPUTE at load from the subtree reference** | on load, for each subtree node: resolve the subtree it calls → read that subtree's param-DTO type → re-populate `_syncNodeMeta` *(exactly what `RecordSubtreeNodeMeta` does, but from the authoritative source instead of a UI draw)* | ⭐ **no DTO widening, no rail change, NO duplication** — one source of truth *(the subtree)*; the exclusion rail stays correct | ⚠ needs the **load path to resolve the subtree asset + its param-DTO type** — feasibility hinges on that being available at load *(the build session must confirm)* |

## ⭐ Recommended lean — **C, with A as the fallback**

⭐⭐ **C is the right shape:** the identity is *derived* data *(it belongs to the subtree)*, and the exclusion
rail was **right** to keep it out of the DTO. The defect is not "the DTO is missing a field" — it is **"nothing
recomputes the derived identity at load."** So the fix is a **load-time recompute** *(mirror
`RecordSubtreeNodeMeta` from the resolved subtree)*, which fixes the reload without persisting redundant,
drift-prone data or touching the rail.

⚠ **The one thing that could force A:** if the subtree's param-DTO type is **not resolvable at load** *(e.g. it
requires compilation, or the subtree asset may be absent)*, C cannot run, and A becomes the pragmatic answer —
in which case the rail update is legitimate *(the field is reclassified as authored, with the reason recorded)*.
⛔ **B is not a standalone answer** — it recovers only part; it can *feed* C *(the bindings half)* but cannot
supply the DTO type.

## To resolve

⭐ **The one measurement that decides C vs A:** *at load, from a subtree node, can we resolve the called subtree
and read its param-DTO type name + namespace?* — a `search_graph`/read pass the build session runs first.
⭐ **On "yes" ⇒ C** *(a `RecomputeSyncIdentity()` at load; the rail is untouched)*. **On "no" ⇒ A** *(persist +
reclassify the rail, documented)*.

⛔ **This unblocks BP-399's tail:** with the identity durable, **S4** *(promote `details.parametersync`)* is no
longer promoting an inert panel, and **S5** *(retire `InspectorWindow`)* — blocked only on S4 — lands with it.
📌 **Owner when resolved:** the UI / BTree-editor lane *(this is `Hrot.BTree.Editor` + `…AiEditor.Persistence`)*;
tracked against **BP-342 gap ①**.
