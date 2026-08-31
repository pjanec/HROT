<!--STATUS
state: LIVE
updated: 2026-08-30
current-answer: §3 carries a recommended answer per sub-question. NOTHING IS APPROVED YET — this is the
  agenda for a working session with the user, per CLAUDE.md ("no architect will answer; you analyze and
  suggest, i approve").
known-conflict: DESIGN_Entity_Creation_Unification.md §2.3 currently designs Role-selected HALVES. If
  Q65-A is approved, §2.3's half-split is SUPERSEDED and the pack becomes uniform. Read this first.
-->
# Architect Question 65 — is entity genesis UNIFORM across ECS nodes, or authority-shaped?

> 🔒 **The user's question, `2026-08-30`, verbatim:** *"so is the unification planned in a way that all
> ECS equipped node are able to create entities and all are able to receive ghost entities and all are
> using all TKB translator lists in the same way (gated just by ECS component registration on node)?
> i.e. will that be really unified cross hosts?"*

⛔⛔ **Honest answer to that question first: NO, not as
[`DESIGN_Entity_Creation_Unification.md`](../DESIGN_Entity_Creation_Unification.md) is currently
written.** §2.3 has `Role` select which *half* of the pack a host gets — which **relocates** the
per-host divergence into a nicer place rather than removing it. ⭐ This document exists to decide whether
to go the rest of the way.

## 1. INVENTORY — measured `2026-08-30`

```
grep -rn "new NetworkSpawningSystem" (non-test)          → 6 sites (5 now via TkbTranslatorSet)
grep -rl "GhostCreationSystem"  per host (non-test)      → SimHost·IG·CGF·Editor·Stride = 5, Replay = 0
grep -rl "GhostPromotionSystem" per host (non-test)      → 🔴 SimHost·IG ONLY
grep -rl "SpawnEntityCommandEgressTranslator" per host   → 🔴 IG ONLY
IgRoleComponentRegistry + HrotSharedComponentRegistry    → IG registers 6 components Base() would fill
```

### 1.1 The three criteria, scored against today and against the current design

| the user's criterion | today | design **as written** | genuinely achievable? |
|---|---|---|---|
| **all ECS nodes can create entities** | ✅ **already true** — IG originates via `MapCommandController.OnEntityCreatedByTool` → `PublishManaged(SpawnEntityCommand)`; Editor, CGF, SimHost all have request sources | ✅ | ✅ **already there** |
| **all can receive ghosts** | ⚠ **partly** — `GhostCreationSystem` on 5 of 6, but 🔴 **`GhostPromotionSystem` — the one that applies TKB translators to ghosts — is on SimHost and IG ONLY** | ⛔ **unaddressed**; the design never mentions promotion | ✅ — add it to the pack |
| **one translator list, gated only by registration** | ⚠ **nearly** — steps 1+2 put 5 sites on `TkbTranslatorSet.Base()`; 🔴 IG still hand-narrowed to 2 *(`CE-141`)* | ⛔ unchanged | ✅ once `CE-141` settles |

## 2. 🔴 THE ONE THING THAT BLOCKS TRUE UNIFORMITY

📐 **`SpawnEntityCommand` conflates INTENT and ORDER, and its meaning depends on which systems the node
composed.**

| on a node **with** `NetworkSpawningSystem` | on IG, which has none |
|---|---|
| it is an **ORDER**: `ProcessSpawn` allocates an id when `NetworkId == 0` and materialises locally | it is an **INTENT**: `SpawnEntityCommandEgressTranslator` forwards it to the authority, whose ghost replicates back |

📌 **Confirmed by the code's own guard rail** — `IgNodeBootstrapper.RegisterSpawningPipeline`:
*"Ghost destruction — **replaces SpawningModule so IG does not duplicate entities**."*
⇒ 🔒 **handing every node a materialiser today would double-create every entity an originator raises.**
⭐⭐ **That is the real reason the design fell back to per-role halves** — not a considered preference.

⭐ **And the separation the codebase needs already half-exists:** `CreateEntityRequestSystem` is the
request tier *(with `isDefaultProcessor` true on the authority, false elsewhere)*, and it is what
allocates the id and enqueues the order. ⛔ **IG bypasses it**, publishing an order-shaped event directly.

## 3. ⭐⭐⭐ THE QUESTIONS, each with a recommended answer

### Q65-A — do originators publish a REQUEST instead of an ORDER?

⭐⭐ **RECOMMENDED: YES.** Every originator *(IG's placement tool, the Editor's placement, SimHost's
scenario manager, ExCon)* publishes `CreateEntityRequest`; **only** the authority's
`CreateEntityRequestSystem` allocates and issues `SpawnEntityCommand`. ⇒ 🔒 **`SpawnEntityCommand` becomes
unambiguously an ORDER**, and every node can then run the identical materialiser safely, because on a
non-authority node no order is ever locally raised.

| ✅ buys | ⚠ costs |
|---|---|
| ⭐⭐ **the pack becomes uniform** — `isDefaultProcessor` is the ONLY role-dependent value, and that is a runtime authority concern, not a composition difference | ⚠ touches the genesis contract: IG's egress translator and `SimHostScenarioManager.SpawnVehicle` both publish orders today |
| ⭐ removes the duplicate-entity hazard **structurally** rather than by omitting a system | ⚠ a request round-trip where there was a local publish — latency on the Editor's offline path needs checking |
| ⭐ makes §2.3's half-split unnecessary | ⛔ **cluster-wide blast radius** ⇒ this is why it is an architect question and not a design decision |

### Q65-B — does every ghost-receiving node get `GhostPromotionSystem`?

⭐⭐ **RECOMMENDED: YES**, for the five that already have `GhostCreationSystem`. 📐 Today only SimHost and
IG promote, so **CGF, the Editor and the Stride node create ghosts whose TKB descriptors are never
projected** — the same silent gap as `CE-138`, one tier further down. ⚠ **Verify first** whether those
three actually receive replicated entities in practice, or only create ghosts for local bookkeeping.

### Q65-C — is IG's translator list widened to `Base()`?

⚠ **RECOMMENDED: DEFER to `CE-141`, and decide it with a live probe, not here.** IG registers six
components `Base()` would fill; they may be filled by DDS replication instead. ⛔ **Do not bundle this
into the pack** — it changes what an IG ghost looks like on the first frame.

### Q65-D — if Q65-A is rejected, what is the fallback?

⭐ **Keep §2.3's half-split, but make it HONEST**: rename `Role` to something that says it selects
*capabilities*, and add a rail asserting **no node has both a local materialiser and a spawn egress
translator** — the invariant that currently lives only in a comment. ⚠ **This is strictly worse than
Q65-A** — it documents the divergence instead of removing it — but it is cheap and it closes the
duplicate-entity hazard with a gate.

## 4. ⭐ What I recommend overall

⭐⭐ **A → B → (CE-141 separately).** ⛔ **But not before the pack's step 3 ships:** step 3 is a pure
composition refactor with no contract change, and it is what makes A cheap — after it, the request/order
separation is edited in **one** place instead of six.

⇒ 🔒 **Sequence: pack step 4 → pack step 3 → Q65-A → Q65-B → `CE-141`.**

⚠⚠ **And the honest caveat on all of it: none of this can be verified live in this session** —
`hrot-ai-debug` has been disconnected throughout, so every claim here is source-measured only.
