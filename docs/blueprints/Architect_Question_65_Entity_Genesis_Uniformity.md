<!--STATUS
state: LIVE
updated: 2026-08-31
current-answer: §4 — RESOLVED. The user and the NotebookLM architect converged, and every load-bearing
  claim was verified against source (§2). Q65-A as originally written was WRONG and is retracted in §3;
  it would have introduced the CGF bottleneck the user explicitly rejected. The real answer is smaller:
  NO contract change is needed. isDefaultProcessor is a broadcast tiebreaker, not an authority gate, and
  the code's own comment says so. What remains is pure COMPOSITION, which is the pack's job.
stale-below: nothing. §3 keeps the retracted wording only as the record of what was wrong, and Q65-B in
  §4 keeps its two superseded mechanisms in the same way. ⛔ Neither table's left column may be quoted as
  current.
known-rot: §4's Q65-B was WRONG TWICE before 2026-08-31 (a host list, then `.WithReplication()`); the
  measured gate is the NodeRole inside NedReplicationModule.RegisterSystems. §1.1's grep-only inventory
  was replaced the same day by a codebase-memory GRAPH pass, which corrected the
  SpawnEntityCommandEgressTranslator row.
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

## 1.1 INVENTORY — ⭐⭐ re-run through the codebase-memory **GRAPH**, `2026-08-31`

⚠ **The first version of this section was grep-only and said so.** The MCP server is still down, but
⭐ **the CLI works** — `/opt/codebase-memory-mcp/codebase-memory-mcp cli <tool> '<json>'` — and the
enumeration below is the graph half, run before anything in §4 was corrected.

| `search_graph` query *(label `Class`)* | total | what it settles |
|---|---|---|
| `CreateEntityRequestSystem` | **3** | ⭐⭐ **ONE production class**, `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs:40-464` *(in 24 / out 2)*; the other two are test classes ⇒ ✅ **obstacle ①'s premise HOLDS** — the request system really does live in a host assembly, and there is no second one hiding |
| `GhostCreationSystem` | **1** | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostCreationSystem.cs:9-60` — already shared |
| `GhostPromotionSystem` | **1** | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs:25-146` *(in 8)* — already shared |
| `SpawnEntityCommandEgressTranslator` | **2** | 🔴 **CORRECTION to the grep row below** — the class is **NOT in IG**. It lives in `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/` *(in 7 / out 1)*, i.e. **already in a shared assembly**, and its factory method `INetworkFactory.CreateIgEgressTranslators` is implemented by **all three** factories *(`Ned`, `Bdc`, `Offline`)*. ⭐ Only its **registration** is IG-only — `IgNodeBootstrapper.cs:351`, the sole caller ⇒ **nothing has to move for Q65-A′; the seam is already there** |
| `NetworkSpawningSystem` | 1 prod | `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/` |

### ⛔⛔ Where the graph is BLIND — stated plainly, because it changes what the counts below mean

📐 `query_graph "MATCH (a:Method)-[:USAGE]->(b:Class) WHERE b.name = '…'"` returned **only test callers**
for `CreateEntityRequestSystem` *(4 rows, all in `Hrot.SimHost.Tests`)* and **ZERO** rows for both ghost
systems — while grep finds their production registrars immediately. ⚠ **C# `new`-site resolution is
under-reported**, exactly as `CLAUDE.md`'s measured caveat warns.

⇒ ⭐⭐⭐ **The division of labour, and neither half is optional:** the **graph** settles the
**INVENTORY** *(how many exist, where they live, how connected)* — which is what corrected the
`SpawnEntityCommandEgressTranslator` row and what confirms there is exactly one of each system.
The **grep** settles the **CONSTRUCTION / REGISTRATION SITES**. The rows below are that grep half:

