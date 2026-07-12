# S3-G (DEMO GATE) — PARKED, needs a design decision before implementation

**Date:** 2026-07-12 (overnight autonomous run) · **Status:** blocked, not attempted (no code changed).
**Context:** Slice-3 mechanism batches S3-3, S3-4, S3-5, S3-6, S3-7 are all **done, verified, pushed**. S3-G applies that mechanism to the *real* Hill Attack behavior. I stopped here deliberately per the "park if genuinely ambiguous rather than guess" instruction — S3-G touches production combat AI and I found genuine ambiguities that need an architect/user decision, not a guess.

## What S3-G asks for (TASK-DETAIL §S3-G)
Declare `HillAttackMutableState` (120 B) as a `Behavior`-scoped `state` variable in `PlatoonHillAttack.btree.json`; rebind `CalculateSegments`/`DispatchWave`/`IsWaveCompleted` to the 4-param `ThreeParamReusableStateful` shape; remove the `Blackboard1024 + Unsafe.As` hack; prove it with `T30_BehaviorScopedShared_ProofTests`.

## Why it's blocked — three concrete issues

### 1. Design gap: params vs working-state variable scoping (the blocker)
The Slice-3 mechanism I built (S3-2/S3-3/S3-4/S3-7, exactly as TASK-DETAIL specified) derives a node's stateful slot **scope** from the variable named by the binding's **`ExpressionTargetField`** — i.e. `ResolveStatefulSlotKey(dto, p.ExpressionTargetField, …)` reads *that* variable's `Scope`.

In every Slice-3 unit/proof test the bound variable and the stateful variable are the **same** authored variable. But real Hill Attack has **two distinct** variables:
- `Params` — `PlatoonHillAttackParams` (52 B, the input param DTO; `ExpressionTargetField: "Params"` on all 7 nodes today).
- `HillAttackMutableState` — 120 B working state (currently the `Blackboard1024` hack, `HeavyDtoType` in `AiBehaviorFactory`).

`BTreeActionPayloadDto` carries `ExpressionTargetField` (the params variable) + `WorkingStateTypeId` (a **type FQN**, not a variable name). There is **no field that names the working-state *variable***, so there is nowhere to attach the `Behavior` scope for the working state, and the scope resolver would read `"Params"` (Input/Node) → a Node-scoped key, not Behavior. 

**Decision needed:** how does a binding with distinct params + working-state variables carry the working-state scope? Options:
- (a) Add a `WorkingStateTargetField` (variable name) to `BTreeActionPayloadDto`; declare `HillAttackMutableState` as a `state`/`Behavior` variable; have `ResolveStatefulSlotKey`/`ResolveVariableRoleScope` key off *that* field for stateful nodes. (Cleanest; small schema addition — but it's a schema/persistence change with byte-stability implications, so it needs sign-off.)
- (b) Convention: for `ThreeParamReusableStateful`, `ExpressionTargetField` names the **working-state** variable and the params come from a fixed/implicit slot. (Changes the meaning of an existing field.)
- (c) Something else per the architect's intent in AIB-DD §4.4.5.

This is an authoring-schema decision, not a mechanical edit.

### 2. Scope discrepancy: 3 named nodes vs 7 that use the state
TASK-DETAIL names 3 nodes, but **7** access `HillAttackMutableState` via the hack: `Action_CalculateSegments`, `Action_DispatchAllToBaseline`, `Action_RequestAreaQuery`, `Condition_IsAreaQueryResolved`, `Action_DispatchWaveWithTargets`, `Condition_IsWaveCompleted`, plus the `Deactivate_RequestAreaQuery` deactivator. Converting only 3 would split the state across two backing stores (partition slot for the converted 3, `Blackboard1024` for the other 4) → **two divergent copies of one logical state → silently broken combat AI.** Full hack removal must convert **all 7 + the deactivator** atomically. Confirm the intended scope is all-7.

### 3. Code-builder vs JSON duality
`HillAttackCommanderNodes.cs` has BOTH:
- a code `[BTreeDefinition("PlatoonHillAttack")] BuildPlatoonHillAttackTree()` using `[BTreeAction]` 3-param methods, and
- the committed `PlatoonHillAttack.btree.json` (which `AiBehaviorFactory` builds via `PlatoonHillAttack.Build()` — the generated topology).

Converting the methods to the 4-param stateful shape breaks the `[BTreeAction]`/code-builder path (`.Action(bb => bb.Params, Action_CalculateSegments)` expects the 3-param delegate; `[BTreeAction]` auto-registration is 3-param only). Decision needed: is the code builder dead (delete it) or must it stay in sync (and if so, how, given stateful nodes aren't `[BTreeAction]`)?

## What is NOT a blocker (confirmed feasible)
- `HillAttackMutableState` is a blittable `unsafe` struct with `fixed` buffers → fine for a partition slot + `Marshal.SizeOf` (120 B), well within any tier.
- The runtime plumbing (scope-aware key, shared-slot provisioning/dedup, clear-detach, monitoring) is done and verified — once the authoring model above is decided, the emit/runtime side is ready.

## Recommended path (for the morning)
1. Decide issue #1 (recommend option **a**: add `WorkingStateTargetField` to the binding DTO with omit-when-default byte-stability, and resolve stateful scope from it). This is itself a small batch (call it S3-G-pre) with its own byte-identity gate.
2. Convert **all 7** nodes + deactivator to the 4-param shape; delete or reconcile the code builder (#3).
3. Author `HillAttackMutableState` as a `Behavior`-scoped `state` var in `PlatoonHillAttack.btree.json`; drop `HeavyDtoType` from the factory; remove the `Blackboard1024` hack.
4. `T30_BehaviorScopedShared_ProofTests` (generate→compile→provision→tick) + `HillAttack_NoBlackboard1024Access` (assert the generated code no longer references `Blackboard1024`/`Unsafe.As` for this state).

## Environment note (important for whoever picks this up)
This session's container had **no .NET SDK** and the dotnet download hosts are egress-403. I installed `dotnet-sdk-8.0` via `apt` from `packages.microsoft.com` (reachable) — `apt-get install -y dotnet-sdk-8.0`. All Slice-3 verification this session ran on that. A fresh container will need the same install before it can build/test.
</content>
