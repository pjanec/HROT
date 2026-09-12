<!--STATUS
state: LIVE
build-state: BUILT (option C, 2026-08-22 — BP-440..442). Option D is DESIGNED and GATED on Q50.
  The C-vs-A measurement is ANSWERED => C; ONE sub-question left for the
  user: what happens when the subtree asset is MISSING at load. ALSO AMENDED 2026-08-22 by the UI lane:
  this question covers BP-342 gap (1) ONLY — gap (2) (the master blackboard does not declare the
  auto-allocated slice) still blocks S4, and needs its own question. Option D added.)
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
| ⛔ **`_syncNodeMeta`** `Dictionary<Guid,(SubtreeName, SubDtoTypeName, SubDtoTypeNs)>` | `BehaviorTreeAsset:243` | ⛔ **session-local — LOST on reload.** Its **only writer** is `RecordSubtreeNodeMeta` *(→ `InspectorWindow:194`, a UI draw)* |
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

## ✅ RESOLVED — **the decisive measurement is ANSWERED: C is feasible** *(UI lane, `2026-08-22`)*

⭐⭐⭐ **The question was:** *at load, from a subtree node, can we resolve the called subtree and read its
param-DTO type name + namespace?* ⛔ It did **not** need to wait for the build session — 📐 **measured now**:

| what `RecordSubtreeNodeMeta` needs | where it comes from | persisted? |
|---|---|---|
| **which subtree this node calls** | `BehaviorTreeAssetDto:233` `SubtreeAssetId` → read back via `BehaviorTreeAsset.GetSubtreeNodeInfo` *(`:645`)* | ✅ **YES** |
| **`SubtreeName`** | `SanitizeIdentifier(subAsset.Name)` | ✅ the sub-asset's own name |
| **`SubDtoTypeName` / `SubDtoTypeNs`** | `ShortTypeName`/`NsOf` over `subAsset.BlackboardTypeName` — `BehaviorTreeAssetDto:342`, a **plain string** on the loaded asset *(`BehaviorTreeAsset:266`)* | ✅ **YES** |

⇒ ⭐⭐ **All three values are derivable from the RESOLVED SUB-ASSET, with no compilation and no type
loading** — ⛔ which was the feared blocker *(*"e.g. it requires compilation"*)* and is **not** one.
⇒ **C stands; A is not needed, and the exclusion rail stays correct.**

### ⚠ The one real constraint the options table did not name — **ORDERING, not feasibility**

⛔ The recompute needs the **sub-asset LOADED**, so it cannot live inside the calling asset's own
deserialisation *(the callee may not exist yet)*. ⇒ ⭐ it belongs **after the catalog is populated** —
the same resolver shape the panel already uses *(`Func<Guid, IBlackboardManagedAsset?>`)*, run once per
subtree node on catalog-ready rather than per UI draw.

⭐⭐ **And that turns `BP-342` gap ① into an ordinary silent-default instance**: the value is available,
the caller holds a resolver, and nothing passes it at load — 📌 the `2026-08-16` rule, whose control is
**a rail asserted on the CONSTRUCTED object**, i.e. *"after a reload with no UI shown,
`GetApproachBSyncGroups()` is non-empty."*

⚠ **What is still the USER's call, and it is the only thing left open:** ⛔ **when a subtree asset is
MISSING at load** *(deleted, renamed, not in the catalog)* — does the node keep its bindings and emit
nothing *(silent)*, or does it raise a diagnostic row? ⭐ **Recommended: a diagnostic row** — 📌 `AIE053`
just established that the Diagnostics window is where an unresolvable authoring reference belongs, and a
silent skip is precisely the failure mode this whole question exists to end.

## ✅ BUILT — **option C shipped `2026-08-22`** *(`BP-440`–`BP-442`)*

🔒 **User, `2026-08-22`:** *"I agree with the recommended solution."*

### ⭐ Where the load path comes from — **the question asked, answered concretely**

