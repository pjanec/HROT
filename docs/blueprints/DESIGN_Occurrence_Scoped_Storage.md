<!--STATUS
state: LIVE
updated: 2026-09-19
build-state: DESIGN
current-answer: the whole document. §4 is the ExtDeps justification; §6 is the sequence.
stale-below: nothing.
known-rot: none at authoring time.
known-conflict: none. This document EXTENDS DESIGN_Parameter_Model.md §4 rather than
  overturning it; where they disagree, DESIGN_Parameter_Model.md wins and this file is wrong.
related-designs:
  - DESIGN_Parameter_Model.md — owns WHAT a parameter is, the {Input,State} role model, the
    one-resolver rule and the "params belong to the occurrence" ruling. This document owns
    WHERE the bytes live and HOW an occurrence is addressed.
  - EXPLAINER_Where_Parameters_And_State_Live.md — owns the file:line measurement record of
    the current storage map. This document owns the target map.
  - Architect_Question_34_Blueprint_Occurrence_Identity.md — owns blueprint Instance slot
    identity (attach the same asset twice). This document owns the behaviour-side occurrence.
  - Blueprint_Subsystem_Runtime_Detailed_Design.md §4–§5 — owns the partition allocator's
    header, slot entry and free-list contract. This document does not change that contract.
  - docs/designs/btree-hsm-unif/DESIGN.md — owns hot reload, terminal-state routing and the
    interrupt path across the two paradigms. It does NOT cover storage; nothing in it moves here.
-->
# DESIGN — occurrence-scoped storage

> **The goal, in one sentence.** Let one entity run an HSM (strategic), a BTree hosted inside an
> HSM state (tactical), and blueprints as their actions and conditions — concurrently, including
> across parallel HSM regions — by making **the occurrence, not the entity, the unit that owns
> memory**.
>
> **This is not a new mechanism.** The allocator exists, is production-proven, and one of the three
> behaviour systems already runs entirely on it. The work is adoption, plus one small and
> well-bounded change inside `FastHSM`.

---

## 1. INVENTORY

Graph first, grep to corroborate; neither alone settles an exhaustive claim.

| query | tool | result |
|---|---|---|
| complete set of tick systems | `search_graph(".*TickSystem.*", label=Class)` | **10, `has_more:false`** — 6 production: `BTreeTickSystem`, `HsmTickSystem`, `BlueprintTickSystem`, `PlaybackTickSystem`, `RecorderTickSystem`, `CommanderUtilityTickSystem` |
| the storage components | `search_graph("^(BrainBlackboard\|Blackboard1024\|BrainBTreeState\|BrainHsm64\|BrainHsm128\|BehaviorState)$")` | **23, `has_more:false`** — one component each per entity |
| the allocator | `search_graph(".*BlueprintBlackboard.*")` | `BlueprintBlackboardPartitions` + `BlueprintBlackboardHeader` + 3 tier components + `BlueprintBlackboardTiers` |
| `BlueprintTickSystem` production construction | `search_code("new BlueprintTickSystem")` → `trace_path(WireBlueprintRuntime, inbound)` | **1 production site**, reached only from `EditorSubsystem.Initialize` + the two Stride editor boots |
| `NodeType.Subtree` execution | `search_code("NodeType.Subtree", limit=60)` | one kernel site — `Interpreter.ExecuteNode` (`:229-231`), returns `Failure` |
| `ExecuteAction` call sites | `search_code("ExecuteAction(")` | `HsmKernelCore.ExecuteAction` (`:762-782`); 5 internal callers — `InitializeSlot:306`, `ProcessActivityPhase:450`, `ExecuteTransition:683,698,730` |
| consumer file set | `scripts/find.sh BrainBlackboard --glob '*.cs'` · same for `Blackboard1024` | **⚠ both graph pages TRUNCATED at the 500-match cap.** Counted from grep instead: **75** production files touch `BrainBlackboard`, **52** touch `Blackboard1024` (excluding tests, `obj/`, generated) |

