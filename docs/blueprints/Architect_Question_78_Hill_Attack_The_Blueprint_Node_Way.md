<!--STATUS
state: LIVE
updated: 2026-09-30
build-state: DESIGN — every sub-question carries a lean; nothing is approved yet.
current-answer: §3, the decisions, each with a lean. §1 is the inventory and §2 the claim table.
stale-below: nothing.
known-rot: none.
known-conflict: HillAssault_Blueprint_Migration.md §"What blueprintize means" keeps the BTree topology and turns only the
  node logic into blueprints. Decision A here departs from that on purpose; the reason is in A.
related-designs:
  - HillAssault_Blueprint_Migration.md — owns the July rebuild log and its GAP-1..GAP-10 list, part of which is now
    out of date (§1 here re-measures it).
  - Architect_Question_77_Blueprint_As_A_Behaviour.md — owns the blueprint-behaviour runtime this rebuild runs on
    (a Tick graph with its own block, latent waits, Return finishes it).
  - Architect_Question_76_One_Blackboard_Block_Per_Primitive.md — owns "one block per running behaviour" (R-151).
  - DESIGN_Parameter_Model.md — §P.9 owns "a leaf reads its host live"; no node writes a Parameter. That is why A leans
    away from blueprint leaves.
  - docs/designs/hill-attack/DESIGN.md — owns the doctrine itself (phases, slots, waves). Its behaviour is the spec;
    CE-460's known quirk is part of it (WON'T FIX).
  - Blueprint_Fixed_Collections_Design.md — owns the fixed-list variable and the curated-accessor collection nodes (C4).
-->

# Architect Question #78 — the hill attack, the blueprint-node way (`CE-464`)

> 🔒 **User, `2026-09-30`:** *"The C# helpers for basically everything is exactly what i found non elegant. The goal was
> making it the blueprint node way."* · earlier the same day: *"lets leave its old behavior as is. What is important is
> that it keeps working as before."*

**Goal.** Rebuild the platoon hill attack commander as a blueprint whose **logic is nodes**. Hill-attack-specific C#
helpers are not allowed. The C# `PlatoonHillAttack` stays the live doctrine and becomes the reference the blueprint
version is compared against.

## 1. INVENTORY *(`2026-09-30`)*

| query | total | result |
|---|---|---|
| `search_graph name_pattern=".*(Ops\|Math\|IntentJson)$" label=Class file=*Hrot.AI.Behaviors/Brains/*` | **18** | 15 hill-attack helpers + `UnitRosterOps`, `BpCollectionDemoOps`, `BpFixedListDemoOps` (the last two are demos) |
| `search_graph name_pattern=".*Node$" label=Class file=*Nodes.cs` | **54** | the node classes; 48 serialised kinds, all lowered by `Stage5_Schedule` except the 4 squad primitives, `ArrayMake/ArrayGet`, the dispatcher pair |
| node census of the 15 `HillAssault2_*.bp.json` twins (script) | **191 nodes** | FunctionCall **44** · SetVariable 35 · Literal 33 · Return 25 · Branch 21 · GetVariable 19 · Compare 13 · PublishEvent 6 · FlowForEach 4 · GetComponent 3 · BinaryOp 3 |
| distinct helper methods called | **28** | listed in §2 |
| grep `BlueprintCallable` outside `Brains/` | — | ⭐ **`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintMath.cs`**: a general-purpose library that already has `Lerp`, `Clamp`, `Min/Max`, int and float arithmetic |

⚠ `check_index_coverage` is not available through the CLI, so the counts above are what the graph and grep returned,
not a proven complete set.

## 2. Claim table — the 28 helper calls, sorted by what replaces them

