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
updated-again: 2026-08-31 — §0 carries the GOVERNING RULING (no capability removal by design; the
  authoring call site picks the path). New: §2.2 (two refuted architect claims), §5.1 (per-node gap),
  §5.2 (the publish half is already uniform — IG CAN publish EntityMaster), and §6's ORDERING HAZARD
  (step 3 and step 4 are atomic for IG, or it double-spawns). The Q65-A′ answer in §4 is unchanged in
  substance but must now be read with §0: path 1 is NOT deprecated — both paths are legitimate.
known-rot: obstacle 4 and the closing caveat of section 5.2 BOTH used to say the _roleHasBrain gate on
  DeferredTakeOwnership was correct and must not be widened. That is FALSE and is corrected in section
  5.3 (CE-142, 2026-08-31): all three pieces are pure mechanism, the receive side is doubly guarded and
  free to ungate, and the only role-specific thing is the injected BrainMuscleOwnershipStrategy POLICY.
  A reader must not quote either prior wording.
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

## 0. 🔒🔒🔒 THE GOVERNING RULING — **no capability removal by design** *(user, `2026-08-31`)*

> 🔒🔒🔒 **User, verbatim:** *"the shared code for entity creation support should not
> restrict any ECS enabled node from creating own networked entities, which makes the subsystems equal in
> distributed architecture and the shared code more uniform, no exceptions, not removing capabilities by
> design, and only concrete authoring code picks the way it needs."*

> 🔒 **And the two-class framing that produced it:** *"there are entities we want to be created on
> CGF, these are the brain enabled entities, for those the request coming to default processor is the right
> choice, as it makes the CGF to own most of their components. but some entities might be desired to be owned
> by the IG who created them, like some map-local drawings that needs to be shared with other IGs and do not
> need any brain … my desire is to not suppress this second possibility by not instantiating some systems,
> not necessarily to use it for every entity."*

| ⭐ what this settles | |
|---|---|
| ⭐⭐⭐ **BOTH paths are legitimate and both stay** | ⛔ this is **not** "make everything local". Routing to the default processor is **correct** for brain-enabled entities, precisely because it puts ownership on CGF |
| ⭐⭐⭐ **the defect is SUPPRESSION, not centralisation** | 📐 IG / SimHost / Stride node cannot reach path 2 **because systems were omitted from their composition** — ⇒ that omission is the thing to remove |
| ⭐⭐ **the DECIDER is the authoring call site** | ⛔ **not** a policy table, **not** a TKB flag, **not** config. 📐 The mechanism is one field, `OwnerAppInstanceId`, already honoured three ways at `CreateEntityRequestSystem.cs:290-294` |
| ⭐⭐ **checkable consequence** | ⛔ **`EntityCreationPack.Build` gets no flag, and no `Role` value, that omits the request or spawn system** — 📄 [`DESIGN_Entity_Creation_Unification.md`](../DESIGN_Entity_Creation_Unification.md) §3.1 invariant ⑥, §3.4, acceptance ⑨–⑪ |

⇒ ⭐⭐ **This RETIRES the last of the "halves"**, and it retires IG's `SpawningModule` omission — 📌 which
means Q65-D was rejected for a **stronger** reason than "it documents divergence": ⛔ **it removes a
capability by design.**

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

### 2.2 🔴 TWO architect claims that MEASUREMENT REFUTES — **added `2026-08-31`**

⚠ **§2's table says "all five hold", and that was true of the five I checked.** ⛔ **Two OTHER claims in the
same relayed conversation are FALSE**, and both are load-bearing for Q65-B — so recording them here rather
than leaving §2 reading as a clean sweep:

| architect said | 📐 measured |
|---|---|
| *"**CGF already has `GhostCreationSystem` and `GhostPromotionSystem` installed**"* | ⚠ **half right.** Creation ✅ — `NedReplicationModule:252`, unconditional, "all roles". 🔴 **Promotion ✗** — role-gated at `:308` *(pure-IG)* and `:356` *(Muscle)*, and CGF is pure `Brain` |
| *"If SimHost publishes `EntityMaster`, CGF receives it, spawns a ghost, and **hydrates it without issue**"* | 🔴 **FALSE** — the ghost is created, but **no TKB descriptor projection happens on CGF**. That is exactly what promotion does, and CGF does not have it |

