# Blueprint AiPrimitive Shared Working-State — Design (`GetShared`)

> **Status:** DESIGN-ONLY (2026-07-15). No code written. Architect-reviewed (Q1–Q5 below).
> **Scope:** how a blueprint-authored AiPrimitive (BTree/HSM host node) accesses working state **beyond its single projected `WorkingState` struct** — both (a) sharing state between nodes, and (b) a single node reaching a *second* shared slot / another entity's slot.
> **Audience:** engineers implementing the shared-state slices; reviewers.
> **Related:** `BTree_AiActionParameterBinding_Detailed_Design.md` §4.4 (scoped working state — the canonical source), `BTree_AiActionParameterBinding_Detailed_Design_Status.md`, `Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` (Mode-1 vs Mode-2), `Blueprint_Subsystem_Slice2_Candidates.md`.

---

## 1. Problem

A blueprint AiPrimitive generates one `TickCore(ref Params, ref WorkingState, Entity, EntityRepository, float)` with **exactly one** `WorkingState` struct. That single slot is a ceiling for two distinct needs:

1. **Cross-node sharing** — several nodes/behaviors on one entity coordinating through shared memory (e.g. a shared cursor or accumulator).
2. **Single-node multi-slot** — one node holding *private* state (a local timer) **and** reading/writing a *separate shared* slot (a squad plan) in the same tick.

These are different: (1) is a scoping choice on the one WorkingState; (2) genuinely needs a *second* slot reachable from one node. The design splits them across two slices.

## 2. What already exists (reuse, do not reinvent)

Byte-matched between emitter and runtime:

- **Scope-keyed slot math** — `BTreeBridgeEmitCore.ComputeStatefulSlotKey(Guid assetId, WorkingStateScope scope, Guid nodeVisualId, string variableId)` and its runtime twin `StatefulBTreeActionBinder.ComputeStatefulSlotKey(...)` (FNV-1a-32, `FnvOffsetBasis=2166136261`, `FnvPrime=16777619`, masked `& 0x7FFFFFFF`):
  - **Node:** `FNV(assetId ++ nodeVisualId)` — per-node private.
  - **Behavior:** `FNV(assetId ++ variableId)` — shared by co-bound nodes in one asset.
  - **Entity:** `FNV(variableId)` — assetId excluded, so the slot survives a behavior switch (cross-behavior / cross-entity).
- **Scope resolution** — `ResolveStatefulSlotKey(dto, targetField, nodeVisualId)` reads the *host* blackboard variable bound to `targetField` where `Role == State`, takes its `Scope`, else `Node`. `StatefulScopeVariable(p)` prefers `WorkingStateTargetField` over `ExpressionTargetField`.
- **Scope/role model** — `WorkingStateScope { Node, Behavior, Entity }` and `BlackboardVariableRole { Input, State }` on the **host** BTree/HSM blackboard variable (`BlackboardVariableDto` / `BlackboardVariableEntry`), authored in the BTree blackboard variables panel (`VariablesPanelControl` / `BlackboardAuthoringWindow`). Runtime twin `StatefulSlotScope : byte`.
- **Per-entity provisioning** — `StatefulSlotInfo` manifest on `BehaviorDefinition.StatefulWorkingSlots`; `BehaviorIngressSystem.ProvisionStatefulSlots` (Input phase) attaches each `SlotKey` into the entity's `BlueprintBlackboard{1024,4096,16384}` tier on `AssignBehaviorEvent`; detached on switch / `ClearBehaviorEvent`.
- **Lookup** — `BlueprintBlackboardPartitions.TryGetSlotOffset(byte* memory, int slotKey, out int offset)` — a **linear scan of only the slots already attached to this entity**. There is **no** lazy/on-demand allocation on the read path.

**Aspirational (no code):** the Mode-1 two-`ref`-state delegate shape, and the entire `GetShared`/`GetSharedRW` accessor.

## 3. Architect decisions (2026-07-15)