**Coverage:** `check_index_coverage` over the four behaviour/kernel scopes → `no_recorded_issue`,
index generated `2026-09-18 20:36Z`, `index_mode: full`, `generation_matches: true`. Best-effort,
never proof of completeness.

⚠ **Two claims deliberately NOT re-measured here**, carried from `BP-297` and flagged as inherited:
*"zero DTO-bound HSM thunks exist in any binary"* and the 55-method `[HsmAction]` census. The
attribute sweep truncated at 150 of 173 results. **§6 does not depend on either.**

---

## 2. The structure

### 2.1 As-is vs to-be

```mermaid
classDiagram
    direction LR

    class BehaviorState {
        +int ActiveBehaviorHash
        +uint InstanceId
        +byte BrainTier
    }
    note for BehaviorState "EXISTS. One per entity.\nBrainTier selects BTree XOR HSM."

    class BrainBlackboard {
        +byte[100] BehaviorParameters
        +byte ExpectedThreatLevel
        +byte Interrupt_MobilityLost
    }
    note for BrainBlackboard "EXISTS. 128 B.\nParams AND entity facts in one struct."

    class Blackboard1024 {
        +byte[1024] Memory
    }
    note for Blackboard1024 "EXISTS. Shared by BTree, HSM,\nBlueprint AND squad at disjoint offsets."

    class BrainBTreeState {
        +BehaviorTreeState State
    }
    class BrainHsm64 {
        +HsmInstance64 State
    }
    note for BrainBTreeState "EXISTS. One per entity\n=> one tree per entity."

    class OccurrenceTier {
        +byte[] Memory
        +Header header
        +SlotEntry[] slots
    }
    note for OccurrenceTier "EXISTS as BlueprintBlackboard1024/4096/16384.\nRenamed only; allocator unchanged."

    class BrainInterrupts {
        +byte ExpectedThreatLevel
        +byte Interrupt_MobilityLost
        +byte Interrupt_Reserved
    }
    note for BrainInterrupts "NEW. The entity facts that were\nthe tail of BrainBlackboard."

    class SquadCognitiveStateComponent {
        +SquadCognitiveState State
    }
    note for SquadCognitiveStateComponent "NEW. Was projected onto\nthe commander's Blackboard1024."

    class OccurrenceSlot {
        +OccurrenceHeader head
        +byte[] Params
        +byte[] State
    }
    note for OccurrenceSlot "NEW payload shape.\nOne per running occurrence."

    OccurrenceTier "1" *-- "0..N" OccurrenceSlot : allocates
    BrainBlackboard ..> BrainInterrupts : tail becomes
    BrainBlackboard ..> OccurrenceSlot : params become
    Blackboard1024 ..> OccurrenceSlot : AI state becomes
    Blackboard1024 ..> SquadCognitiveStateComponent : squad state becomes
    BrainBTreeState ..> OccurrenceSlot : tree state becomes
    BrainHsm64 ..> OccurrenceSlot : HSM instance becomes
```

**What the picture shows that prose hid:** `BrainBlackboard` and `Blackboard1024` are each **two
unrelated things in one struct**. Splitting them by *lifetime* — entity fact vs occurrence state —
is what makes the migration tractable, and it is why "delete both components" is the wrong framing:
two of the four outgoing edges go to new **named** components, not to slots.

### 2.2 Who ticks what, on which host

