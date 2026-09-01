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
known-rot: §5.4's headline answer "the target is Hrot.Core/Network" is WRONG and is retracted in its own
  AS-BUILT block (2026-08-31). Hrot.Core -> Hrot.Common is a CYCLE, because CreateEntityRequestSystem
  constructs Hrot.Common.Serializers.InitialUnitSubordinateIntent by fully-qualified name. The built
  target is Hrot/Engine/Hrot.Common/Systems/, namespace Hrot.Common.Systems. Obstacle 1 is DONE.
known-rot: §5.1's and DESIGN §5.1's "IG keeps GhostDestructionSystem + IgUnitHierarchyModule and gains
  the full genesis pipeline" is WRONG — keeping GhostDestructionSystem beside NetworkSpawningSystem is
  the destroy-side double-consumption bug. Corrected 2026-08-31 in §5.6 (CE-144).
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
| **①** | 🔴 **`CreateEntityRequestSystem` lives in `Hrot.CGF/Systems/`** — a host assembly. Only CGF, the Editor and the Stride editor construct it | ✅⭐ **RESOLVED `2026-08-31` — the target is `Hrot.Core/Network/`, and the move is CLEAN.** 📐 See §5.4 |
| **②** | 🔴🔴 **IG's tools publish bus-level `SpawnEntityCommand`, and it leaves the DDS request UNOWNED** — 📐 `SpawnEntityCommandEgressTranslator.cs:167` writes `Owner = default`, and `NedCgfEntityLifecycleAdapters.cs:76` maps `OwnerAppInstanceId = msg.Owner.AppInstanceId` ⇒ **`0` ⇒ broadcast ⇒ the arbiter (CGF)**. ⭐⭐ **This is the whole mechanism of the "everything goes via CGF" bottleneck: one unset field plus a missing local pipeline** | ⇒ retarget to a self-targeted `CreateEntityRequest`; same for `SimHostScenarioManager.SpawnVehicle`. ✅ **§1.1's graph pass shrank this one**: `SpawnEntityCommandEgressTranslator` is **already** in the shared `Hrot.Network.NED` assembly behind an `INetworkFactory` method all three factories implement ⇒ **no code moves, only the originator's target changes.** ⚠ Keep the translator — 🔒 path 1 is still correct for brain-enabled entities |
| **②b** | 🔴 **`GhostPromotionSystem` is ROLE-gated inside `NedReplicationModule`**, and pure-Brain *(CGF)* is excluded by construction | ⇒ ⭐ see Q65-B. ⚠ **Not a host-composition oversight** — today it is correct, because pure-Brain spawns rather than receives. It becomes a gap **only once Q65-A′ lands** |
| **③** | 🔴🔴 **`ReliableInitType` is HARDCODED to `AllPeers`, and the request carries no field to override it** | ⚠⚠ **SHARPENED `2026-08-31`** from *"verify the handshake does not stall"* — 📐 it is not merely an unknown behaviour, **it is a MISSING FIELD.** ⇒ **`CE-143`**, §5.5 |
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

#### ✅✅ AS-BUILT — **`CE-142` IS BUILT `2026-09-01`. ⚠ And it is a PREREQUISITE, not a fix**

⭐ **What landed**, exactly the corrected shape this section prescribes:

| piece | was | is |
|---|---|---|
| `DeferredTakeOwnershipEgressTranslator` | `_roleHasBrain` | built whenever `participant != null` |
| `DeferredTakeOwnershipIngressTranslator` | `_roleHasMuscle` | built whenever `participant != null` |
| `DeferredTakeoverSystem` | `_roleHasMuscle` | registered unconditionally *(pure mechanism; its `tkbDb` parameter is measured **unused** — kept only for signature compatibility)* |

⭐⭐ **Why the direction matters, restated on `R-138` rather than on the architect's claim:** ownership is
per-component, dynamic and transferable, and `NodeRole` is a convention. ⇒ delegation must work in **every**
direction. ⛔ The old gating made one direction **structurally impossible and silent**: a Muscle-originated
entity's creator held **no egress translator**, so grants it computed would be dropped with no error —
§5.3's "latent silent drop", which `R-138` turns from latent into a real capability gap.