- **Q1 — Slot key is by NAMED `variableId`, not by type.** Type-keying was rejected (two same-typed variables would collide). Accessor signature carries the name: `GetShared<T>(Entity, WorkingStateScope, variableId)`. All keys stay compile-time constants.
- **Q2 — No blueprint-model change.** `Role`/`Scope` live **only** on the host BTree/HSM blackboard variable. A blueprint declares its WorkingState as ordinary scope-agnostic fields; the host node binds it via `WorkingStateTargetField` to a scoped host variable, and the adapter keys the slot by the **host** variable's scope. No `SharedStateDecl`, no scope on blueprint `VariableDecl`.
- **Q3 — `Blackboard1024` stays untouched.** Self-hosted blueprint thunks keep the legacy `Blackboard1024 + 8` WorkingState offset; only composed/shared nodes use the partitioned `BlueprintBlackboard*` tiers. Adding a partition allocator to `Blackboard1024` is explicitly out of scope.
- **Q4 — Slice 1 = Behavior scope, same-behavior only.** Race-free by construction (one entity, sequential ticks). Entity/cross-entity reintroduce multi-writer hazards and are deferred.
- **Q5 — Slice 2 cross-entity contract:** owner (e.g. commander) provisions the Entity-scoped slot on itself in the Input phase; members only read, supplying the target `Entity` via a graph pin; ≤1-frame latency by fixed tick order; `TryGetShared`→bool for the not-ready case (member ticks before owner's assignment processes) — never a throwing hard `ref`.

## 4. Slice 1 — Behavior-scoped sharing (race-free MVP)

**Goal:** several composed AiPrimitive nodes on one entity share one WorkingState slot, chosen by scoping the host variable. Solves *cross-node sharing* (§1.1). Does **not** give one node two slots.

**Gap today:** `ComposeAiPrimitive{Action,Condition}` (Phase A/E2) auto-creates only a `bpParams` (Input) variable and never sets `WorkingStateTargetField`; so `StatefulScopeVariable` falls back to the Params var (Input role) → `ResolveStatefulSlotKey` yields `Node` scope always. The composed WorkingState is therefore permanently private.

**Work:**
1. On placement, also create a **WorkingState host variable** (`Role = State`, typed as the blueprint's generated `+WorkingState`) and set the node's `WorkingStateTargetField` to it — so its scope is authorable and distinct from the Params variable.
2. Author scope via the existing Role/Scope panel: `Node` (default, private) or `Behavior` (shared).
3. Co-binding: two composed nodes pointing `WorkingStateTargetField` at the same `Behavior`-scoped variable resolve (via `ResolveStatefulSlotKey`) to the **same** slot key → shared memory. `EmitStatefulWorkingSlotsArray` already dedups by slot key, so one manifest entry provisions the shared slot.
4. **Proof (T35-style):** two composed nodes incrementing a shared `Behavior`-scoped counter; assert both see the running total, and a `Node`-scoped control node stays isolated.

**No runtime changes** — reuses `ComputeStatefulSlotKey(Behavior)` + provisioning + `TryGetSlotOffset` verbatim. This is editor-composition + emit wiring only.

## 5. Slice 2 — Mode-2 accessor (`GetShared`) + cross-entity

**Goal:** a single node reaches a *second* slot — its private WorkingState **plus** a shared slot, and cross-entity (member reads commander). This is the capability behind "one WorkingState isn't enough."

**Shape:**
- **Runtime accessor** (in the behavior/partition runtime layer): `bool TryGetShared<T>(Entity e, WorkingStateScope scope, string variableId, out /*ref*/ T)` (+ a RW form), computing `ComputeStatefulSlotKey(assetId?, scope, _, variableId)` and probing tiers 16384→4096→1024 via `TryGetSlotOffset`. Returns false when the target entity hasn't provisioned the slot (not-ready), never throws. Entity-scope key omits assetId (§2), so a member and the owner agree on the key from `variableId` alone.
- **Blueprint graph nodes** `GetSharedNode`/`SetSharedNode`, mirroring `GetVariableNode`/`SetVariableNode` end-to-end: `Nodes.cs` `[JsonDerivedType]` + subclass (carrying `variableId`, target type, `scope`, and — cross-entity — a target-`Entity` input pin) → `BuiltInNodeRegistry` pin schema → `Stage0_Rehydrate` enrichment → `Stage5_Schedule` new `IrOp_ReadShared`/`IrOp_WriteShared` lowering to the partition-slot lookup (not the blueprint-local variable store).
- **Provisioning:** owner's behavior declares the Entity-scoped State variable → its `StatefulWorkingSlots` manifest provisions it on the owner. Members never provision another entity's slot (respects the no-mid-tick-structural-change rule).

**Safeguards (Slice 2):** debug-build "second distinct writer this tick" assertion; coordinator-writes/members-read convention; cross-entity dispatcher calls (validator-error in Slice 1) routed via deferred events.

## 6. Invariants

- **Named-`variableId` keying** — never type-only (collision safety, Q1). Both provisioner and reader must agree on the `variableId` string (a compile-time constant on each side).
- **Scope is host-side only** (Q2) — the blueprint stays memory-topology-agnostic.
- **`Blackboard1024` unmodified** (Q3) — shared/composed state lives exclusively in `BlueprintBlackboard*` partition tiers.
- **Owner-provisions, readers-get-not-ready** (Q5) — the read path can only see already-attached slots; `TryGetShared` returns false otherwise.
- **Slice-1 is race-free** (Q4) — Behavior scope, one entity, sequential ticks; broader scopes gated behind Slice-2 safeguards.

## 7. Deferred / open

- Migrating self-hosted thunks off `Blackboard1024 + 8` onto the partition rail (Q3 says not now — its own cleanup if ever).
- Mode-1 two-`ref`-state delegate shape (aspirational; the accessor supersedes the need for blueprints).
- Group/squad scope is *not* a separate scope — it is an `Entity`-scoped slot on the coordinator, read by members via the Slice-2 accessor.
