# Architect question #5 — the next capability tier for the Hill-attack rebuild

**Context.** The read/condition family is now proven end-to-end through the *real* MSBuild generator:
P7/P7.1 (context-aware FunctionCall), slice 1 (`HasTarget`), and **P2 `GetComponent`** (visually-native,
reflection-free ECS component field read; optional cross-entity Target pin → subsumes GAP-2). A recon
of the remaining Hill-attack oracle nodes shows **the cheap wins are done** — every remaining slice needs
a *new generic capability*, and four of them hinge on a design decision we'd rather get right than guess.
All four are "build a generic hardcoded node/helper" per your earlier steer; the questions are about shape.

## Where each remaining oracle node is blocked

| Oracle node | Needs | Gap |
|---|---|---|
| `Action_ReverseToBaseline` | MoveTo channel cmd **+ publish `ClearBehaviorEvent`** | **GAP-3** (event publish) |
| `Action_AimAndFireSpecific` | Weapon channel cmd + **ammo read** + **round-count state** + target resolve | GAP-3-adjacent, **GAP-11** (params), reads=P2 ✓ |
| `Condition_AreAllAtBaseline` | **foreach subordinate** → read `NavigationStatus` → AND-reduce | **GAP-1** (loop) + P2 ✓ |
| `Action_DispatchAllToBaseline` / `CalculateSegments` | roster **fan-out** + N event publishes | GAP-1 + GAP-3 |
| `Action_DispatchWaveWithTargets` | slot alloc + wave parity + SoA + per-death burn | GAP-1/3/5 (hard core) |

## Q-A — how does an **AiPrimitive** blueprint publish an engine event? (GAP-3)

The only publish op in the IR (`IrOp_PublishEvent`) emits `ecb.PublishEvent(new global::T{…})`. But `ecb`
(`IEntityCommandBuffer`) is only in scope for **Instance** dispatch — the **AiPrimitive** `TickCore`
signature is `(ref Params, ref WorkingState, Entity self, EntityRepository world, float time)`, **no
`ecb`**. Meanwhile the oracle publishes via `ctx.World.Bus.Publish(new ClearBehaviorEvent{ Entity=self })`
— a world **event bus**, not the command buffer. Since Hill-attack blueprints are all AiPrimitive
(BTree-hosted Conditions/Actions), which is the intended path?
1. **`world.Bus.Publish(...)`** — matches the oracle, `world` is already in scope; add an
   `IrOp_PublishBusEvent` + a `PublishEvent` node lowering to it for AiPrimitive dispatch. *(smallest;
   our lean guess)*
2. Thread **`ecb`** into the AiPrimitive `TickCore` signature (bigger; touches the dispatch ABI).
3. A curated **`[SharedAiAction]` helper** via FunctionCall (keeps it in reviewable C#, no new node).

Is `world.Bus.Publish` the sanctioned engine-event path from a Brain-tier AiPrimitive, or is the ECB the
only legitimate mutation channel and Bus.Publish an oracle shortcut we should NOT replicate?

## Q-B — component **writes**: ChannelCommand-only, or a generic write node?

The oracle's actions do raw `GetComponentRW<WeaponChannel>` / `<LocomotionChannel>` and hand-write
`ActiveAction`/params/`ActionInstanceId`. In blueprint-world that's exactly what the **`ChannelCommand`**
node does (it's lowered). So our plan is: **rebuild all channel writes as `ChannelCommand`, and expose NO
generic "set arbitrary component field" node** — keeping the CQRS invariant (Brains write only through the
3 channels). Do you agree, or is there a legitimate need for a generic component-write node (e.g.
`BehaviorState.InstanceId` sync, which the oracle writes directly)? If the latter, should it also route
through a curated surface rather than raw `GetComponentRW`?

## Q-C — `FlowForEach` (the loop, GAP-1, "the biggest")

Slice 4 (`AreAllAtBaseline`) needs: *for each subordinate in the roster, read a component (P2 ✓), reduce*.
You already blessed "loop as a structured bounded latent-free foreach." Two design points:
1. **Iteration source.** The roster is a `UnitRoster`-style component holding a fixed-capacity entity
   array (SoA). Do we expose a curated **"roster/collection source"** (a node that yields the component's
   entity array as the loop's iterable, cap-bounded), mirroring how EQS/catalog surfaces are curated?
2. **Lowering.** The scheduler is a BFS basic-block model that already emits `goto __block_*` labels. A
   bounded, latent-free foreach can lower to an **inline C# `for` over `[0, Count)`** with the body
   statements inlined (no back-edge through the block scheduler, no per-iteration latent state). Is inline
   `for` the shape you want, or do you want it to reuse the block/back-edge machinery (needed only if a
   loop body may ever contain a latent `WaitForChannel`)? Our lean read: **inline `for`, body must be
   latent-free** (validated), which covers every Hill-attack loop.

## Q-D — Params: migrate-to-WorkingState, or make Params graph-readable? (GAP-11)

Blueprint graphs currently can't read a declared **Parameter** (`GetVariable` consults only
Variables/WorkingState; `IrOp_ReadParam` exists but Stage5 never produces it). Slice 1 worked around this
by putting `TargetNetworkId` in WorkingState. Round-counting (`RoundsFired`/`LastObservedAmmo`) is the
same story. Is the intended authoring model "**inputs are WorkingState/Variables; Params are the
host-BTree wiring surface only**" (i.e. the workaround is actually the design), or should we close GAP-11
by wiring a `GetParameter` path (produce `IrOp_ReadParam`)? This decides whether GAP-11 is a bug or a
non-issue.

---

**Our lean defaults if you're happy with them:** A-1 (`world.Bus.Publish`), B-agree (ChannelCommand-only,
no generic write), C-inline-`for` + curated roster source + latent-free body, D — expose `GetParameter`
(close GAP-11) so authors aren't forced to misuse WorkingState. We'll proceed on these unless you redirect.