⇒ ⭐⭐ **The architect was MORE optimistic than the code warrants about the RECEIVING half.** ⛔ It reasoned
that because the primitives are generic toolkit modules, every node effectively has them — ⚠ **the role gate
is invisible from that altitude.** ⇒ ⭐ **This strengthens Q65-B rather than weakening it**: without Q65-B,
path 2 half-works — the entity exists cluster-wide and the Brain never projects its template.

⚠ **Stated fairly:** the architect's *architectural* conclusion — that nothing in DDS/NED/FDP forbids
peer-to-peer genesis — is **correct and verified** *(§2 ①–⑤)*. ⛔ Its per-host **composition** claims are
where it was wrong, twice, in the optimistic direction.

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
| **②** | 🔴🔴 **IG's tools publish bus-level `SpawnEntityCommand`, and it leaves the DDS request UNOWNED** — 📐 `SpawnEntityCommandEgressTranslator.cs:167` writes `Owner = default`, and `NedCgfEntityLifecycleAdapters.cs:76` maps `OwnerAppInstanceId = msg.Owner.AppInstanceId` ⇒ **`0` ⇒ broadcast ⇒ the arbiter (CGF)**. ⭐⭐ **This is the whole mechanism of the "everything goes via CGF" bottleneck: one unset field plus a missing local pipeline** | ⇒ retarget to a self-targeted `CreateEntityRequest`; same for `SimHostScenarioManager.SpawnVehicle`. ✅ **§1.1's graph pass shrank this one**: `SpawnEntityCommandEgressTranslator` is **already** in the shared `Hrot.Network.NED` assembly behind an `INetworkFactory` method all three factories implement ⇒ **no code moves, only the originator's target changes.** ⚠ Keep the translator — 🔒 path 1 is still correct for brain-enabled entities |
| **②b** | 🔴 **`GhostPromotionSystem` is ROLE-gated inside `NedReplicationModule`**, and pure-Brain *(CGF)* is excluded by construction | ⇒ ⭐ see Q65-B. ⚠ **Not a host-composition oversight** — today it is correct, because pure-Brain spawns rather than receives. It becomes a gap **only once Q65-A′ lands** |
| **③** | ⚠ **the ELM ACK handshake on a node with no peers** | the Editor runs offline with a `SequentialIdAllocator` and no peers to ACK. ⭐ It already works *(`isDefaultProcessor: true`, local bus)* — ⛔ but **verify the handshake does not stall** when a *networked* node self-targets and a peer is absent |
| **④** | 🔴🔴 **ownership DELEGATION is role-gated, and the gate has NO justification inside it** — `DeferredTakeOwnershipEgressTranslator` on `_roleHasBrain` *(`NedReplicationModule.cs:230`)*, its ingress on `_roleHasMuscle` *(`:232`)*, `DeferredTakeoverSystem` on `_roleHasMuscle` *(`:206`)* | ⚠⚠ **CORRECTED `2026-08-31` — see §5.3.** An earlier version of this row said the gate was *"correct, do not widen speculatively."* 🔴 **That was asserted from a role NAME and the architect's claim, without opening the three classes.** 📐 All three are pure mechanism. ⇒ filed as **`CE-142`** |

### 5.1 📐 THE PER-NODE GAP — **measured `2026-08-31`; three pieces, none of them protocol**

| node | path 1 → arbiter | path 2 → self-owned | what it lacks |
|---|---|---|---|
| **CGF** · **Editor** · **Stride editor** | ✅ | ✅ | 🔴 **CGF lacks ghost PROMOTION** *(pure-Brain gate)* ⇒ Q65-B |
| 🔴 **IG** | ✅ *(its egress translator, `Owner = default`)* | 🔴 **suppressed** | request source · `CreateEntityRequestSystem` · `NetworkSpawningSystem` |
| 🔴 **SimHost** | ⛔ cannot even RECEIVE one | ⚠ partial — has the spawn system, no way to raise a request | request source · `CreateEntityRequestSystem` |
| 🔴 **Stride node** | ⛔ | 🔴 suppressed | request source · `CreateEntityRequestSystem` |