⛔⛔ **MEASURED AFTER THE CHANGE — CGF's authority is UNCHANGED.** `HasAuthority<BehaviorState>` /
`<LocomotionChannel>` / `<NavigationIntent>` on a CGF ghost of a SimHost-originated entity are still all
`False`. ⇒ ⭐⭐⭐ **`CE-142` opened the transport; nothing yet COMPUTES the grants.** The remaining half is
POLICY, and it is two things:

| # | what is missing | measured |
|---|---|---|
| ① | **SimHost composes no `CreateEntityRequestSystem` and holds no `IOwnershipDistributionStrategy`** | production composition sites are CGF, Editor, Stride editor only; `BrainMuscleOwnershipStrategy` is constructed solely in `NedNetworkFactory.CreateCgfEntityLifecycleAdapters():269` ⇒ **`Q65-A′`** |
| ② | **the one strategy is creator-relative and ONE-DIRECTIONAL** | `GetInitialGrants` hands `dtWorldPos` + `dtNavigationStatus` **away** to the least-loaded Muscle and keeps everything else on the creator. ⛔ There is **no Brain-ward counterpart** — no way to express *"cognition goes to the Brain node"* for an entity a Muscle originated |

⭐ **Sequencing correction:** this section previously said *"sequence it WITH or AFTER pack step 3"* and
*"not a prerequisite for path 2"*. ⚠ Both remain true **for the map-drawing case** *(single-owner entities
delegate nothing)*. ⛔ **But for a Muscle-originated BRAIN-ENABLED entity, `CE-142` is a hard prerequisite** —
without it the policy half would be built and its grants silently discarded. ⇒ **`CE-142` is done first, and
that ordering is deliberate.**

⚠ **Filing note:** `CE-142` had **no tracker row** until `2026-09-01` — it lived only in this section. It now
has one in [`Blueprint_Issues_Tracker.md`](Blueprint_Issues_Tracker.md).

### 5.4 ✅✅ OBSTACLE ① — **BUILT `2026-08-31`. ⚠ The target is `Hrot.Common`, NOT `Hrot.Core` — this section's own answer was WRONG**

📌 **Raised by the architect review (`2026-08-31`) as watch-out A**: *"ensure `JsonAttributeCompiler`
and `IOwnershipDistributionStrategy` do not drag CGF-specific or presentation-specific references down into
`Fdp.Toolkits`."* ⭐ **Good instinct, and the concern turns out not to exist** — but measuring it **answers
the assembly question outright**, so the choice the review posed *(`Fdp.Toolkits` or `Hrot.Common`)* is
neither.

| 📐 measured | |
|---|---|
| **what moves** | ⭐ **exactly 2 files**, both in `Hrot.CGF/Systems/`: `CreateEntityRequestSystem.cs` and `EntityRequestFinalizationSystem.cs` *(the latter is the `finalizationSystem` ctor arg and holds `RequestKind`)* |
| **the feared drag** | ✅ **absent.** `JsonAttributeCompiler` is already `Fdp.Toolkits/Replication/Patching/`; `IOwnershipDistributionStrategy` already `Fdp.Toolkits/Replication/Abstractions/` |
| ⭐ **host-assembly references inside the two files** | ✅ **NONE** — `grep` for `Hrot.CGF|Hrot.Map|Hrot.Editor|Hrot.IG` matches **only their own `namespace Hrot.CGF.Systems` line.** Their entire using set is `Fdp.*` plus **`Hrot.Core.Network`** |
| ⛔⛔ **why NOT `Fdp.Toolkits`** | 📐 both depend on `Hrot.Core.Network` *(`IEntityCreationRequestSource`, `IEntityAckSink`, `EntityCreationRequest`)* ⇒ **putting them in `Fdp.Toolkits` would need `Fdp.Toolkits → Hrot.Core`, INVERTING the layering** |
| 🔴🔴 **~~the answer: `Hrot.Core/Network/`~~ — RETRACTED, see the AS-BUILT block below** | ⛔ **`Hrot.Core` → `Hrot.Common` is a CYCLE.** 📐 The reasoning in this row was right about the *seam* living in `Hrot.Core/Network/`, and wrong about the *systems* being able to follow it |
| ⚠ **one tidy-up** | `EntityCreationRequest.PreAllocatedNetworkId`'s doc has `<see cref="Hrot.CGF.Systems.CreateEntityRequestSystem"/>` — ⭐ update the cref with the namespace, or it becomes a stale-doc warning |

