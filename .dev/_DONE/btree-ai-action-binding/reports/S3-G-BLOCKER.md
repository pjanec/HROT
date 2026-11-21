# S3-G (DEMO GATE) — decisions resolved; stage 1 landed; stages 2–5 remain

> **⚠️ SUPERSEDED / STALE (2026-07-13).** All S3-G stages (1–5) are now **complete, verified,
> and pushed** — see `S3-G-STAGE2-DONE-STAGES345-DECISIONS.md` in this folder for the authoritative
> status. The "stages 2–5 remain" framing below is out of date; kept only for historical context.

**Updated:** 2026-07-12 (overnight run). Was a blocker; the design questions are now **decided** (with the user) and **stage 1 is implemented, verified, pushed**. This doc is now the continuation plan.

## Decisions (made with the user)
1. **Keep the code `[BTreeDefinition]` builder** — do NOT obsolete it. It's a first-class code-first authoring path (valuable for AI-agent/code prototyping). It must support 4-param stateful nodes.
2. **FastBTree stays generic** (no FDP-bound types in the ExtDep). Confirmed feasible: `BTreeBuilder.Action(NodeLogicDelegate<TBlackboard,TContext>, …)` (Fbt.Compiler/BTreeBuilder.cs:220) already accepts a raw 4-arg thunk. FDP curries `(param projection + partition-slot working-state projection via TContext=BTreeContext)` into that seam — **no FastBTree change needed**. The builder already handles in-blackboard working state (field projection); Slice 2/3 only added a *second*, partition-tier state region (FDP-specific), which the seam lets FDP inject.
3. **Convert all 7 commander nodes + the deactivator** (not just the 3 named) — partial conversion would split state across the partition slot and Blackboard1024.
4. **Staged, verify each.**

## Stage 1 — DONE (`2a888ed`), verified
Authoring model for behaviors with **distinct** param + working-state variables (the real Hill Attack shape):
- `BTreeActionPayloadDto.WorkingStateTargetField` names the working-state variable; its Role/Scope drive the slot key. Falls back to `ExpressionTargetField` (byte-identical for Slice-2).
- `BTreeBridgeEmitCore.StatefulScopeVariable(p)` used at all three baked-key sites (thunk, topology, manifest) + role/scope.
- `BTreeBlackboardPackHelper.Pack`/`WouldOverflow` **exclude State-role variables** from the ≤100B inline param region (they live in the partition tier) — so the 120B `HillAttackMutableState` won't overflow. Byte-identical for the Input-only corpus.
- Slice-3 tests corrected to the two-variable model.
- Gates: byte-identity 136/0; Generators 100/100; Fdp.Toolkit.Behavior 142/142; Presentation projection 6/6.

## Remaining stages (each its own verified commit)

### Stage 2 — FDP-side stateful code-builder helper
Add a helper in `Fdp.Toolkit.Behavior` (NOT FastBTree) that binds a 4-param stateful method through the existing generic `Action(NodeLogicDelegate,…)` seam. It must:
- curry a `NodeLogicDelegate<TBB,TCtx>` that: projects params (blackboard field/offset, as `Action<TValue>` does), dispatches across `BlueprintBlackboard{16384,4096,1024}` tiers, `TryGetSlotOffset(scopeKey)`, projects the working state, and calls `(ref TParams, ref TWorkingState, ref BehaviorTreeState, ref TCtx)` — the runtime analogue of the emitted JSON thunk (see `BTreeBridgeEmitCore.EmitStatefulActionThunks`);
- compute the scope key with the **same** FNV-1a used by `ComputeStatefulSlotKey` (Node/Behavior/Entity);
- surface a `StatefulSlotInfo` (SlotKey, PayloadSize=`Marshal.SizeOf<TWorkingState>()`, StructureHash, type, label, role, scope) so the caller assembles a `StatefulWorkingSlots` manifest for the `BehaviorDefinition`.
- Design point to settle: how the manifest flows from the code builder to the `BehaviorDefinition`. For Hill Attack the **factory hand-builds** the def (`AiBehaviorFactory` ~184–191), so the factory can collect the helper's slot infos directly; a general `[BTreeDefinition]`→def path is a larger follow-up.
- Tests: a code-built 2-node behavior sharing one Behavior var → one slot, shared cursor (mirror `S3_BehaviorScopedThunkTests` but via the builder).

### Stage 3 — convert the 7 nodes + deactivator to 4-param
`HillAttackCommanderNodes`: change `Action_CalculateSegments`, `Action_DispatchAllToBaseline`, `Action_RequestAreaQuery`, `Condition_IsAreaQueryResolved`, `Action_DispatchWaveWithTargets`, `Condition_IsWaveCompleted`, and `Deactivate_RequestAreaQuery` to take `ref HillAttackMutableState s` instead of the `ctx.World.GetComponentRW<Blackboard1024>() + Unsafe.As` projection. Rewire `BuildPlatoonHillAttackTree()` via the stage-2 helper. This removes the hack from the node bodies and keeps the code builder compiling + functional.

### Stage 4 — JSON + factory
- `PlatoonHillAttack.btree.json`: declare `HillAttackMutableState` as a `state`/`Behavior` variable; set each of the 7 nodes' binding to `ThreeParamReusableStateful` with `WorkingStateTargetField` = that variable + `WorkingStateTypeId`.
- `AiBehaviorFactory`: drop `HeavyDtoType = typeof(HillAttackMutableState)`; carry the partition-slot `StatefulWorkingSlots` manifest instead (from the JSON registrar's def, or hand-assembled).

### Stage 5 — remove the hack + T30 proof
- Confirm no remaining `Blackboard1024`/`Unsafe.As<…,HillAttackMutableState>` references.
- `T30_BehaviorScopedShared_ProofTests`: `HillAttack_SharedState_PersistsAcrossNodes` (generate→compile→provision→tick; a mask written by DispatchWave is read by IsWaveCompleted via the one shared slot) + `HillAttack_NoBlackboard1024Access` (generated code no longer references the hack).
- Byte-identity gate green throughout.

## Environment note
No .NET SDK preinstalled + dotnet download hosts are egress-403. Installed `dotnet-sdk-8.0` via `apt` from `packages.microsoft.com` (reachable): `apt-get install -y dotnet-sdk-8.0`. A fresh container needs this before build/test.
</content>