```mermaid
graph TD
    subgraph Editor["Editor host"]
        E1[EditorSubsystem.Initialize] --> E2[BlueprintRuntimeWiring.WireBlueprintRuntime]
        E2 --> E3[BlueprintTickSystem]
    end
    subgraph Cognitive["CognitiveRuntimeModule - every ECS host"]
        C1[BTreeTickSystem]
        C2[HsmTickSystem T]
        C3[BehaviorIngressSystem]
    end
    subgraph Hosts["CGF / SimHost"]
        H1[schedules CognitiveRuntimeModule]
        H2[no BlueprintTickSystem]
    end

    H1 --> C1
    H1 --> C2
    H1 --> C3
    H2 -.DEAD EDGE.-> E3

    C3 -->|provisions slots| T1[(Occurrence tiers)]
    E3 -->|walks slot table| T1
    C1 -->|reads params| T1
    C2 -->|reads params| T1

    classDef dead stroke-dasharray: 5 5
    class H2 dead
```

**Caption — what only this diagram shows, stated precisely.**

⭐ **Blueprints DO run on CGF — in their other mode.** A blueprint whose `Dispatch == AiPrimitive`
compiles into a BTree/HSM **action or condition** and executes inside the behaviour thunk, driven by
`BTreeTickSystem`/`HsmTickSystem`, which CGF schedules. That is the mode the shipped CGF content
uses, and nothing here is wrong with it.

🔴 **Blueprint *Instances* are the half that is broken, and it is a HALF-WIRED state rather than an
absent one.** `CgfSubsystem:1004` calls `BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems`,
which registers **`BlueprintMaterializationSystem`** (resolves `InitialBlueprintsIntent` from scenario
state and **pre-provisions the tier**) and **`BlueprintEventIngressSystem`** (attach/switch by event).
Neither is the tick system. The only production code that constructs and schedules
`BlueprintTickSystem` is `BlueprintRuntimeWiring.WireBlueprintRuntime` + `SpliceIntoSimulation`,
called from **`EditorSubsystem.Initialize:1585/1595` and nowhere else** (the integration `EditorHarness`
is the only other caller).