#### ✅ 🔒 USER RULING `2026-08-31` — **`DeleteEntityRequestSystem` moves too**

> 🔒 **User:** *"move DeleteEntityRequestSystem, update docs for EntityCreationRequest's of course"*

📐 **Measured, and the case is STRONGER than the "same story" lean it was proposed on:**

| | |
|---|---|
| ⭐⭐ **it is HARD-COUPLED to a file already moving** | `DeleteEntityRequestSystem`'s ctor takes **`EntityRequestFinalizationSystem` as a REQUIRED, non-nullable arg** *(`:37-42`)* ⇒ ⛔ **leaving it behind would split a hard dependency across assemblies** |
| ✅ **its references are equally clean** | usings are `Hrot.Core.Network` + `Fdp.*` only; `grep` for `Hrot.CGF|Map|Editor|IG` matches **only its own namespace line** |
| ✅ **its seam is already in the target** | `IEntityDeletionRequestSource` is in **`Hrot.Core/Network/EntityLifecycleInterfaces.cs:102`** — the *same file* as `IEntityCreationRequestSource` |
| ⭐ **tiny blast radius** | **1** production construction site *(`CgfSubsystem.cs:728`)* + 1 test |

⇒ ✅ **THE MOVE IS 3 FILES**: `CreateEntityRequestSystem.cs` · `EntityRequestFinalizationSystem.cs` ·
`DeleteEntityRequestSystem.cs` → **`Hrot.Core/Network/`**. ⭐ Still zero new project references.

#### ✅ 🔒 AND the doc-reference fix is an explicit deliverable, not a tidy-up

⭐ `EntityCreationRequest.PreAllocatedNetworkId`'s XML doc carries
`<see cref="Hrot.CGF.Systems.CreateEntityRequestSystem"/>`. ⇒ **update it with the namespace in the same
commit as the move.** ⚠ Sweep for others — ⛔ a stale `cref` is a warning that outlives the batch.

#### 📐 A measured NON-finding, recorded so nobody re-derives it

⚠ **The deletion tier is ASYMMETRIC with creation and that is FINE.** 📐 Creation has **three**
sources *(DDS `NedEntityCreationRequestSource`, in-memory `ScenarioEntityCreationRequestSource`, and the
composite)*; **deletion has only the DDS one.** ⛔ **This is NOT a capability gap** — ⭐ the *local* destroy
path does not go through the request tier at all: **`NetworkSpawningSystem.cs:98` consumes bus
`DestroyEntityCommand` directly**, so any node the pack equips can destroy what it owns in-process. ⇒ ⭐ the
deletion **request** tier exists only for network / non-ECS clients, which is the DDS path by definition.
⚠ **I flagged this as a gap on instinct and it did not survive measurement** — recorded here so the next
session does not spend a batch adding an in-memory deletion source nothing needs.

### 5.6 🔴🔴 `CE-144` — **the DESTROY side has the SAME double-consumption hazard, and it fails SILENTLY**

📌 **Found while measuring the ruling above** — ⛔ **and it CORRECTS §5.1 / `DESIGN` §5.1, which
said IG "keeps `GhostDestructionSystem` … and gains the full genesis pipeline."** 🔴 **Keeping both
is exactly the bug.**

📐 **Two consumers of ONE bus event**, and they are **not** equivalent:

