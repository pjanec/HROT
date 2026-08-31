<!--STATUS
state: LIVE
updated: 2026-08-30
current-answer: §4 — RESOLVED. The user and the NotebookLM architect converged, and every load-bearing
  claim was verified against source (§2). Q65-A as originally written was WRONG and is retracted in §3;
  it would have introduced the CGF bottleneck the user explicitly rejected. The real answer is smaller:
  NO contract change is needed. isDefaultProcessor is a broadcast tiebreaker, not an authority gate, and
  the code's own comment says so. What remains is pure COMPOSITION, which is the pack's job.
stale-below: nothing. §3 keeps the retracted wording only as the record of what was wrong.
-->
# Architect Question 65 — is entity genesis UNIFORM across ECS nodes? — ✅ **RESOLVED: yes, and no contract change is needed**

> 🔒 **User, `2026-08-30`:** *"so is the unification planned in a way that all ECS equipped node are able
> to create entities and all are able to receive ghost entities and all are using all TKB translator
> lists in the same way (gated just by ECS component registration on node)? i.e. will that be really
> unified cross hosts?"*
>
> 🔒 **User, after the architect round:** *"we are still in the mental model 'only default processor can
> execute the create entity request'. … every ECS equipped subsystem technically has the capability to
> initiate the entity lifecycle protocol and complete it. Am i right? I do not want to end up in a system
> where everything needs to go via CGF. I need a distributed system where each node can create
> entities."*

⭐⭐⭐ **You are right, and so is the architect. And the decisive evidence is in the codebase, four lines
that I read past twice.**

## 1. ⭐ THE ANSWER IN ONE PARAGRAPH

**`isDefaultProcessor` is a BROADCAST TIEBREAKER, not an authority gate.** Any ECS node already processes
a `CreateEntityRequest` **targeted at itself**, regardless of that flag — so decentralised, peer-to-peer
entity genesis is **already the architecture**, not a change to it. ⇒ 🔒 **Q65 does not need a
genesis-contract change.** What is missing is purely **composition**: the nodes that should be able to
create entities do not register the two systems that would let them. That is exactly what
[`DESIGN_Entity_Creation_Unification.md`](../DESIGN_Entity_Creation_Unification.md)'s pack is for.

## 1.1 INVENTORY — the queries actually run

```
grep -rln "class CreateEntityRequestSystem" (non-test)   → 1  — 🔴 in Hrot.CGF/Systems/, a HOST assembly
grep -rn  "new CreateEntityRequestSystem"   (non-test)   → 3  — CGF · Editor · Stride editor ONLY
grep -rl  "GhostCreationSystem"  per host   (non-test)   → SimHost·IG·CGF·Editor·Stride = 5, Replay = 0
grep -rl  "GhostPromotionSystem" per host   (non-test)   → 🔴 SimHost·IG ONLY
grep -rl  "SpawnEntityCommandEgressTranslator"           → 🔴 IG ONLY
grep -rln "DdsIdAllocator|IdAllocatorServer" (non-test)  → 2 services in Fdp.Network.Cyclone/Services
EntityMaster generated struct fields                     → EntityId · TkbType · DisType · Flags (no owner)
IgRoleComponentRegistry + HrotSharedComponentRegistry    → IG registers 6 components Base() would fill
```

⚠⚠ **The codebase-memory graph was NOT used for this inventory** — its MCP server timed out repeatedly
this session. ⭐ The CLI *does* work *(`/opt/codebase-memory-mcp/codebase-memory-mcp cli <tool> '<json>'`)*
and was used earlier for the translator enumeration, which corroborated the production set. ⛔ **Stated
explicitly per `CLAUDE.md`: an inventory taken with grep alone is not proof of completeness**, and the
exhaustive claims here — *"only 3 hosts construct the request system"*, *"promotion on 2 hosts only"* —
should be re-checked with `search_graph` when it reconnects.

## 2. 📐 VERIFICATION — every load-bearing claim, checked against source