⇒ **CGF attaches Instances, allocates their slots and dispatches their attach/switch events — and
never ticks them.** That is worse than missing: it looks wired. Scheduling twin of the registration
gap that crashed `--mode all` on `2026-09-03` (recorded in `BlueprintBlackboardTiers`' own header).
**Independent of everything else here** — item `O0`.

### 2.3 Activating and ticking a nested occurrence

```mermaid
sequenceDiagram
    participant Ing as BehaviorIngressSystem
    participant Alloc as OccurrencePartitions
    participant Hsm as HsmTickSystem
    participant Kern as FastHSM kernel
    participant Thunk as generated thunk
    participant BT as BTree interpreter

    Ing->>Alloc: TryAttach(key(asset, hostPath), size, hash)
    Alloc-->>Ing: payloadOffset (zeroed)
    Ing->>Alloc: ParseParams(json, slot+paramsOffset, world, self)
    Note over Ing,Alloc: resolve BEFORE commit - a bad parse<br/>leaves the entity on its old behaviour

    Hsm->>Kern: Update(def, ref instance, ref bridge, dt)
    loop each region r
        Kern->>Kern: bridge.Occurrence = (r, leafId)
        Kern->>Thunk: ExecuteAction(id, instance, ctx, writer)
        Thunk->>Alloc: TryGetSlotOffset(key(asset, r, state))
        Alloc-->>Thunk: payloadOffset
        Thunk->>BT: Tick(ref slotParams, ref slotTreeState, ref ctx)
        BT-->>Thunk: NodeStatus
    end
    Thunk-->>Kern: (status routed as an HSM event)
```

**Caption.** The only new information crossing the ExtDeps boundary is the single
`bridge.Occurrence = (r, leafId)` write. Everything below it — key, lookup, params base, tree state —
is resolved on our side of the line.

---

## 3. The model

**An occurrence is a running instance of an asset on an entity.** Its identity is
`(assetId, hostPath)`, where `hostPath` is the chain of `(hostOccurrence, siteId)` pairs from the
root. A root behaviour has an empty host path. This is not a new key function:
`ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)` already puts an occurrence
discriminator into the slot table's `BlueprintId` field, and `Q34 §7` measured that the field is
**already polymorphic** — blueprint ids and stateful slot keys coexist in one table, correctly.

**Slot payload:** `[OccurrenceHeader][Params P][State S]`. `DESIGN_Parameter_Model.md` §3.3 already
rules that params must not sit at offset 0 for blueprint Instances (the 16-byte
`BlueprintLatentCursor` lives there); the header generalises that reservation.

**Why every thunk survives.** `NodeLogicDelegate<TBlackboard, TContext>` is generic and the
interpreter never touches the blackboard's members — measured at `Interpreter.ExecuteAction`
(`:643-671`), which calls `actionDelegate(ref bb, ref state, ref ctx, node.PayloadIndex)` and
nothing else. A `[SharedAiAction]` thunk bakes only the **field** offset within the DTO, so it is
valid wherever that DTO lives. Same offsets, different base.

### 3.1 ⭐⭐ Hosting one graph inside another ALREADY SHIPS — and it is the template

⛔ **Correction to an earlier reading of mine: `NodeType.Subtree`'s `Failure` stub is NOT how BTree
hosts a BTree, and BTree-hosts-BTree is not a missing feature.** Measured:

- `BTreeEmitCore.EmitSubtree` (`:858-865`) emits `.Subtree("Name", …)` into the fluent builder;
- `BTreeOrchestratorEmitCore` (`:135-176`) generates a **`[BTreeAction] Orchestrate_<Sub>_Tick`**
  whose whole body is
  `ref var subBb = ref master.<VarName>; return <Sub>.GetInterpreter().Tick(ref subBb, ref state, ref ctx);`
  — with an *Approach B* variant that does **COPY IN → tick → COPY OUT** over `SubtreeSyncBindings`.

⇒ ⭐⭐⭐ **Subtree hosting is implemented as "an action that ticks the child interpreter inline,
handing it its own blackboard."** That is *exactly* the mechanism HSM-hosts-BTree needs. The child's
blackboard is a **compile-time slice of the master's** (`master.<VarName>`), so this design's
migration is *"replace the compile-time slice with an allocated slot"* — a narrowing of an existing
mechanism, not a new one. ⛔ **Nothing here removes, deprecates or reroutes it.**

🔴 **But it exposes a live defect of precisely this document's shape.** The generated orchestrator
ticks the child with **`ref state` — the MASTER's `BehaviorTreeState`**. The hosted occurrence has
**no runtime state of its own**: `RunningNodeIndex` and the local registers are shared between host
and child. With one child at a time and no suspension it mostly survives; with two hosted children,
or a child left `Running` while the host advances, host and child overwrite each other. **This is the
BTree twin of the HSM two-region collision, and it is in shipped code today.** `O4` fixes it by
giving the hosted occurrence its own `BehaviorTreeState` in its own slot — which is the same
machinery `O8` needs, so the fix and the feature are one piece of work.

⚠ **The `NodeType.Subtree` kernel stub is a separate, unused path.** It is not what ships, and this
design does not touch it; whether it is ever implemented or deleted is out of scope here.

---

## 4. ExtDeps — what must change, and why it cannot live anywhere else

> The standing rule: an ExtDeps edit needs a reason of the form *"this information exists only
> here"*, not *"this is where it was convenient to patch."*

### 4.1 FastBTree — **no change at all**

| candidate | verdict |
|---|---|
| `Interpreter<TBlackboard,TContext>` | ⛔ **no change.** The type argument is supplied by the caller and the kernel never reads the blackboard's members (`Interpreter.cs:643-671`). Pointing a tree at slot-resident params is a change to **our** call site and **our** generator |
| `BehaviorTreeState` | ⛔ **no change.** It moves from a component field to a slot payload field; the struct is unmodified |
| `NodeType.Subtree` stub (`Interpreter.cs:229-231`) | ⚠ **deliberately left alone for now** — see `Q1` in §8. Hosting a tree under an HSM state does **not** need it; it is BTree-hosts-BTree, a different axis |

### 4.2 FastHSM — **one change, and here is why it belongs there**

The occurrence — region index `r` and active state `current` — exists **only inside
`HsmKernelCore`'s own loops** (`ProcessActivityPhase:436-454`) and is discarded at the private
`ExecuteAction` wrapper (`:762-782`), which forwards `(actionId, instancePtr, contextPtr, writerPtr)`
and nothing else. The context struct is built **once per entity per tick**, before the region loop
starts. **No amount of work on our side can recover which region is executing.** That is the
justification: the fact is created in ExtDeps and is destroyed in ExtDeps.

| option | ExtDeps delta | verdict |
|---|---|---|
| **(a)** widen `HsmActionDispatcher.ExecuteAction` + the thunk delegate to carry `r` | signature change to `delegate*<void*,void*,HsmCommandWriter*,void>` ⇒ **ABI break** reaching every attributed method and all five thunk emitters (`BP-291`'s census) | ⛔ **rejected** — pays a full ABI break to move 4 bytes |
| **(b)** kernel writes the occurrence into the **context** it already carries | ① one new 4-byte `HsmOccurrence {ushort RegionIndex; ushort StateId;}` in `Fhsm.Kernel.Data`; ② a documented contract that `TContext` begins with it; ③ `in TContext` → `ref TContext` on `HsmKernel.Update`/`UpdateBatch`; ④ five assignments in `HsmKernelCore` | ✅ **CHOSEN.** Additive. **No delegate change, no thunk signature change** — every existing `[HsmAction]` keeps compiling |
| **(c)** kernel writes the occurrence into the **instance** header | `InstanceHeader` is `Size=16` and **fully packed** (offsets 0–15, measured) ⇒ growing it shifts `ActiveLeafIds` at offset 16 in every tier | ⛔ **rejected** — changes every instance layout to avoid a context field |
| **(d)** reuse `HistorySlots` scratch | documented "dual-purpose… simple counters/flags" | ⛔ **rejected** — overloads persistent state with a per-call value; silent corruption if a machine uses history |

**The precedent that makes (b) the house pattern, not an invention.** `HsmTraceContext*` is already
threaded from the kernel entry through `ProcessActivityPhase` into `ExecuteAction` purely to serve an
Hrot diagnostic concern (`behav-diag-1`). `HsmKernelBridge` — the struct behind `contextPtr` — is
**ours**, in `Fdp.Toolkits` (`HsmTickSystem.cs:24-36`), and already carries `Self`, `WorldHandle` and
`TraceContext*`. Option (b) adds one more field to a struct we own and four assignments to a kernel
that already threads two such pointers.

⚠ **Two honest caveats on (b).** ① `HsmKernel.Update` declares the context `in` and pins it with
`fixed`; writing through the resulting pointer violates that contract in spirit, so the entry point
changes to `ref` rather than relying on the pointer being physically writable. ② `UpdateBatch` shares
**one** context across a `Span` of instances — safe only because the kernel processes instances
sequentially on one thread. That assumption becomes a rail (§7).

### 4.3 Explicitly NOT changing

`HsmCommandWriter` · `HsmEventQueue` · the tier instance layouts · `BehaviorTreeState` ·
`HsmDefinitionBlob` · the `[HsmAction]`/`[HsmGuard]`/`[SharedAi*]` attribute shapes ·
`HsmActionDispatcher`'s tables and signatures.

---

## 5. Blast radius

75 production files touch `BrainBlackboard`, 52 touch `Blackboard1024`. They are not one problem —
they are seven, and only two are large.

| # | consumer class | files | what it needs | size |
|---|---|---|---|---|
| **1** | **squad / commander state** — `SquadCognitiveState`, `SquadInputs`, `StandardInputs`, 4 squad systems, `ForceManeuverMapper`, `ThreatMatrixAssignmentSystem`, the overlay | ~12 | ⭐ **nothing to do with occurrences.** `SquadCognitiveState.Project(ref bb)` projects the **commander's whole** `Blackboard1024` ⇒ it is entity-scoped and wants its own typed component | **S — do it first, independently** |
| **2** | **entity facts** — `CognitiveInterruptSystem`, `CognitiveCleanupSystem`, `RouteContextSystem`, `HsmTickSystem:168`, `BlackboardOffsets` | ~6 | the `BrainBlackboard` tail becomes `BrainInterrupts`. ⚠ **`R-41`/`R-39` pin byte offsets 126/127 and the param-region size — both ledger rows need updating**, not silently invalidating | **S** |
| **3** | **behaviour runtime** — `BehaviorIngressSystem`, `BehaviorRegistry`, the two tick systems, `BTreeActionRegistryFactory` | ~6 | the real work: key, allocate, resolve into the slot, tick against it | **L** |
| **4** | **generators / emitters** — `BTreeBridgeEmitCore`, `BTreeEmitCore`, `HsmBridgeEmitCore`, `BTreeBlackboardPackHelper`, `AiEmitCoreBase`, the two analyzers | ~8 | emit a slot lookup instead of a fixed component projection. **Mechanical but must be exact** — the registry key is `Method@byteOffset` | **M** |
| **5** | **blueprint compiler** — `AiPrimitiveEmitter`, `InlineActionLowering`, `CSharpEmitter`, `WorkingStateLayout`, `IrOperation`, `BlueprintCompilerContracts` | ~6 | ⭐ **this is the half the user flagged.** A blueprint reaches params two ways: as an AiPrimitive action (via the thunk — follows class 4) **and** as an Instance (already slot-resident). `FieldLayout`'s `startOffset: 0` is the one real trap | **M** |
| **6** | **editor / diagnostics / replay** — renderers, view providers, `BlackboardReflection`, `LiveBlackboardValueProvider`, `BlueprintDebugSession`, `VariableEditCommit`, `BlueprintLiveValueWriter`, `PredicateCompiler`, 2 field drawers, `SearchPredicateDto.BlackboardTarget` | ~15 | every one resolves *"where are this entity's params"* — they need the same `TryResolveOccurrence` seam, **once**, not 15 times. ⚠ `R-52`'s live corruption defect (whole-component staged write) is fixed for free by per-slot writes | **M** |
| **7** | **scenario translators** — `BrainBlackboardTranslator`, `Blackboard1024Translator` | 2 | ✅ **measured harmless.** Both are scenario/clipboard **extract-only** with `Inject` a documented no-op, over `[DataPolicy(NoScenario)]` components. **Not DDS translators** ⇒ **no wire format change, no `R-136` exposure** | **XS** |

---

## 6. Sequence

Ordered so that each step is provable on its own and the expensive irreversible one comes last.

| # | item | why here | ExtDeps |
|---|---|---|---|
| **O0** | **Re-home `BlueprintTickSystem` into a module every ECS host schedules** | independent of everything else; until it lands, blueprint Instances are editor-only and the tripod has no third leg on CGF | — |
| **O1** | **`SquadCognitiveState` gets its own component** | removes the largest non-AI consumer of `Blackboard1024`; pure win even if the rest is cancelled | — |
| **O2** | **Split `BrainBlackboard` → `BrainInterrupts` + a params region type** | the params region becomes addressable; the tail stops travelling with it. Updates `R-39`/`R-41` | — |
| **O3** | **The occurrence seam** — `OccurrenceKey`, `TryResolveOccurrence`, the slot header; rename the tiers | one lookup that classes 4/5/6 all call. **No behaviour changes yet** | — |
| **O4** | **BTree onto occurrence storage** — tree state and params into slots, **including a hosted subtree's own `BehaviorTreeState`** | ⭐ **proves the whole model with ZERO ExtDeps change** (§4.1) **and closes the shared-`BehaviorTreeState` defect in §3.1**. If this does not work, stop before paying for `O6` | **none** |
| **O5** | **Blueprint Instances take params** (`DESIGN_Parameter_Model.md` §3.3) | the slot layout is now shared with `O4`; closes `R4`, which has no design today | — |
| **O6** | **`HsmOccurrence` in the kernel** (§4.2 option b) | the one ExtDeps change, paid **once**, after `O4` has proved the storage model | **the only one** |
| **O7** | **HSM per-region actions key on the occurrence** — closes `BP-297`/`E3` | needs `O6` | — |
| **O8** | **BTree hosted under an HSM state** — the strategic/tactical composition | needs `O3`+`O6`; the child is just another occurrence | — |

⭐ **`O0`–`O5` deliver real value with no ExtDeps edit at all.** The boundary is crossed once, at
`O6`, and only after `O4` has demonstrated the model on the paradigm that needs no kernel change.

---

## 7. Rails

| rail | asserts |
|---|---|
| **two occurrences, distinct bytes** | the same asset twice on one entity ⇒ different param bytes. This is `DESIGN_Parameter_Model.md` §8's rail and it is what stops the shared-region assumption returning |
| **the tail is untouched** | resolving params for any occurrence writes neither interrupt byte nor `ExpectedThreatLevel` |
| **cursor intact** | a blueprint Instance with params keeps its `BlueprintLatentCursor` at offset 0 after a resolve — the `startOffset: 0` trap |
| **parse before commit** | a failing resolve at attach leaves the entity without the new occurrence |
| **two regions, two slots** | two concurrently-active HSM regions running the same action write **different** bytes. ⚠ `BP-297` measured that today's fixture cannot redden this — the two regions run an **empty** action. **A DTO-bound HSM action must be authored as part of `O7`, or the rail is vacuous** |
| **one context per instance** | `UpdateBatch` with N instances: each `ExecuteAction` sees the occurrence of *its* instance (§4.2 caveat ②) |
| **a hosted subtree keeps its own cursor** | host tree `Running` at node A, hosted child `Running` at node B ⇒ **both survive a tick**. ⛔ Must be written to go RED before `O4` — it reproduces the §3.1 defect in shipped code |
| **every host ticks blueprints** | a `--mode all` run shows a blueprint **Instance** advancing on CGF, not only in the editor (`O0`). ⚠ Anti-vacuity: CGF already materialises and event-attaches Instances, so the rail must assert the *tick counter advances*, not that the slot exists |
| **one supply mechanism** | unchanged from `DESIGN_Parameter_Model.md` §8 — a second `Overrides`-style applier fails it |

---

## 8. Open questions

| id | question | lean |
|---|---|---|
| **Q1** | ~~Does `NodeType.Subtree` come with this?~~ **WITHDRAWN — the question was wrong.** See §3.1 | ✅ **BTree-hosts-BTree SHIPS and is KEPT.** `O4` improves it |
| **Q2** | HSM region capacity: **2** (`HsmInstance64`) / **4** (`HsmInstance128`), with a **single shared event queue** per instance | raise only if a real machine needs it. ⚠ the shared queue is unmeasured for cross-region hazards — measure before `O8`, not before `O6` |
| **Q3** | Rename `BlueprintBlackboard*` → `Occurrence*`, given it already stores BTree/HSM state | **yes, but via Roslyn and not on the critical path** — the name is actively misleading now |
| **Q4** | ~~Does the root brain stay exclusive once nesting works?~~ | ✅ **RULED, user, `2026-09-19`, verbatim: *"there is still just up to one assignable behavior per entity."*** `BehaviorState` stays singular; `InstanceId` preemption and `ChannelArbitrationSystem` are untouched. Concurrency comes from nesting, never from a second assignable root |