| | `GhostDestructionSystem` *(IG, `IgBootstrapperHelpers.cs:30`)* | `NetworkSpawningSystem.ProcessDestroy` *(`:98` → `:213`)* |
|---|---|---|
| what it does | `_entityMap.Unregister(...)` then **`world.DestroyEntity(entity)` — an IMMEDIATE HARD DELETE** | `cmdBuffer.SetLifecycleState(entity, TearDown)` then **`_elm.BeginDestruction(...)`** — the real ELM teardown |
| on a map miss | ⭐ silently skips | ⚠ **writes to stderr and returns** |
| suitable for | ⭐ **a GHOST** — nothing to tear down, no authority to yield | ⭐ **an OWNED entity** — lifecycle transition, ACK handshake, `EntityMaster` disposal |

⇒ 🔴🔴 **If IG holds both, whichever runs first DEFEATS the other:**

| order | consequence |
|---|---|
| `GhostDestructionSystem` first | it unregisters + hard-deletes immediately ⇒ `ProcessDestroy` finds nothing in the map, **logs to stderr and returns** ⇒ 🔴 **ELM teardown NEVER RUNS: no `TearDown` state, no `BeginDestruction`, `EntityMaster` is never disposed on the wire** ⇒ ⛔⛔ **other IGs keep the drawing as a ZOMBIE forever** |
| `NetworkSpawningSystem` first | teardown begins, then the hard delete rips the entity out **mid-teardown** ⇒ the handshake can never complete |

⭐⭐ **Either order is wrong, so the resolution is unambiguous:** ⛔⛔ **once IG has `NetworkSpawningSystem`,
`GhostDestructionSystem` must be DROPPED, not kept beside it.**

⭐ **And the code says so itself.** Its registration comment reads *"Ghost destruction — **replaces
SpawningModule** so IG does not duplicate entities"* ⇒ 🔒 **it is the DESTROY half of the very
substitution Q65-A′ undoes.** ⛔ It exists because IG lacked a materialiser — the same reason as the spawn
omission, and it retires for the same reason.

| ⭐ the item | |
|---|---|
| **what changes** | in IG's `RegisterSpawningPipeline`: **drop `GhostDestructionSystem`**, keep `IgUnitHierarchyModule`. ⭐ Ships in the **same commit** as IG's pack adoption + Q65-A′ + `CE-143` |
| ⚠⚠ **the symptom is the opposite of the spawn hazard's, which is why it is easy to miss** | spawn ⇒ **a DOUBLE entity**, loud and visible. destroy ⇒ **an UNDELETED entity on peers**, silent, and only visible on another node |
| ⭐ **the rail** | 📄 **acceptance ⑪ extended to the destroy side** — ⛔ a root must not hold `NetworkSpawningSystem` **and** a second `DestroyEntityCommand` consumer |
| ⚠ **not verified** | the actual execution ORDER of the two on IG today. ⛔ **Irrelevant to the fix** *(both orders are wrong)* — ⭐ but it decides which symptom a live IG would show first |



#### ✅✅ AS-BUILT `2026-08-31` — **3 files → `Hrot/Engine/Hrot.Common/Systems/`, namespace `Hrot.Common.Systems`**

🔴🔴 **This section said `Hrot.Core/Network/` and was WRONG.** 📐 The measurement that
broke it, found by *building* rather than reading:

```
Hrot.Common/Systems/CreateEntityRequestSystem.cs:394
    childComponents.Add(new Hrot.Common.Serializers.InitialUnitSubordinateIntent { … });   // FULLY QUALIFIED
Hrot.Common.csproj:33
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />                            // Common → Core
```

⇒ ⛔⛔ **`Hrot.Core` → `Hrot.Common` would be a CYCLE.** ⭐ `Hrot.Common` is the correct home, and it is what
§5's original wording said all along *("a shared assembly (`Fdp.Toolkits` or `Hrot.Common`)")* — ⚠ **it was
this section's later "resolution" that narrowed it to `Hrot.Core` on an incomplete check.**