⚠ **The architect's answer was relayed through the user, so it is treated as a claim to verify, not as
authority.** All five checked; all five hold.

| # | claim | verdict |
|---|---|---|
| **①** | the routing guard lets a self-targeted request through regardless of `isDefaultProcessor` | ✅ **VERBATIM CORRECT** — `Hrot.CGF/Systems/CreateEntityRequestSystem.cs:151-156`, and ⭐⭐ **the comment directly above it states the interpretation**: *"If the request specifies an explicit target node, **only that node** processes it. If the target is 0 (broadcast / 'any default'), only the designated default processor intercepts it — all other nodes drop the packet silently to prevent duplicate ID allocation"* |
| **②** | `EntityMaster` carries no owner field | ✅ **CORRECT** — the generated struct is `EntityId`, `TkbType`, `DisType`, `Flags`. Existence is defined by the sample being ALIVE, by whoever wrote it |
| **③** | ID allocation is a distributed DDS service | ✅ **CORRECT** — `Fdp.Network.Cyclone/Services/DdsIdAllocator.cs` + `DdsIdAllocatorServer.cs` |
| **④** | IG's tools emit an order that a translator converts to a DDS request | ✅ **CORRECT** — `SpawnEntityCommandEgressTranslator` converts bus `SpawnEntityCommand` → **DDS `CreateEntityRequest`**. ⇒ 📌 **IG's flow today is order → request → (remote) order** — a round trip that exists only because IG has no local materialiser |
| **⑤** | the restriction lives in `IgBootstrapperHelpers.cs` | ✅ **the file exists** and carries the *"replaces SpawningModule so IG does not duplicate entities"* wiring |

### 2.1 🔴 What this makes of MY earlier reasoning

⛔⛔ **The `SpawnEntityCommand` "conflates intent and order" framing was half right and led somewhere
wrong.** The conflation is real — on IG the same event is an intent, elsewhere an order. ⛔ **But I
concluded the fix was to route orders through "the authority"**, which would have **created** the
centralisation the user rejects. 📐 The guard shows the request tier is *already* peer-to-peer; the
conflation is a **symptom of IG lacking a materialiser**, not the cause of a protocol limitation.

⚠ **And I had the file open.** I read `CreateEntityRequestSystem`'s constructor and `isDefaultProcessor`
field earlier the same day, and did not read the guard 40 lines below. 📌 Same shape as the other misses
this session: a confident architectural claim from a partial read.

## 3. ⛔ RETRACTED — the original Q65-A, kept as the record

> ⛔ *"Q65-A — RECOMMENDED YES: every originator publishes `CreateEntityRequest`; **only** the authority's
> `CreateEntityRequestSystem` allocates and issues `SpawnEntityCommand`."*

🔴 **Wrong on the word "only".** Every node's own request system already allocates and issues for
self-targeted requests. ⇒ 🔒 **the corrected form is Q65-A′ below**, and it is a composition change, not a
contract change.

## 4. ✅ THE RESOLVED ANSWERS

### Q65-A′ — every ECS node composes the FULL genesis pipeline · ⭐ **ACCEPTED**

⭐ **What changes:** each node registers the identical set —

```csharp
new CreateEntityRequestSystem(…, isDefaultProcessor: isBroadcastArbiter)   // ⭐ the ONLY differing value
new NetworkSpawningSystem(…, translators: TkbTranslatorSet.Base()+extras)
new GhostCreationSystem(entityMap)
new GhostPromotionSystem(entityMap, tkbDb, sameTranslatorInstance)
```

⭐ **And originators target themselves.** A tool creating an entity the node intends to own publishes a
`CreateEntityRequest` with `OwnerAppInstanceId = localNodeId`; the local request system allocates via the
DDS allocator, issues the order locally, and `EntityMaster` announces it. ⛔ **ExCon keeps `Owner == 0`**,
and exactly one node carries `isBroadcastArbiter: true` for those.

