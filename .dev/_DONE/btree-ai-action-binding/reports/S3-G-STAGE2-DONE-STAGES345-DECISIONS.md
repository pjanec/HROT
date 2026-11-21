# S3-G — stage 2 landed; stages 3–5 need two decisions before touching production combat AI

> **✅ RESOLVED & COMPLETE (2026-07-13).** The user chose **1a + 2a**. All stages are implemented,
> verified, and pushed on `claude/hill-attack-json-slice-3-7fbaf4`:
> stage 2 `85dea39`, stages 3+4 `e268a33` (landed together — compile-coupled as predicted below),
> stage 5 `736b7b0`. Decision 1 → **1a** (emitter emits a 5-param slot-projecting stateful deactivator
> registered under the full `@offset@slotKey` key). Decision 2 → **2a** (factory reuses the generated
> `PlatoonHillAttackRegistrar` via a throwaway registry, then registers under the stable id 3014 with the
> geo `ParseParams`). Gates all green (byte-identity 136/0, Generators 102/102, Behavior 144/144,
> Presentation projection 6/6, SimHost HillAttack node 46/46 + integration 6/6, IG deactivator 3/3).
> The rest of this note is the original pre-implementation analysis, kept for the record.

---


**Updated:** 2026-07-13. Stage 2 is **implemented, verified, pushed** on
`claude/hill-attack-json-slice-3-7fbaf4`. Stages 3–5 are **parked** on two genuine design
decisions that the continuation brief explicitly said not to guess on (deactivator stateful-slot
access; factory registration). This note records the full investigation so the next run (or the
user's answer) can proceed directly.

## Stage 2 — DONE (commit `85dea39`), verified

FDP-side stateful code-builder helper, layering-safe:
- `Fdp.Toolkit.Behavior.ReusableStatefulActionDelegate<TParams,TWorkingState,TContext>` — the 4-param
  stateful node shape (FDP-side; FastBTree untouched).
- `Fdp.Toolkit.Behavior.StatefulBTreeActionBinder` (runtime toolkit — **only** touches `Fbt.Kernel`,
  not `Fbt.Compiler`): `RegisterStatefulThunk(...)` curries the runtime analogue of the emitted
  stateful thunk (param projection at baked offset + tier dispatch 16384→4096→1024 +
  `TryGetSlotOffset(scopeKey)` + working-state projection + the 4-param call), registers it under the
  emitter-compatible key `{MethodFqn}@{offset}@{slotKey}`, and records a `StatefulSlotInfo` in a
  `StatefulSlotManifestBuilder` (deduped by slot key). Scope key = same FNV-1a as
  `BTreeBridgeEmitCore.ComputeStatefulSlotKey` (Node/Behavior/Entity).
- `Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope` — {Node=0,Behavior=1,Entity=2}. Placed in
  the partitioning namespace, **not** `…Behavior`, because CycloneDDS auto-generates an IDL enum for
  every public enum and the `Behavior` enumerator collides with the `module Behavior` (IDL injects
  enumerators into the enclosing scope). This was a real build break; do not move it back.
- `Hrot.AI.Behaviors.Brains.StatefulTreeBuilderExtensions.StatefulAction(...)` — thin authoring glue
  (needs `Fbt.Compiler`'s `BTreeBuilder`) that calls the runtime binder then adds the leaf via the
  generic `BTreeBuilder.Action(string methodKey)` seam. Lives in Hrot.AI.Behaviors so the runtime
  toolkit stays free of the FastBTree compiler assembly. **Layering note:** the brief said "helper in
  Fdp.Toolkit.Behavior" but `Fdp.Toolkits` references only `Fbt.Kernel`, so the substantive helper is
  in FDP and only the ~10-line builder extension is in the authoring assembly.
- Test: `Fdp.Toolkits.Tests/Behavior/CodeBuiltStatefulActionTests.cs` — Behavior-scoped two-node share
  (one slot, cursor 0→2) + Node-scoped independence (two slots). Uses `DemoCounterNodes.*`.

**Gates re-run green:** stage-2 tests 2/2; Behavior namespace 144/144 (142 + 2 new); byte-identity
136/0; T20 2/2.

## The stage 3↔4 build coupling (must land together)

Converting the commander node signatures (stage 3) and converting the JSON (stage 4) are **not
independently buildable**:
- The generated `PlatoonHillAttack.Registrar.g.cs` and `FbtActionRegistrar.g.cs` emit calls to the
  node methods. While the JSON stays `ThreeParamReusable`, they emit 3-param calls `(ref dto, ref st,
  ref ctx)`; if the node bodies become 4-param first, those generated calls fail to compile. Convert
  the JSON first and the generated stateful thunk emits a 4-param call `(ref dto, ref ws, ref st, ref
  ctx)` to a still-3-param method — also a break.
- `FbtActionRegistrar` (the `BTreeActionGenerator` analyzer) **skips** stateful (4-param) methods —
  confirmed: `DemoCounterNodes.Action_AdvanceCursor` is absent from its output. So once the 6 commander
  nodes become stateful they vanish from `FbtActionRegistrar`, and the factory's action registry no
  longer contains their thunks (see decision 2).

⇒ Land stage 3 + stage 4 as **one** green commit (node bodies + code-builder rewire + JSON + factory).
Stage 5 (T30 proof + hack-removal assertion) can be its own commit.

Nodes to convert (6 that touch `s`, + deactivator): `Action_CalculateSegments`,
`Action_DispatchAllToBaseline`, `Action_RequestAreaQuery`, `Condition_IsAreaQueryResolved`,
`Action_DispatchWaveWithTargets`, `Condition_IsWaveCompleted`, and `Deactivate_RequestAreaQuery`.
`Condition_AreAllAtBaseline` does **not** touch `s` — leave it `ThreeParamReusable` (the brief's "7"
is 6 stateful nodes + the deactivator). Byte-identity gate is unaffected (it covers the code-first
`Trees/*.cs` fixtures — SampleScout etc. — not the JSON `PlatoonHillAttack`).

## DECISION 1 (blocker) — how does the deactivator reach the working-state slot?

`Deactivate_RequestAreaQuery` frees the in-flight EQS slot via `s.CachedEqsRequestId`. Today it uses
the `GetComponentRW<Blackboard1024>() + Unsafe.As` hack. After conversion `s` lives in a partition
slot, so the deactivator needs the slot **and its FNV slot key**. But:
- `NodeDeactivatorDelegate<TBB,TCtx>` is `(ref TBB, ref BehaviorTreeState, ref TCtx, int)` — no working
  state.
- The emitter (`EmitDeactivatorRegistrations`) supports **only** 3-param (project DTO at offset) and
  4-param (full blackboard, direct) deactivators. **There is no stateful-deactivator emission** that
  projects a partition slot, and a plain deactivator method has no baked slot key.
- `T30.HillAttack_NoBlackboard1024Access` requires the hack be gone, so "leave the deactivator as-is"
  is not an option.

Options:
- **(1a) Add a stateful-deactivator emission (recommended, correct/parity).** New 5-param deactivator
  shape `(ref TParams, ref TWorkingState, ref BehaviorTreeState, ref TCtx, int)`; extend
  `DeactivatorEntry` + the scanner + `EmitDeactivatorRegistrations` to emit a wrapper that bakes the
  slot key, does tier dispatch + `TryGetSlotOffset`, projects the working state, and calls the 5-param
  method — mirroring `EmitStatefulActionThunks`. Also add a code-builder analogue for the
  `BuildPlatoonHillAttackTree` path. **Cost:** touches the byte-identity-gated emitter + a new delegate
  shape + registrar/scanner + tests. Cleanest long-term.
- **(1b) In-body partition projection with a baked key (pragmatic, no new infra).** Keep the 4-param
  deactivator; inside it, tier-dispatch + `TryGetSlotOffset(SLOT_KEY)` where `SLOT_KEY` is a
  `const`/`static readonly` on the node class computed from the known asset id + `"State"`. Removes the
  Blackboard1024 hack, satisfies T30, no emitter change — but hard-codes the asset-scoped key in combat
  code (fragile if the asset id or variable name changes; the code and JSON must agree).
- **(1c) Code-builder closure only.** In `BuildPlatoonHillAttackTree` the builder already has the slot
  key, so a captured-key deactivator closure works there — but the **production** path is the
  JSON-generated interpreter (see decision 2), which registers the plain method, so this alone does
  not satisfy the production/T30 path.

Recommendation: **1a** if we want it done right (matches how stateful actions already work); **1b** if
we want the demo gate closed fast with a documented follow-up to generalize.

## DECISION 2 (blocker) — how does `AiBehaviorFactory` register the stateful PlatoonHillAttack?

Production registers behaviors through `AiBehaviorFactory.BuildRegistrationAction`
(`CgfBehaviorSetup.LoadFromAiAssembly`); the per-asset `PlatoonHillAttackRegistrar` is the
**editor/hot-reload** path (`AiHotReloadCoordinator.ScanForRegistrars`). The factory builds its
interpreter from `PlatoonHillAttack.Build()` and binds against an action registry populated by
`FbtActionRegistrar.RegisterAll`. After stage 4:
- `PlatoonHillAttack.Build()` blob keys become `{MethodFqn}@0@{slotKey}` (stateful).
- `FbtActionRegistrar` no longer carries the 6 stateful thunks (it skips stateful methods).
- The factory must also drop `HeavyDtoType` and carry the `StatefulWorkingSlots` manifest so
  `BehaviorIngressSystem` provisions the shared slot.
- The factory must **keep** the geo-aware `ParseParams` (`ParsePlatoonHillAttackParams` with
  geoTransform + entityMap) — the generated registrar emits no such ParseParams.

Options (both pre-sanctioned by the brief: "from the JSON registrar's def, or hand-assembled"):
- **(2a) Reuse the generated `PlatoonHillAttackRegistrar` (recommended, DRY).** Invoke it against the
  factory's `actionRegistry` (registers the stateful thunks + the `Condition_AreAllAtBaseline` thunk +
  deactivator) into a throwaway `BehaviorRegistry`, take its def's `BTreeInterpreter` +
  `StatefulWorkingSlots` + `ManagedBlackboardVariables`, then register the real def with the geo-aware
  `ParseParams` swapped in. Needs a `BlueprintRegistryStaging` arg (the current registrar body doesn't
  use it — verify after regen). Reuses verified generated thunks; no hand-duplicated tier dispatch.
- **(2b) Hand-assemble in the factory.** Hand-register 6 stateful thunks (duplicate the emitted tier-
  dispatch pattern) + the manifest (single Behavior slot: `ComputeStatefulSlotKey(assetId, Behavior,
  Empty, "State")`, `SizeOf<HillAttackMutableState>()` = 120, typeNameHash ^ size). Explicit, but ~250
  lines of hand-written combat thunks that must stay in lockstep with the emitter.

Recommendation: **2a**.

## Everything else is mechanical once 1 & 2 are decided
- Node bodies: delete the `ref var heavyComp = GetComponentRW<Blackboard1024>(); ref var s =
  Unsafe.As<…>(…);` prologue; add `ref HillAttackMutableState s` param. `PickClosestBaselineSlot`
  already takes `ref HillAttackMutableState s`; `SwapRemove` too.
- Code builder: rewrite `BuildPlatoonHillAttackTree` to use `.StatefulAction<PlatoonHillAttackBlackboard,
  PlatoonHillAttackParams, HillAttackMutableState>(bb => bb.Params, <method>, manifest, "State",
  StatefulSlotScope.Behavior, visualId, label)` for the 6 stateful nodes; keep `.Action(bb=>bb.Params,
  Condition_AreAllAtBaseline)` for the non-stateful one; expose the manifest for the factory if 2a/2b
  needs it.
- JSON: add a `State`-role `Behavior`-scope variable `HillAttackMutableState`
  (`Hrot.AI.Behaviors.Brains.HillAttackMutableState`); set the 6 nodes' `DelegateShape` to
  `ThreeParamReusableStateful` with `WorkingStateTargetField="State"` + `WorkingStateTypeId`.
- Stage 5: `T30_BehaviorScopedShared_ProofTests` — `HillAttack_SharedState_PersistsAcrossNodes`
  (generate→compile→provision→tick; mask written by DispatchWave read by IsWaveCompleted over the one
  shared slot) + `HillAttack_NoBlackboard1024Access` (generated source has no
  `Blackboard1024`/`Unsafe.As<…,HillAttackMutableState>`).