| arm | the resolver | status |
|---|---|---|
| ⭐⭐ **editor (C)** | `catalog.FindByAssetId(id)` — **already wired**, at `PerspectiveWorkspaceRegistrar:289`, for the Inspector's sync panel. ⇒ ⛔ nothing new to plumb | ✅ **BUILT** |
| ⭐⭐ **generator (D)** | the **`*.btree.json` `AdditionalTexts` the generator ALREADY receives** *(`BTreeJsonGenerator:30`)*, second-projected exactly as `GeneratedBlueprintSchemaCatalog` does for `*.bp.json`. ⇒ ⭐ **cheaper than this document implied** — not new plumbing, a second read of texts already in hand | ⛔ **GATED on `Q50`** — see below |

### ⭐⭐ What shipped

| | |
|---|---|
| **`SubtreeSyncIdentity`** *(new, `Hrot.AiEditor.Persistence/Emit/`)* | ⭐⭐⭐ **THE derivation, once.** 📐 **Measured: there is no netstandard2.0/net8.0 wall on this path** — that assembly is `netstandard2.0` and is referenced by the **generator**, by `Hrot.Editor.AiShared` **and** by `Hrot.BTree.Editor` ⇒ ruling 9's *"one implementation"* is achievable, and `BATCH-03-REPORT.md:100`'s duplication hazard does not apply. ⚠ **`InspectorWindow`'s three private helpers are DELETED** — the panel and the reload now derive identically **by construction** |
| **`BehaviorTreeAsset.RecomputeSubtreeSyncIdentity(resolve)`** | walks the nodes that HAVE bindings, resolves each callee, re-records. ⭐ Idempotent; ⚠ a **missing** callee is left ALONE, never cleared *(a half-loaded catalog must not destroy a designer's session)* |
| ⭐⭐⭐ **the PULL** | `BTreeOrchestratorEmitter.Emit(asset, resolveSubAsset)` — the resolver is **REQUIRED** and the recompute happens **inside** `Emit`. 📌 `R-126`: *"no path can forget to raise what is never raised."* ⛔ An **optional** resolver would have rebuilt the exact failure mode this fixes — an identity only some callers bother to supply |
| **rails** | 11 new, incl. one that **pins the defect** *(a reloaded asset yields no groups until recomputed)* so the fix cannot become vacuous. Revert-probed: removing the recompute reddens the emit-path rail **and nothing else** |

⚠ **Stated limit, measured:** the editor emit path *(`BTreeOrchestratorEmitter`)* currently has **no
production caller** — `WriteOrchestratorFile`'s site is the Category-1 hand-authored path, deliberately
unwired *(`BP-340`)*. ⇒ ⭐ C makes the identity **correct wherever it is read**; it does not by itself
make anything ship. **That is D's job, and D is gated.**

### ⛔⛔ Why option D is NOT wired yet — **measured, not cautious**

📐 `BTreeOrchestratorEmitCore:165` emits `ref var subDto = ref master.{sliceField};` where `sliceField`
is `{SubtreeName}_{DtoTypeName}` — ⛔ **a field no blackboard emitter declares** *(gap ②)*. ⇒ ⚠ **wiring
D would make the generator emit non-compiling code the moment a designer creates a sync binding** —
📌 exactly `BP-306`'s shape. ⭐ **D lands immediately after [`Q50`](Architect_Question_50_The_Master_Blackboard_Declares_The_Subtree_Slice.md).**

---

## ⛔⛔ AMENDMENT — **this question covers `BP-342` gap ① ONLY, and ① alone does NOT unblock `S4`**

> ⚠ **Added by the UI lane, `2026-08-22`, on merging the coordinator branch.** The question as written
> closed with *"this unblocks `BP-399`'s tail."* 📐 **Measured — that is overstated on TWO counts**, and
> `BP-342`'s own text says the first of them in as many words.

### ① `BP-342` has a SECOND gap, and it is not an identity problem

📄 **`BP-342` ②, verbatim:** *"the destination **FIELD** does not exist at all."* The Approach-B body
writes `ref master.{SubtreeName}_{SubtreeDtoTypeName}`. That slice comes from
`BehaviorTreeAsset.GetAutoAllocatedVariables()` *(`:768`)*, whose **only** consumer is
`BlackboardAuthoringWindow:529`, which merely **displays it greyed** as *"(size unknown until build)"*.
⇒ ⛔ it never enters `_blackboardVariables`, never reaches `Blackboard.Variables`, and **no blackboard
emitter declares it** ⇒ **the Approach-B orchestrator references a master field the generated blackboard
struct does not have.**

⭐⭐⭐ **`BP-342` states the consequence directly:** *"Widening the DTO would NOT be sufficient while ②
stands, which is why it was not attempted."* ⇒ ⛔ **the same is true of recomputing it (option C).**
📌 The open question ② carries: **does the master blackboard DECLARE the auto-allocated sub-tree slice,
and if so who sizes it?** — ⚠ a **blackboard-emission** decision, not an identity one, and
`GetAutoAllocatedVariables` carries its own `DEBT` *("real type resolution requires catalog
integration")*.

### ② Option C fixes the EDITOR arm — ⛔ and per `Q45` the editor arm is not the one that ships

📐 `Q45` ruled that **neither editor path owns the sidecar — the SOURCE GENERATOR does**. And the
generator passes `Array.Empty<OrchestratorSyncGroup>()` *(`BTreeJsonGenerator:286`)*, saying at the call
site why: ⛔ **a generator cannot load assets**, so an in-editor load-time resolver — which is exactly
what C is — **cannot run there**.

⇒ ⭐⭐ **C makes the editor's groups survive a reload, and changes nothing about the generated
`.Orchestrators.g.cs`.** ⚠ That is still worth having *(the editor's `WriteOrchestratorFile` path is
retained per spec)*, ⛔ but it is not *"the identity is durable, therefore the panel is live."*

### ⭐ THE MISSING OPTION — **D: a `*.btree.json` catalog for the generator**

⛔ **Not in the options table, and `BP-342` had already identified it** — with a working precedent **in
`BTreeJsonGenerator` itself**: *"Option A"* collects sibling `*.bp.json` `AdditionalTexts` into
`GeneratedBlueprintSchemaCatalog`, precisely because a sibling generator's shape is not in this DTO.

| **D** | ⭐⭐ **a `*.btree.json` `AdditionalTexts` catalog keyed `AssetId → Blackboard.TypeName`** | mirrors the shipped `*.bp.json` precedent | ⭐ **works in the GENERATOR**, where C cannot; **no schema change**; the exclusion rail stays correct | ⛔ **fixes ① only** — ② still stands |

⇒ ⭐⭐⭐ **The honest shape is C **and** D, not C or A:** ⭐ **D** for the generator *(the arm that ships)*,
⭐ **C** for the editor *(the retained hand-authored path)* — ⚠ **and they share one derivation**
*(`AssetId → Name + BlackboardTypeName → Sanitize/ShortName/Ns`)*, so 📌 **ruling 9 says write it once**
and give it two front ends, exactly as `BTreeOrchestratorEmitCore` already is.

### ⇒ ⭐ What this question actually unblocks

| | |
|---|---|
| ✅ **`BP-342` gap ①** | with **C + D**, and the C-vs-A measurement is settled *(above)* |
| ⛔ **`S4` / `BP-399`'s tail** | **NOT unblocked by this question alone.** `BP-342` **②** is the remaining blocker and it is a **separate architect question** — *"does the master blackboard declare the auto-allocated slice, and who sizes it?"* ⚠ **Worth its own `Q50`** |
| ⛔ **`S5`** | blocked on `S4`, which is blocked on ② *(`BP-439`)* |

📌 **Owner when resolved:** the UI / BTree-editor lane *(`Hrot.BTree.Editor` + `…AiEditor.Persistence`)*;
tracked against **`BP-342`** — ⚠ **gap ① here, gap ② needs its own question.**