### 5.2 ✅⭐⭐ WHAT IS ALREADY UNIFORM — **the publish half needs NOTHING** *(measured `2026-08-31`)*

📌 **The question that prompted this:** *"IG must be able to publish `EntityMaster` itself, check
it."* ⇒ ✅ **It already can.**

`SharedTranslatorPack` is documented as *"the shared translator set that all `NodeRole` values install
**regardless of specialisation**"*, and 📐 `NedReplicationModule.cs:213-216` gates its construction
on **`participant != null` ONLY — not on role**. IG calls `.WithReplication(role)` at
`IgNodeBootstrapper.cs:142`, so IG holds all twelve, including:

| translator | why it matters here |
|---|---|
| `EntityMasterEgressTranslator` *(ordinal 0)* | ⭐ announces existence cluster-wide — **the thing that was in doubt** |
| ⭐⭐ **`MapVisualOverlayEgressTranslator`** | *"publishes tactical-graphic overlay geometry for **owned** area entities"* — 🔒 **literally the IG map-drawing use case** |
| `GeoSpatialEgressTranslator` · `EntityInfoEgressTranslator` | position + affiliation for **owned** entities |
| `EntityMasterIngressTranslator` · `MapVisualOverlayIngressTranslator` | ⭐ how **other IGs** receive it — also ungated |

⭐⭐ **And the pack's own comments show the uniformity was deliberate one layer down:** `GeoSpatialEgress`
and `MapVisualOverlayEgress` were **moved into** the shared pack *"so Brain nodes (CGF) can publish overlays
for area entities they own — same rationale as `GeoSpatialEgressTranslator`."*

⇒ ⭐⭐⭐ **IG is not missing the ability to PUBLISH. It is missing the ability to BECOME THE OWNER.** ⛔ So
the fix is three composition pieces, and **zero** translator, protocol or wire changes.

⚠⚠ **One asymmetry remains — and `2026-08-31` measurement shows it is NOT justified.** 🔴 **The text that stood here claimed the `_roleHasBrain` gate was "correct" and said "do not widen it speculatively." That was wrong, and §5.3 replaces it.**

### 5.3 🔴🔴 `CE-142` — **ownership delegation is MECHANISM gated by POLICY** *(measured `2026-08-31`)*

> 🔒 **User:** *"why that? what does the ownership has to do with hasBrain? why not same everywhere?"*

⛔⛔ **It has nothing to do with it.** ⚠ **Obstacle ④ and §5.2's closing caveat BOTH previously said the
gate was correct and must not be widened** — 🔴 asserted from a role NAME plus the relayed architect claim,
**without opening the three classes.** 📌 Same failure shape this document exists to catch, and the third
instance of it in this programme *(after Q65-A's "only", and Q65-B's mechanism twice)*.

#### 📐 THE PROBE — all three pieces read end to end

| piece | gate | what is actually inside |
|---|---|---|
| `DeferredTakeOwnershipEgressTranslator` | `_roleHasBrain` *(`:230`)* | reads `DeferredTakeOwnershipCommand` off the bus, converts `DescriptorGrant` → `DescriptorOwnerEntry`, writes ONE DDS sample. ⭐ **Zero role logic.** Its only *"Brain"* is a doc comment — *"installed on the Brain (CGF) node only"* — ⛔ a statement of the WIRING, not a reason |
| `DeferredTakeOwnershipIngressTranslator` | `_roleHasMuscle` *(`:232`)* | *"extracts only the entries whose `NodeId` equals the local node ID"* ⇒ ⭐ **it already self-filters.** A node with no grants addressed to it does nothing |
| `DeferredTakeoverSystem` | `_roleHasMuscle` *(`:206`)* | `PendingAuthorityGrants` + `Constructing` → `SetAuthority` → `OwnershipUpdate` for symmetrical yield. ⭐ **No role logic** — again only a comment |

⭐⭐ **The ONE legitimately role-specific thing is `BrainMuscleOwnershipStrategy`** — and that is a **POLICY**:
one implementation of the generic `IOwnershipDistributionStrategy` seam
*(`GetInitialGrants(entityType, masterNodeId)`)*, **injected**. Its content *(delegate `dtWorldPos` +
`dtNavigationStatus` to the least-loaded Muscle, keep mission/intent on the Brain)* **should** be role-aware.