| ⚠⚠ **the error class — THIRD instance in one day** | |
|---|---|
| what I checked | the three files' **`using` directives**, and concluded *"their entire using set is `Fdp.*` plus `Hrot.Core.Network`"* |
| what that cannot see | 🔴 **a FULLY-QUALIFIED type reference in a method body.** `Hrot.Common.Serializers` needs no `using`, so it was invisible |
| the other two instances | `CharacterAnimationDefDto`'s assembly *(step 4, §3.3 finding ③)* and the `CE-145` file count *(24 → 53 → 56)* |
| ⭐ **the checkable habit** | ⛔ **a usings scan is NOT a dependency scan.** ⭐ For a cross-assembly move, either `grep` the body for `<OtherAssembly>\.` prefixes, **or just BUILD IT** — the compiler is the only complete answer, and it costs 8 s per project |

##### ⭐ Why not move `InitialUnitSubordinateIntent` down to `Hrot.Core` instead

📐 **Measured: ~30 consumer files**, and it lives in `Hrot.Common/Serializers/GenesisIntentComponents.cs`
with sibling genesis-intent DTOs and a matching `GenesisIntentRegistry`. ⇒ ⛔ **far more churn than moving the
three systems, and it would split a cohesive file.** ⭐ Routing the systems to `Hrot.Common` is the smaller,
truer change.

##### ✅ `Hrot.Common` satisfies "every node can register it"

| 📐 | |
|---|---|
| **references `Hrot.Common` DIRECTLY** | `Hrot.CGF` · `Hrot.SimHost` · `Hrot.IG` · `HrotStrideApp.Game` · `Hrot.ClusterRunner` · `Hrot.ExCon` · `Hrot.NodeComposition` · `Hrot.Orchestrator` · `Hrot.Network.NED` · `Hrot.Blueprints.Compiler` |
| **transitively** | ⭐ **`Hrot.Editor`** — via `Hrot.SimHost` / `Hrot.CGF` / `Hrot.Network.NED` |
| ⭐⭐ **and it is where `SharedApplicationBootstrapper` already lives** | `Hrot.Common/Infrastructure/` ⇒ **this is the shared HOST-level assembly by construction**, which is exactly the layer a universally-registered system belongs to |

##### 📐 The change, and why the churn was small