```
grep -rn  "new CreateEntityRequestSystem"   (non-test)   → 3  — CGF · Editor · Stride editor ONLY
grep -rn  "new GhostPromotionSystem"        (non-test)   → 3  — 🔴 NedReplicationModule :308 + :356, ReplicationLogicModule :43
grep -rln "new ReplicationLogicModule"      (non-test)   → 2  — ⭐ BOTH in FDP/Examples ⇒ examples-only
grep -rn  "SpawnEntityCommandEgressTranslator" reg sites → 1  — NedNetworkFactory.CreateIgEgressTranslators
grep -rn  "CreateIgEgressTranslators" callers            → 1  — IgNodeBootstrapper.cs:351
grep -rn  "WithReplication"                 (non-test)   → 4  — IG · Stride · SimHost · EyesAndMuscle; ⚠ NOT CGF — but see §4's Q65-B: CGF builds the module the OTHER way
EntityMaster generated struct fields                     → EntityId · TkbType · DisType · Flags (no owner)
grep -rln "DdsIdAllocator|IdAllocatorServer" (non-test)  → 2 services in Fdp.Network.Cyclone/Services
IgRoleComponentRegistry + HrotSharedComponentRegistry    → IG registers 6 components Base() would fill
```

⭐⭐ **The graph pass paid for itself twice**: it corrected the egress-translator's home, and chasing
*"who registers `GhostPromotionSystem`"* through it is what exposed that **Q65-B's mechanism was wrong** —
see §4.

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

### Q65-B — uniform ghost promotion · ⭐ **ACCEPTED — but the MECHANISM was wrong TWICE, corrected `2026-08-31`**

⛔⛔ **Both earlier drafts of this sub-answer were host-shaped reads, and a host list is not a mechanism.**
Recording both, because it is the same error shape this document exists to catch.

| draft | what it said | 🔴 why wrong |
|---|---|---|
| ① | *"`GhostPromotionSystem` on SimHost and IG only ⇒ add it to every host's composition"* | a **host list** describes the symptom; it names no gate, so it cannot say what to change |
| ② | *"promotion follows `NedReplicationModule`, so the real gap is CGF's missing `.WithReplication()`"* | 🔴 **CGF DOES build a `NedReplicationModule`** — via `nodeFactory.CreateReplicationModule()` *(`CgfSubsystem.cs:600-610`)*, not the builder chain. The `.WithReplication()` grep was **true and irrelevant** |

#### 📐 THE MEASURED MECHANISM

`NedReplicationModule.RegisterSystems` is the **only production registrar** — `ReplicationLogicModule.cs:43`
is the second one and is **examples-only** *(two constructors, both under `FDP/Examples/`)*:

| site | gate |
|---|---|
| `NedReplicationModule.cs:308` | `pureIgRole` = `ImageGenerator ∧ ¬MuscleGround ∧ ¬Brain` **∧** `_tkbDb != null` **∧** `_lifecycleModule != null` |
| `NedReplicationModule.cs:356` | `_roleHasMuscle` = `role.HasFlag(MuscleGround)` **∧** the same two null guards |

⇒ 🔒 **The gate is the ROLE — not the host, not the builder chain.** CGF runs `NodeRole.Brain`
*(`CgfSubsystem.cs:532` and `:600`)*, which is **neither** `pureIgRole` **nor** `_roleHasMuscle` ⇒ **pure-Brain
gets no promotion, by construction**; and the Editor / Stride editor get none because they build no
`NedReplicationModule` at all.

⭐⭐ **And as the system stands today that is DEFENSIBLE, not a bug.** The comment at `:350` says why:
Muscle promotes *"CGF-spawned entities (WorldPos delegated to Muscle)"*. ⭐ Pure-Brain **is** the spawning
authority — it never received a ghost carrying TKB descriptors it had not itself created.

⇒ ⭐⭐⭐ **Q65-A′ is precisely what breaks that assumption.** Once IG self-targets a `CreateEntityRequest`
and owns the result, CGF becomes a **receiver** of entities it did not spawn ⇒ the missing pure-Brain
promotion stops being a design property and becomes a real gap. ⛔ **But only then, and only for Brain.**

| ✅ the corrected item | |
|---|---|
| ⭐⭐ **what to change** | in `NedReplicationModule.RegisterSystems`, collapse the two role-gated sites into **one** registration valid for **any** role, once `_tkbDb` and `_lifecycleModule` are present |
| ⚠⚠ **the null guards are the real per-host lever** — and they are SILENT | a role that supplies no TKB database skips promotion with no diagnostic. ⛔ **Do not convert them to a throw** without first measuring which hosts pass null — that is the `CLAUDE.md` silent-default pattern, and the caller-holds-it test has not been run here |
| ⭐ **safe by `tkb-1` §6.5b gate ②** | a node that does not register a component skips it silently ⇒ widening the role gate **cannot** write a component a host never registered |
| ⛔ **sequence it AFTER Q65-A′** | before A′, pure-Brain promotion is dead code; after it, load-bearing. ⚠ Doing it first buys nothing and changes CGF's system set for no measurable reason |