⇒ 🔒🔒🔒 **The gate conflates POLICY with MECHANISM: the transport is gated on the role
that happens to hold the only current policy.** ⛔ Delegating ownership is *"here is a grant, addressed to a
node id"* — the descriptors, the wire type and the filtering are all node-agnostic. 🔒 **Squarely inside
§0's ruling: a capability removed by design.**

#### ✅ THE SAFETY PROBE — **ungating the RECEIVE side is free**

⚠ **The stated risk was:** a node receiving a grant for a component it does not register would throw,
since `EntityRepository` throws on unregistered writes. 📐 **Measured — it cannot happen.**
`DeferredTakeoverSystem.ExecuteTakeover` is **doubly guarded**:

```csharp
if (ownerNodeId != _localNodeId) continue;                 // self-filters AGAIN, after the ingress
...
foreach (int componentId in _ownershipMap.GetComponentIdsForDescriptor(descriptorTypeId))
    if (repo.HasComponentByTypeId(entity, componentId))    // ⭐ per-component guard
        repo.SetAuthority(entity, componentId, true);
```

⇒ ⭐⭐ **Exactly the `tkb-1` §6.5b gate ② shape** — a component the entity does not carry is skipped
silently, no throw. ⭐ And `DescriptorOwnershipMap` is built from `IDescriptorTranslator.TargetComponentIds`,
so a node with a narrower translator set claims fewer components **by construction.**

#### ⚠⚠ THE LATENT SILENT DROP — **two unrelated gates decide one behaviour**

| where | condition |
|---|---|
| `CreateEntityRequestSystem.cs:313` — **publishes** the bus command | `_isDefaultProcessor && _ownershipStrategy != null` |
| `NedReplicationModule.cs:230` — the translator that **puts it on the wire** | `_roleHasBrain` |

📐 **Today they coincide by CONVENTION ONLY** — the ctor doc says *"The Brain (CGF) node is always the
default processor; Muscle (SimHost) nodes must set this to false."* ⇒ ⚠ **a non-Brain node made the arbiter
and given a strategy would compute grants, publish them on its bus, and have them SILENTLY DROPPED** — no
error, no log; the entity is created, peers never claim authority, and the creator silently keeps components
it meant to delegate. 📌 **The `CLAUDE.md` silent-default pattern: the caller holds the value and
nothing consumes it.**

⭐ **Stated accurately: LATENT, not live.** ⛔ It is not reachable in today's configurations — ⚠ but it is a
coincidence between two unrelated gates, and unification is what turns those into live bugs.

#### ✅ `CE-142` — the corrected shape

| concern | gate it on |
|---|---|
| ⭐⭐ **MECHANISM** *(all three pieces)* | **`participant != null`** — ⭐ exactly what `SharedTranslatorPack` already does *(§5.2)*. ⛔ Ungated by role |
| ⭐⭐ **POLICY** *(are grants computed at all)* | **`_ownershipStrategy != null`** — ⭐ already the real lever at `:313`. A node with no strategy delegates nothing and pays one idle translator |