| | |
|---|---|
| **moved** | `CreateEntityRequestSystem.cs` · `EntityRequestFinalizationSystem.cs` · `DeleteEntityRequestSystem.cs` → `Hrot/Engine/Hrot.Common/Systems/`, namespace `Hrot.CGF.Systems` → **`Hrot.Common.Systems`** |
| ⭐ **namespace RENAMED, not preserved** | ⛔ unlike `CE-145`, where preserving it was the cheap win. 🔒 Here the name is the *point*: **keeping "CGF" in the name of a type every node registers would perpetuate the misconception Q65 exists to kill** |
| **consumer edits** | ⭐ **6 one-line `using Hrot.Common.Systems;` additions** *(`CgfSubsystem`, `SimHostInstance`, and 4 `Hrot.SimHost.Tests` files)*. 📐 3 more already had it |
| ✅⭐ **the Stride fence held with ZERO action** | 📐 `EditorStrideSubsystem.cs` **already** imported `Hrot.Common.Systems`, so it needed no edit — ⇒ **the one file both lanes could reach was never actually contested.** 📄 `HANDOFF_CE145_Stride_Windows.md` §4 |
| ⚠ **one hazard worth naming** | `Hrot.Core` has `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, so `EntityLifecycleInterfaces.cs`'s `<see cref="…CreateEntityRequestSystem"/>` — now pointing into an assembly `Hrot.Core` does not reference — would be **CS1574 ⇒ an ERROR**. ⭐ Changed to `<c>…</c>` |
| ⭐ **each moved file carries a header** | why it moved, why the namespace changed, and why `Hrot.Common` and not `Hrot.Core` |

##### 🔴 A PRE-EXISTING BREAK found and fixed on the way

📐 **`Hrot.SimHost.Integration.Tests` did not compile AT ALL** on the base commit: `SimHostInstance.cs`
used `AttributeCompilerFactory` *(`Fdp.Toolkit.Replication.Attributes`)* with **no import for it**.
⭐ **Proven pre-existing** by `git stash -u` + rebuild — the same `CS0103` fires on base with none of this
work present. ⇒ ⭐ fixed with the one missing `using`, and **7/7 `EntityCreationFlowTests` now pass** — which
is the strongest verification this move has, since those exercise `CreateEntityRequestSystem` end to end.
⚠ **Out of my lane** *(test-suite reliability is the backend lane's `QA-` area)* — ⭐ fixed anyway because it
was one line and it was blocking verification of my own change; **flagged here so the backend lane knows.**

##### 📐 Gates

| gate | result |
|---|---|
| per-project builds *(`--no-restore`; ⛔ never the solution)* | ✅ `Hrot.Core` · `Hrot.Common` · `Hrot.CGF` · `Hrot.Editor` · `Hrot.SimHost` · `Hrot.Network.NED` · `Hrot.Presentation` · `Hrot.SimHost.Tests` · `Hrot.Editor.Tests` · `Hrot.SimHost.Integration.Tests` |
| ⭐ **new rails** `RequestTierPlacementRails` | ✅ **12/12** — no host assembly, no "CGF" in the namespace, publicly constructible, `IEcsModuleSystem` |
| ⭐ **non-vacuity probe** *(expectations flipped to the OLD assembly/namespace)* | ✅ **exactly 6 of 12 redden** — the two placement rails × three types. ⚠ **Stated accurately: this proves the rails READ REALITY, it is not a defect red-proof** — a real one would mean reverting the move |
| **T1 `Hrot.SimHost.Tests`** | ✅ **810 passed** *(798 + 12 new)* · 1 failed · 3 skipped |
| **`Hrot.Editor.Tests`** | ✅ **341 passed** · 0 failed · 1 skipped |
| **`EntityCreationFlowTests`** *(integration)* | ✅ **7/7** |
| 🔴 **the 1 red** | `FullBranchPipelineTests.BranchedRecording_…` = **`QA-012`**, pre-existing, proven by stash+rebuild on base earlier this session |
| ⛔ **NOT verified** | the Stride tree — ⭐ but `EditorStrideSubsystem` needed **no edit at all**, so the exposure is a stale `using Hrot.CGF.Systems;` that may now be unused *(a warning at most)* |

### 5.5 🔴🔴 `CE-143` — **`ReliableInitType` is hardcoded; the request cannot express "do not wait for peers"**

📌 **Raised by the architect review as watch-out C, and it is the review's most valuable finding.**
⚠ **Obstacle ③ previously said only *"verify the handshake does not stall."*** 📐 **Measured — it is
not an unknown behaviour, it is a missing field:**

| 📐 | |
|---|---|
| `CreateEntityRequestSystem.cs:302` *(the root entity)* and **`:397`** *(auto-spawned TKB children)* | ⛔ **`InitType = ReliableInitType.AllPeers` — hardcoded, at BOTH sites** |
| `EntityCreationRequest` *(all 8 members)* | ⛔ **carries NO `ReliableInitType`** — `RequestId`, `OwnerAppInstanceId`, `TkbType`, `DisType`, `InitialAttributesJson`, `InitialComponents`, `PreAllocatedNetworkId`, `ChildComponentOverrides` |
| ⭐ the enum already has the values | `ReliableInitType` = **`None`** · **`PhysicsServer`** · `AllPeers` *(`Fdp.Toolkits/Replication/ReliableInitType.cs`)* |

⇒ 🔴 **Consequence for path 2:** an IG tactical drawing created via a self-targeted request is spawned
`AllPeers`, so **the ELM holds it in `Constructing` until every expected peer returns a
`ConstructionAck`** — pointless latency for a single-owner presentation entity, and a **stall risk if a peer
is absent or slow.** ⛔ **And the authoring code has no way to say otherwise.**

#### ⭐⭐ THE DESIGN POINT — **two INDEPENDENT axes, do not conflate them**

| axis | field | what it decides |
|---|---|---|
| ⭐ **ownership target** | `OwnerAppInstanceId` | **WHO runs genesis** — the arbiter, or me |
| ⭐ **init reliability** | `ReliableInitType` | **whether the creator WAITS for peers** before going `Active` |

⛔⛔ **Folding reliability into the affordance would be wrong** — *"I own this"* does **not** imply *"nobody
needs to ACK it"*: a node could locally own something genuinely simulated. ⇒ ⭐⭐ **`ReliableInitType` is an
explicit, defaulted parameter on both affordances:**

```csharp
creation.CreateLocallyOwned(tkbType, transform, components,
                            initType: ReliableInitType.None);      // ⭐ IG map drawing: don't wait
