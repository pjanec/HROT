# Hill-Attack → Blueprints — Migration Log

> **Goal:** rebuild the Platoon Hill-attack behavior, step by step, as visually-authored
> **blueprints** under a new name — discovering (and filling) the missing blueprint capabilities
> progressively. **Not a port; a rebuild.** The C# original stays untouched as the oracle.
> **Working name:** `HillAssault2` (commander) / `HullDownRun2` (tank) — rename freely.

## What "blueprintize" actually means here

The Hill-attack is **two cooperating BTrees**, and their *topology is already JSON-authored*. What is
still hardcoded is the **node logic** (C# action/condition methods) and the param / working-state
structs. So the migration re-expresses those C# node methods as `.bp.json` blueprint graphs, hosted
back into a BTree — it does **not** re-draw the tree shape.

**Headless workflow (no editor / Windows needed):** author `.bp.json` by hand + a proof test that
compiles and runs it, asserting behavior against the C# oracle — mirroring `SharedStateRallyDemo.bp.json`
+ the T35–T38 proof tests. This is how every slice below is built and verified.

## Oracle files (ground truth)

- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` (commander logic + tree builder)
- `.../Brains/HillAttackTankNodes.cs` (tank logic + tree builder)
- `.../Brains/HillAttackDtos.cs` (`PlatoonHillAttackParams`, `HillAttackMutableState`, `HullDownAttackParams`)
- `Assets/BTrees/PlatoonHillAttack.btree.json`, `.../HullDownAttackRun*.btree.json` (topology)
- Node vocabulary: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`

## Slice order (simplest → hardest)

| # | Slice | Node kinds needed | Notes |
|---|-------|-------------------|-------|
| 0 | **Warm-up (synthetic)** — trivial action, e.g. always-Success / a single MoveTo + WaitForChannel | `EventEntry`, `ChannelCommand`, `WaitForChannel`, `Return` | shakes out author→compile→run headlessly before touching real logic |
| 1 | `Condition_HasTarget` | `FunctionCall`(pure TargetMemory scan), `Branch`/`Return` | read-only; the recommended first *real* slice |
| 2 | `Action_ReverseToBaseline` | `ChannelCommand`(Locomotion), `WaitForChannel`, `Return` | + `ClearBehaviorEvent` on terminal (see GAP-3) |
| 3 | `Action_AimAndFireSpecific` | `ChannelCommand`(Weapon) + round-count working state | ammo-drop counting → `SetVariable` (GAP-6) |
| 4 | `Condition_AreAllAtBaseline` | roster iterate + per-subordinate component read | hits GAP-1 (loop) + GAP-2 (foreign component read) |
| 5 | `Action_CreepToAndBeyondSlot` | two-phase move + overshoot geometry | needs working-state phase var |
| 6 | `Action_CalculateSegments` / `Action_DispatchAllToBaseline` | roster fan-out + N event publishes | GAP-1, GAP-3, GAP-4 |
| 7 | EQS loop (`RequestAreaQuery` + `IsAreaQueryResolved`) | `SpawnEqsSensor`, `ReadEqsResult` | async batch |
| 8 | `Action_DispatchWaveWithTargets` + `Condition_IsWaveCompleted` | slot alloc, wave parity, SoA tracking, per-death burn, behavior-hash poll | the hard core — GAP-1/2/4/5 all at once |

## Candidate missing pieces (confirm as we hit them)

Flagged by recon; each is *suspected* until a slice proves it. Progressive discovery — confirm,
then decide workaround vs build-the-node.

| ID | Gap | Severity | First hit | Likely workaround |
|----|-----|----------|-----------|-------------------|
| **GAP-1** | **No loop / iteration node** (Repeater / ForEach / While) — no counted iteration over a roster or a wave | **high** (biggest) | slice 4 | none clean; may need a new node kind, or lean on squad primitives |
| **GAP-2** | **Read a *foreign* entity's ECS component** (commander polling each subordinate's `BehaviorState` / `NavigationStatus`) — `GetShared` covers shared *slots*, not arbitrary components | high | slice 4 | `FunctionCall` helper taking the target Entity? confirm |
| **GAP-3** | **Publish arbitrary engine events** (`AssignTacticalIntentEvent`, `ClearBehaviorEvent`) — `ChannelCommand` only targets the 3 CQRS channels | med | slice 2/6 | verify `BuiltInEngineEventCatalog` entries; else add them |
| **GAP-4** | **Roster fan-out / N orders** — no node iterates `UnitRoster` + publishes per-subordinate | med | slice 6 | squad primitives (`PartitionElements`/`AssignRoles`/`AcquireSlot`/`AdvancePhase`) may cover — verify |
| **GAP-5** | **Bitmask + `fixed`-array SoA working state** — exceeds demoed single-`int` shared struct; no array-set / bit-op vocabulary | med | slice 8 | `FunctionCall` helpers (the `SquadRallyStateOps` escape hatch) — expressible but not visually native |
| **GAP-6** | **In-place "param" mutation** (`RoundsFired`, `LastObservedAmmo`) — blueprints split Input vs State | low | slice 3 | migrate to working-state var (`SetVariable`) — a refactor, not a gap |