⭐ **This also collapses the two-gate mismatch into one condition in one place.**
⚠ **Sequence it WITH or AFTER pack step 3** — same composition surface. ⛔ **Not a prerequisite for path 2**:
single-owner entities *(the user's map drawings)* delegate nothing, so `CE-142` is about not having removed a
capability, **not** about unblocking the drawing case.

## 6. ⭐ SEQUENCING

| # | | why here |
|---|---|---|
| **1** | pack **step 4** *(catalogue contents)* | smallest, independent |
| **2** | ⭐ **move `CreateEntityRequestSystem` to a shared assembly** | obstacle ① — a pure move, and nothing below is expressible without it |
| **3** | pack **step 3** *(`EntityCreationPack`)*, now **one uniform pipeline** — ⛔⛔ **adoption order matters: Stride node → SimHost → Editor → CGF → IG LAST, and IG only together with step 4** | §2.3's halves are gone, and 🔒 the `2026-08-31` ruling forbids omitting the pipeline per host. ⚠ **See the hazard below the table** |
| **4** | **Q65-A′** — retarget originators to self-targeted requests, starting with IG's tactical graphics *(obstacle ④ says they need no split authority)*. ⭐ **Ship it in the SAME commit as IG's pack adoption** | the user's actual use case, and the safest instance of it. ⛔⛔ **Not separable from IG's step-3 adoption — see the hazard** |
| **5** | **Q65-B** — collapse the two role-gated `GhostPromotionSystem` registrations in `NedReplicationModule` into one | ⛔ **strictly after step 4.** Before Q65-A′, pure-Brain promotion is dead code — ⭐ and the gate is the ROLE, not the host, so this is a **two-line** change in one file, not a per-host sweep |
| **6** | **`CE-141`** — IG's translator width, with a live probe | |
| **7** | 🔴 **`CE-142`** — ungate ownership DELEGATION: mechanism on `participant != null`, policy on `_ownershipStrategy != null` | 📄 **§5.3.** ⭐ WITH or AFTER step 3 *(same composition surface)*. ⛔ **Not a prerequisite for path 2** — single-owner entities delegate nothing |

### ⛔⛔⛔ THE ORDERING HAZARD — **why step 3 and step 4 are ATOMIC for IG** *(added `2026-08-31`)*

📐 **Measured, and it is the one thing in this document that can break a running cluster:**

```
NetworkSpawningSystem.cs:92                     view.ReadManagedEvents<SpawnEntityCommand>()
SpawnEntityCommandEgressTranslator.cs:80        _eventBus.ReadManaged<SpawnEntityCommand>()
```

⇒ ⭐⭐⭐ **Both read the SAME bus event.** ⛔ A node holding **both** — the spawning system from step 3, and
the egress translator it already has — whose tools still publish bus-level `SpawnEntityCommand` will
**materialise the entity locally AND forward a DDS request to the arbiter, which materialises it again and
replicates a ghost back.** 🔴 **A double spawn, on the entity-creation path, in a live cluster.**

📌 **The architect flagged exactly this** — *"handing every node the same spawning pipeline would
instantly duplicate entities on the IG"* — ⚠ **and an earlier version of this document recorded it only as a
BENEFIT of Q65-A′** *("the duplicate-entity hazard dissolves")*, never as an ORDERING CONSTRAINT. ⛔ **That
omission left §6 with step 3 before step 4 and no warning.**

| ⭐ the rule | |
|---|---|
| ⭐⭐⭐ **IG's `NetworkSpawningSystem` registration and the retargeting of IG's tools ship TOGETHER** | ⛔ never step 3 for IG alone |
| ⭐⭐ **the gate that lifts the old protection is Q65-A′ — say so at the site** | 📌 `IgBootstrapperHelpers.cs`'s *"replaces SpawningModule so IG does not duplicate entities"* comment is **true today**; it must be replaced with the reason it is now safe, not silently deleted |
| ⭐ **the rail is acceptance ⑪** | 📄 `DESIGN_Entity_Creation_Unification.md` §6 ⑪ — no root holds the spawn system **and** the egress translator while any tool still publishes a bus-level order |
| ⭐ **the other four hosts are unaffected** | 📐 none of them registers `SpawnEntityCommandEgressTranslator` — `NedNetworkFactory.CreateIgEgressTranslators` has exactly one caller, `IgNodeBootstrapper.cs:351` |

⚠⚠ **Everything above is source- and graph-measured only.** `hrot-ai-debug` has been disconnected all
session, so **no claim here has been checked against a running cluster** — and obstacle ③ in particular is
exactly the kind that only shows up live.

⭐ **What the `2026-08-31` graph pass changed, so a reader can see whether it was worth it:** it corrected
one inventory row *(the egress translator is already shared)*, confirmed three *(one class each of the
request system and the two ghost systems)*, and — by making me chase *who registers promotion* through the
graph instead of assuming — **caught that Q65-B's mechanism was wrong for the second time.** ⛔ It also
measured its own blindness: the `USAGE` edges into these classes reach **only test callers**, so the
construction-site counts remain grep's.
