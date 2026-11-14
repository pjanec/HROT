# BATCH-06 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-16

## Summary
S2-1 (per-node WorkingState in a `BlueprintBlackboard*` partition slot, FNV-1a key, baked-offset adapter thunk + manifest) and S2-2 (synchronous Input-phase tier provisioning/upgrade in `BehaviorIngressSystem`) implemented per spec. Verified by reading the source and running the suites myself.

## Verification (run by lead, not trusting the report)
- `Fdp.Toolkits.Tests` `~StatefulPrimitiveTests|~BehaviorIngressStatefulTests`: **4/4 pass**.
- `Hrot.AiEditor.Generators.Tests` `~StatefulSlotKeyTests`: **2/2 pass**.
- Byte-identity gate `Hrot.AiEditor.Persistence.Tests`: **129/0**.
- Agent-reported full counts (Behavior 150/0; full 1862/0; Generators 83/2 = the 2 known MigrationEquivalence) consistent with spot-checks.

## Test quality (read the assertions)
- **S2-2 provisioning tests** — strong. Exercise the real `BehaviorIngressSystem.Execute` via the event bus; Test 1 pre-fills a 900 B slot with a sentinel, asserts upgrade 1024→4096, sentinel survived `CopyToLargerTier`, all 3 manifest slots attached. Test 2 asserts `GetSlotCount==3`. Not hand-rolled.
- **S2-1 mechanism tests** — strong runtime value assertions (cursor persists to 3 across ticks from the partition slot; two nodes evolve to 6/2 independently). Hand-rolled thunks that *mirror* the emitter (acceptable for proving the partition mechanism).
- **S2-1 emitter test** — runs the real `BTreeBridgeEmitCore.EmitBridge`, asserts the baked slot-key literal matches the independently-computed FNV value + presence of `TryGetSlotOffset`/`Unsafe.AsRef`/`StatefulWorkingSlots`. String-on-emitted-source (not compiled), but it does exercise the emitter and lock the key.
- **Slot-key stability test** — locks FNV-1a-32 vs emitter impl + distinctness. Good.

## Issues Found
None blocking. Two follow-on notes recorded as debt (not regressions):

### Note 1 (→ S2-G): emitted stateful thunk is never compiled yet
No authored asset uses `ThreeParamReusableStateful`, so the 257-line emitted stateful thunk is not compiled by any build/test — a syntax error would not surface until S2-G. **S2-G MUST author a real stateful demo asset (T20) compiled by the normal codegen build + a proof test that ticks the actually-emitted code**, closing this gap. (DEBT-AIB-026)

### Note 2 (→ S2-3): StructureHash is a type-NAME hash, not layout-derived
`EmitStatefulWorkingSlotsArray` sets `StructureHash = FNV-1a(typeName)`. This will NOT change if a same-named WorkingState struct grows a field — exactly the Hard-Reload ghost-slot hazard S2-3 must catch. **S2-3 must make the manifest `StructureHash` layout-derived** (field types/offsets), and the re-publish/detach path must rely on it. (DEBT-AIB-027)

## Deviations (ratified)
- `Action_AdvanceCursor` carries no `[BTreeAction]` (that attribute's source generator only handles the 3-param stateless shape and would emit a wrong-typed registry → compile error). Statefulness is marked via `BTreeDelegateShapeDto.ThreeParamReusableStateful` on the payload + `WorkingStateTypeId`. Sound.
- `SameStatefulPrimitive_TwoNodes_IndependentSlots` uses two independent single-node blobs (the inherited Sequence-blob design was logically broken — a Running NodeA blocks NodeB forever). Correct fix.
- Scoping decision (lead): mechanism proven via a stateful demo primitive, NOT the full BTree→blueprint-`TickCore` reference-resolution composition (absent today). → DEBT-AIB-025.
- `PayloadSize` emitted as `Marshal.SizeOf<T>()` at registration (vs the managed-size resolver). Correct for blittable WorkingState structs (which these must be, living in unmanaged partition memory).

## Verdict
APPROVED. Proceed to S2-3 (carry Note 2) and S2-4; close Note 1 at S2-G.

## Commit Message
```
feat(btree-binding): S2-1+S2-2 stateful per-node WorkingState + sync provisioning (BATCH-06)

Completes S2-1, S2-2.
- DemoCounterNodes: DemoCursorParams/State + Action_AdvanceCursor (ThreeParamReusableStateful)
- BehaviorTreeAssetDto: ThreeParamReusableStateful shape + WorkingStateTypeId
- BehaviorRegistry: StatefulSlotInfo + BehaviorDefinition.StatefulWorkingSlots manifest
- BTreeBridgeEmitCore: emit partition-slot adapter thunk (3-tier dispatch 16384→4096→1024,
  fail-loud on missing slot) + per-asset StatefulWorkingSlots manifest; FNV-1a-32 slot key
- BehaviorIngressSystem: synchronous Input-phase tier provisioning + upgrade
  (AddComponent+CopyToLargerTier+RemoveComponent inline, safe outside the Simulation lock);
  detach prior behavior's slots on change
Tests: 6 new (4 runtime mechanism+provisioning, 2 emitter/slot-key); byte-identity 129/0;
no net-new failures. Notes: emitted thunk compile-gap → S2-G; layout-hash → S2-3.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