### Q65-C — IG's translator width · ⚠ **DEFERRED to `CE-141`**, unchanged

### Q65-D — the half-pack fallback · ⛔ **REJECTED**

⇒ ⭐⭐ **`DESIGN_Entity_Creation_Unification.md` §2.3's role-selected HALVES are SUPERSEDED.** There is one
pipeline; `Role` selects **only** `isBroadcastArbiter` and whatever role-specific systems exist for other
reasons.

## 5. ⚠ THE REAL OBSTACLES — composition, not contract

| # | obstacle | note |
|---|---|---|
| **①** | 🔴 **`CreateEntityRequestSystem` lives in `Hrot.CGF/Systems/`** — a host assembly. Only CGF, the Editor and the Stride editor construct it | ⇒ ⭐ **it must move to a shared assembly** *(`Fdp.Toolkits` or `Hrot.Common`)* before "every node registers it" is even expressible. **This is the first task, and it is a MOVE with no behaviour change** |
| **②** | IG's tools publish bus-level `SpawnEntityCommand` | ⇒ retarget to a self-targeted `CreateEntityRequest`; same for `SimHostScenarioManager.SpawnVehicle`. ✅ **§1.1's graph pass shrank this one**: `SpawnEntityCommandEgressTranslator` is **already** in the shared `Hrot.Network.NED` assembly behind an `INetworkFactory` method all three factories implement ⇒ **no code moves, only the originator's target changes** |
| **②b** | 🔴 **`GhostPromotionSystem` is ROLE-gated inside `NedReplicationModule`**, and pure-Brain *(CGF)* is excluded by construction | ⇒ ⭐ see Q65-B. ⚠ **Not a host-composition oversight** — today it is correct, because pure-Brain spawns rather than receives. It becomes a gap **only once Q65-A′ lands** |
| **③** | ⚠ **the ELM ACK handshake on a node with no peers** | the Editor runs offline with a `SequentialIdAllocator` and no peers to ACK. ⭐ It already works *(`isDefaultProcessor: true`, local bus)* — ⛔ but **verify the handshake does not stall** when a *networked* node self-targets and a peer is absent |
| **④** | ⚠ **`DeferredTakeOwnership` is wired only in CGF's `CognitiveTranslatorPack`** | ⇒ split-authority spawns *(IG creates a vehicle whose kinematics SimHost must own)* still need CGF's routing. ⭐ **Pure single-owner entities — tactical graphics, markers, areas — need none of it**, which is exactly the user's case |

## 6. ⭐ SEQUENCING

| # | | why here |
|---|---|---|
| **1** | pack **step 4** *(catalogue contents)* | smallest, independent |
| **2** | ⭐ **move `CreateEntityRequestSystem` to a shared assembly** | obstacle ① — a pure move, and nothing below is expressible without it |
| **3** | pack **step 3** *(`EntityCreationPack`)*, now **one uniform pipeline** | §2.3's halves are gone |
| **4** | **Q65-A′** — retarget originators to self-targeted requests, starting with IG's tactical graphics *(obstacle ④ says they need no split authority)* | the user's actual use case, and the safest instance of it |
| **5** | **Q65-B** — collapse the two role-gated `GhostPromotionSystem` registrations in `NedReplicationModule` into one | ⛔ **strictly after step 4.** Before Q65-A′, pure-Brain promotion is dead code — ⭐ and the gate is the ROLE, not the host, so this is a **two-line** change in one file, not a per-host sweep |
| **6** | **`CE-141`** — IG's translator width, with a live probe | |

⚠⚠ **Everything above is source- and graph-measured only.** `hrot-ai-debug` has been disconnected all
session, so **no claim here has been checked against a running cluster** — and obstacle ③ in particular is
exactly the kind that only shows up live.

⭐ **What the `2026-08-31` graph pass changed, so a reader can see whether it was worth it:** it corrected
one inventory row *(the egress translator is already shared)*, confirmed three *(one class each of the
request system and the two ghost systems)*, and — by making me chase *who registers promotion* through the
graph instead of assuming — **caught that Q65-B's mechanism was wrong for the second time.** ⛔ It also
measured its own blindness: the `USAGE` edges into these classes reach **only test callers**, so the
construction-site counts remain grep's.