| ✅ | |
|---|---|
| ⭐⭐ **no protocol change, no new event, no wire change** | the guard, the allocator and `EntityMaster` already support it |
| ⭐⭐ **CGF stops being a bottleneck** | it arbitrates *unowned broadcasts* only — which is all it was ever for |
| ⭐ **the duplicate-entity hazard dissolves** | IG stops emitting orders; a self-targeted request is materialised **once**, by the node that raised it |
| ⭐ **`SpawnEntityCommandEgressTranslator` becomes unnecessary for self-owned entities** | ⚠ keep it while anything still emits bus-level orders |

### Q65-B — every ghost-receiving node gets `GhostPromotionSystem` · ⭐ **ACCEPTED**

📐 Today `GhostCreationSystem` is on 5 of 6 hosts but **`GhostPromotionSystem` on SimHost and IG only** ⇒
CGF, the Editor and the Stride node create ghosts whose TKB descriptors are never projected. ⭐ Safe by
`tkb-1` §6.5b's gate ②: a node that does not register a component skips it silently.

### Q65-C — IG's translator width · ⚠ **DEFERRED to `CE-141`**, unchanged

### Q65-D — the half-pack fallback · ⛔ **REJECTED**

⇒ ⭐⭐ **`DESIGN_Entity_Creation_Unification.md` §2.3's role-selected HALVES are SUPERSEDED.** There is one
pipeline; `Role` selects **only** `isBroadcastArbiter` and whatever role-specific systems exist for other
reasons.

## 5. ⚠ THE REAL OBSTACLES — composition, not contract

| # | obstacle | note |
|---|---|---|
| **①** | 🔴 **`CreateEntityRequestSystem` lives in `Hrot.CGF/Systems/`** — a host assembly. Only CGF, the Editor and the Stride editor construct it | ⇒ ⭐ **it must move to a shared assembly** *(`Fdp.Toolkits` or `Hrot.Common`)* before "every node registers it" is even expressible. **This is the first task, and it is a MOVE with no behaviour change** |
| **②** | IG's tools publish bus-level `SpawnEntityCommand` | ⇒ retarget to a self-targeted `CreateEntityRequest`; same for `SimHostScenarioManager.SpawnVehicle` |
| **③** | ⚠ **the ELM ACK handshake on a node with no peers** | the Editor runs offline with a `SequentialIdAllocator` and no peers to ACK. ⭐ It already works *(`isDefaultProcessor: true`, local bus)* — ⛔ but **verify the handshake does not stall** when a *networked* node self-targets and a peer is absent |
| **④** | ⚠ **`DeferredTakeOwnership` is wired only in CGF's `CognitiveTranslatorPack`** | ⇒ split-authority spawns *(IG creates a vehicle whose kinematics SimHost must own)* still need CGF's routing. ⭐ **Pure single-owner entities — tactical graphics, markers, areas — need none of it**, which is exactly the user's case |

## 6. ⭐ SEQUENCING

| # | | why here |
|---|---|---|
| **1** | pack **step 4** *(catalogue contents)* | smallest, independent |
| **2** | ⭐ **move `CreateEntityRequestSystem` to a shared assembly** | obstacle ① — a pure move, and nothing below is expressible without it |
| **3** | pack **step 3** *(`EntityCreationPack`)*, now **one uniform pipeline** | §2.3's halves are gone |
| **4** | **Q65-A′** — retarget originators to self-targeted requests, starting with IG's tactical graphics *(obstacle ④ says they need no split authority)* | the user's actual use case, and the safest instance of it |
| **5** | **Q65-B** — uniform `GhostPromotionSystem` | |
| **6** | **`CE-141`** — IG's translator width, with a live probe | |

⚠⚠ **Everything above is source-measured only.** `hrot-ai-debug` has been disconnected all session, so
**no claim here has been checked against a running cluster** — and obstacle ③ in particular is exactly the
kind that only shows up live.