Confirmed gaps graduate to tasks + entries in `Blueprint_Authoring_UX_Backlog.md`.

## Safety-net findings (2026-07-16) — several candidates now CONFIRMED

The day-1 node safety net (`NodeCoverageTests` / `SchemaReflectionTests`) immediately proved that
some nodes we hoped to lean on are **unimplemented no-ops** — they have no `Stage5_Schedule`
lowering and fall through to a `default:` branch. Verified directly in
`Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`.

| Node(s) | Symptom | Reshapes |
|---------|---------|----------|
| `PartitionElements`, `AssignRoles`, `AdvancePhase`, `AcquireSlot` (squad primitives) | `default:` → **BP4004 warning, node dropped**, no IR | **GAP-4** — the hoped "squad primitives cover roster fan-out" workaround is **dead**; either implement their lowering or use `FunctionCall` helpers |
| `CallEventDispatcher`, `BindEventDispatcher` | `default:` → BP4004 warning, dropped | **GAP-3** — event-publish via dispatchers is a no-op today |
| `ArrayMake`, `ArrayGet` | pure `default:` → **silent `default` value, NO diagnostic** (worst kind) | **GAP-5** — array working state silently broken |
| `WaitForEventNode` | short `EventTypeId` passes BP1402 validation but fails Roslyn (`CS0400`); FQN fails validation — **no value satisfies both** | latent event-wait unusable until FQN resolution added (mirror `WaitForChannelNode.ResolveChannelTypeFqn`) |

**Plan impact:** slices **0–3 are unaffected** (they use only lowered nodes: `EventEntry`,
`ChannelCommand`, `WaitForChannel`, `Return`, `FunctionCall`, `Branch`). The unimplemented nodes
first bite at slice 4+ (roster/loop) and slice 6+ (fan-out, events). We decide *per slice* when we
reach them: implement the node's lowering, or route around it with a `FunctionCall` helper (the
`SquadRallyStateOps` escape hatch). Tracked as task #25.

## Slice-1 finding (2026-07-16) — GAP-7: no ECS-read / no `self`/`world` in graphs **[foundational, blocks all conditions]**

Confirmed while scoping `Condition_HasTarget`. Blueprint graphs can read only their **own blackboard**
(Params / WorkingState / Variables via `GetVariable`) plus **implicit-`self` accessor nodes**
(`ChannelCommand`, `GetShared`/`SetShared` — these have `self`/`world` wired in by the emitter). There
is **no** way to:
- read an arbitrary **component** on `self` (e.g. `TargetMemory`, `NavigationStatus`, `BehaviorState`),
- read a **world singleton** (e.g. `NetworkEntityMap`),
- obtain **`self`** or **`world`** as a data value to pass into a `FunctionCall` helper.

Evidence: `SharedStateCrossEntityDemo` gets its target `Entity` from a host-pre-populated
`WorkingState.Commander` variable via `GetVariable` — NOT from any ECS read. `Nodes.cs` has no
`Self`/`World`/`GetComponent`/`GetSingleton` node. `FunctionCall` args resolve only from blackboard /
literals / other pure-node outputs.

**Consequence:** the Hill-attack **condition family is unexpressible today** — `Condition_HasTarget`,
`Condition_AreAllAtBaseline`, `Condition_IsWaveCompleted`, `Condition_IsAreaQueryResolved` all read
ECS state. Even the `SquadRallyStateOps`-style `FunctionCall` escape hatch fails, because the helper
can't be handed `self`/`world`.

**The blueprint vocabulary is rich for OUTPUT (channel commands, shared-state writes, variable sets)
but poor for INPUT (reading arbitrary ECS state).** Filling this is the single highest-leverage
capability for the whole migration.

**Options (needs a decision — task #26):**
1. A **context-aware `FunctionCall`** — a flag/variant whose callee implicitly receives the ambient
   `self`/`world` (lowers to `Helper(args…, self, world)`). Smallest change; reuses the escape hatch;
   keeps ECS logic in reviewable C#. **Recommended first step.**
2. Dedicated **`GetComponent<T>(self)`** / **`GetSingleton<T>()`** source nodes (+ the foreign-entity
   form → subsumes GAP-2). More "visually native" but a bigger build (new node kinds, pin typing,
   validation, editor).
3. `Self` / `World` source nodes feeding existing `FunctionCall` pins — minimal vocabulary, but exposes
   raw `EntityRepository` in graphs (leakier).

Until decided, the migration proceeds only on **output-shaped** slices (command actions via
`ChannelCommand`) — the condition family is parked behind this capability.