| helper call(s) | code (how it is) | replaced by | design basis |
|---|---|---|---|
| `SegmentMath.Lerp` ×6, `LerpParam` ×3, `TotalSlots` | `SegmentMath.cs:13-55`: plain arithmetic | ✅ `BlueprintMath.Lerp` (**already exists**) + `BinaryOp` + `Compare` | seam law: a duplicate of `BlueprintMath` |
| `VectorOps.Vec3` | `new Vector3(x,y,z)` | ✅ `MakeStruct` | — |
| `WorldOps.IsNull`, `WaveParityOps.*`, `WaveDispatchOps.ShouldConsider` | compare to `Entity.Null`; `index % 2 == wave` | ✅ `Compare` + `BinaryOp(Modulo)` (+ is-alive, C2) | — |
| `SlotOps.PickClosestBaselineSlot` | two loops over slots, min distance | ✅ a loop + variables, or a **blueprint** function/macro | — |
| `WaveMonitorOps.Update` | loop over runners, read each one's `BehaviorState`, remove dead or finished | ✅ loop + `GetComponent` on a target entity (`GetComponentTargetDemo` has a `Target` pin) + list write | `Blueprint_Fixed_Collections_Design.md` |
| `MemberSlotListOps.*` ×5 | four parallel fixed arrays (SoA), capacity 8 | ✅ **a fixed-list variable of a `Runner` struct** (`ListWriteNode`, Q19's `BlackboardFixedList`) | `Architect_Question_19` |
| `MaskOps.WithBitSet` ×3 | `mask \| (1 << i)` | ⛔ no bit operators (`ArithmeticOperator` = Add/Subtract/Multiply/Divide/Modulo, `Nodes.cs:465`) → **C1** | searched `docs/`, no ruling |
| `WorldOps.SimTime`, `WorldOps.IsAlive` | downcast `view` to `EntityRepository` | ⛔ no node → **C2** | — |
| `NetworkEntityMapOps.ResolveTarget`, `TargetPoolOps.ResolveNetId`, `HillAssault2TankOps.HasTarget` | read a managed **singleton** (`NetworkEntityMap`) | ⛔ no singleton read → **C2** | migration doc GAP-10, still open |
| `SlotOps.PickRandomFreeSlot` | `SimRng.FromSim(self, wave, simTime)`, deterministic | ⛔ no random node → **C3** | — |
| `AreaQueryBatchOps.*` ×8 | `AreaQueryBatchHelper` (`Fdp.Toolkits/Spatial/Eqs`): request event + 64-slot ring buffer | ⚠ **a different pipeline** from the built-in `SpawnEqsSensor`/`ReadEqsResult` (template-based sensor) → **D1** | ⛔ not measured whether the sensor can answer "hostiles in area" |
| `MoveIntentJson.Build`, `HullDownIntentJson.Build` | a DTO serialised to a string; `AssignTacticalIntentEvent` carries `JsonParams: string` (`BuiltInEngineEventCatalog.cs:218`) | ⛔ `MakeStruct` exists, **struct → JSON** does not → **D2** | — |
| `FlowForEach` over `UnitRoster` | needs `UnitRosterOps.Count/Subordinate` (curated C# accessors) | ⚠ **every collection node needs curated C# accessors** → **C4** | `Blueprint_Fixed_Collections_Design.md` (`CuratedStatic`) |

⇒ About half the helper calls can be replaced with nodes that exist today. The rest need **six generic capabilities**
(C1–C4, D1, D2). None of them is hill-attack-specific.

## 3. Decisions — each with a lean

### A — Shape: one blueprint behaviour, or a behaviour tree with blueprint leaves?

- **A1 — ONE blueprint behaviour** (Q77's `Dispatch = Behavior`): one Tick graph. The phases (segments → staging → waves
  → return) are an enum variable plus `Branch`, with latent waits (`Delay`, `When`) where the doctrine waits. All state
  lives in its own block.
- **A2 — keep the BTree, and make each leaf a blueprint** (the migration doc's original intent).
- **Lean: A1.** Measured: the commander's leaves **write** shared doctrine state (segments, wave number, slot masks,
  the runner list). A blueprint leaf **reads** its host's `Params` live (`DESIGN_Parameter_Model` §P.9, `CE-444`), and
  the thunk even passes them by `ref` (`AiPrimitiveEmitter.EmitLiveHostParams`: `ref var p = ref *(Params*)(__root +
  __at)`). But **no node writes a Parameter**: `GetParameter` exists, and `SetVariable` targets only the leaf's own
  variables (`Nodes.cs:196`). So A2 needs a new "write my host's parameter" node, which makes leaves write shared host
  bytes. That is the hazard the user named on `2026-09-21` (*"two actions running in two hsm regions would overwrite the
  params"*, §P "the ruling that forced it"). The last version that worked around it (`PlatoonHillAttack2` plus
  GetShared/SetShared) was deleted by `CE-436`.
  **What would change the lean:** if the user wants the tree picture kept for readability, A2 is possible, but only
  after a ruling that a leaf may write its host's parameters.

### B — What "no C# helpers" means precisely

- **B1** — zero C# calls of any kind. **B2** — only *general-purpose* library functions are allowed (like `BlueprintMath`,
  kept in a shared toolkit library), never doctrine-specific ones. **B3** — status quo.
- **Lean: B2**, enforced by a rail: *no blueprint under `Assets/` calls a type in `Hrot.AI.Behaviors/Brains/`*.
  B1 would force re-inventing arithmetic as nodes; the engine's own standard library is the node palette.

### C — The four missing general-purpose capabilities

| | capability | lean | blast radius |
|---|---|---|---|
| **C1** | bit operations | ⭐ **model slots as a fixed list of a slot-state enum (Free/Burned/Reserved) instead of a bitmask.** It reads better and needs no bit maths. Round-out: add `BitAnd/BitOr/BitXor/ShiftLeft/ShiftRight` to `ArithmeticOperator` for integer types anyway (cheap, generic) | compiler: one enum + lowering |
| **C2** | world reads: sim time, is-alive, **singleton read** | library functions `SimTime`, `IsAlive(entity)` in the shared library; a **`GetSingleton`** node (the migration doc's GAP-10 / "P3") plus a library `ResolveNetworkId(long) → Entity` in the network toolkit | ⚠ if `ISimulationView` needs a singleton accessor, that touches **Fdp.Core, which belongs to the backend lane** → STOP and report. Otherwise keep the downcast inside the shared library, once |
| **C3** | deterministic random | library `RandomInt(self, key, min, max)` built on `SimRng.FromSim`, so replay stays deterministic | toolkit only |
| **C4** | collection loops without per-collection C# accessors | let the compiler read an `[InlineArray]` / fixed-buffer **field** directly (as the `ManagedMember` mode already does for managed collections) | compiler; ⚠ **the reason `CuratedStatic` was chosen is NOT yet read**. The session must read `Blueprint_Fixed_Collections_Design.md` + Q20 before deciding. If the reason still holds, keep the accessors but put them in the shared library, one per engine collection type, never per doctrine |

### D — The two engine pipelines the graph must reach

| | | lean |
|---|---|---|
| **D1** | the area query | measure first whether the EQS sensor nodes can answer "hostile units in an area". If they can, use them. If not, add **request / is-ready / read / free** nodes over `AreaQueryBatchHelper` as a generic EQS family, not as hill-attack helpers |
| **D2** | the intent's JSON payload | a generic **`ToJson`** node: baked struct FQN in, string out, the `MakeStruct` pattern. ⚖️ Alternative: a typed intent event. Rejected because it changes a wire contract the C# doctrine also uses |

### E — Scope, reference and proof

- **Commander only** (`PlatoonHillAttack`). The members keep running the C# `HullDownAttackRun`; that is a later
  question.
- **The C# doctrine is the reference, quirks included** (`CE-460` WON'T FIX): same scenario, same outcomes. Proof:
  `hill-attack-close` in `SimHost` (the suite `HillAttackIntegrationTests` already uses) plus one live
  `ClusterRunner --mode all` run. Both targets destroyed, platoon back on its baseline.
- **The 15 `HillAssault2_*` twins:** keep them until the rebuild's proofs cover their node types, then retire them with
  coverage re-pointed (`R-137`). This is not a rush removal.

## 4. Suggested order

① C1 round-out + C2 + C3 (small, generic, independent) → ② C4 and D1 (each needs its own measurement first) → ③ D2 →
④ author the behaviour (A1) → ⑤ the proof (E). Each generic node gets its own rail in its feature's suite.