creation.RequestFromDefaultProcessor(tkbType, transform, components);  // defaults to AllPeers
```

| ✅ | |
|---|---|
| ⭐⭐ **default `AllPeers` on BOTH** | ⇒ **adoption changes nothing** — 📄 acceptance ⑥'s byte-identical default holds, and no existing caller behaves differently |
| ⭐ **`EntityCreationRequest` gains one `init`-only member** | ⭐ **additive**, defaulted `AllPeers`; the two hardcoded sites read it instead |
| ⚠ **`:397`'s children** | ⛔ **decide explicitly whether children inherit the parent's `InitType`** — 📌 my lean is YES *(a drawing's children are as local as the drawing)*, but it is a separate line and must not be left implicit |
| 🔴 **STILL UNVERIFIED — the live half** | ⭐ **do peers ACK ghosts of entities they neither simulate nor own?** ⛔ Only a running cluster answers it, and `hrot-ai-debug` has been down all session. ⚠ **`ReliableInitType.None` SIDESTEPS the question for presentation entities** — ⛔ it does **not** answer it for a locally-owned entity that legitimately wants `AllPeers` |

⇒ ⭐ **Sequence `CE-143` WITH Q65-A′** *(step 4)* — ⛔ **it is the one item that is a genuine prerequisite for
IG's drawings being usable**, as opposed to merely correct.

## 6. ⭐ SEQUENCING

| # | | why here |
|---|---|---|
| **1** | pack **step 4** *(catalogue contents)* | smallest, independent |
| ✅ **2** | ✅✅ **DONE `2026-08-31`** — the 3 request-tier files moved to **`Hrot/Engine/Hrot.Common/Systems/`**, namespace `Hrot.Common.Systems` | obstacle ① — a pure move, and nothing below is expressible without it. ⚠⚠ **The target was NOT `Hrot.Core`** — that would be a cycle; §5.4's AS-BUILT block carries the correction |
| **3** | pack **step 3** *(`EntityCreationPack`)*, now **one uniform pipeline** — ⛔⛔ **adoption order matters: Stride node → SimHost → Editor → CGF → IG LAST, and IG only together with step 4** | §2.3's halves are gone, and 🔒 the `2026-08-31` ruling forbids omitting the pipeline per host. ⚠ **See the hazard below the table** |
| **4** | **Q65-A′** — retarget originators to self-targeted requests, starting with IG's tactical graphics *(obstacle ④ says they need no split authority)*. ⭐ **Ship it in the SAME commit as IG's pack adoption** | the user's actual use case, and the safest instance of it. ⛔⛔ **Not separable from IG's step-3 adoption — see the hazard** |
| **4b** | 🔴 **`CE-143`** — add `ReliableInitType` to `EntityCreationRequest` *(default `AllPeers`)*; IG drawings pass `None` | 📄 **§5.5.** ⛔ **WITH step 4** — the one real prerequisite for IG drawings being USABLE rather than merely correct |
| **4c** | 🔴 **`CE-144`** — DROP `GhostDestructionSystem` from IG when it gains `NetworkSpawningSystem` | 📄 **§5.6.** ⛔ **WITH step 4** — keeping both silently skips ELM teardown and leaves ZOMBIE drawings on peer IGs |
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
