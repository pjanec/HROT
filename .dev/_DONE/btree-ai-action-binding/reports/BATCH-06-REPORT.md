# BATCH-06 Report

## Implementation Summary

### Task 1 (S2-1): Stateful demo primitive + partition-slot WorkingState adapter

**Files changed:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/DemoCounterNodes.cs` — added `DemoCursorParams { int Limit }`, `DemoCursorState { int Cursor }`, and `Action_AdvanceCursor(ref DemoCursorParams, ref DemoCursorState, ref BehaviorTreeState, ref BTreeContext)`.
- `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs` — added `ThreeParamReusableStateful = 2` to `BTreeDelegateShapeDto`; added `WorkingStateTypeId` property to `BTreeActionPayloadDto`.
- `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` — added `ComputeStatefulSlotKey`, `EmitStatefulActionThunks`, `EmitStatefulWorkingSlotsArray`, `DeriveWorkingStateTypeFromMethod`, `ComputeTypeNameHash`.
- `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` — added `StatefulSlotInfo` record; added `StatefulWorkingSlots` property to `BehaviorDefinition`.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — added project reference to `Hrot.AI.Behaviors`.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/StatefulPrimitiveTests.cs` — new runtime test file (S2-1 tests).
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Bridge/StatefulSlotKeyTests.cs` — new emitter-exercising test file.

**What was built:**
1. `DemoCursorParams` / `DemoCursorState` blittable structs with `[StructLayout(Sequential)]`.
2. `Action_AdvanceCursor` 4-param stateful method (NOT attributed with `[BTreeAction]` — see Design Decisions).
3. `BTreeDelegateShapeDto.ThreeParamReusableStateful` enum value.
4. `WorkingStateTypeId` property on `BTreeActionPayloadDto`.
5. `ComputeStatefulSlotKey(Guid, Guid)` — FNV-1a-32 over assetId bytes ++ nodeVisualId bytes, masked to positive int.
6. `EmitStatefulActionThunks` — emits a thunk per stateful node instance that:
   - Projects `Params` at baked param offset (Slice-1 pattern)
   - Dispatches across 16384 → 4096 → 1024 tiers
   - Calls `TryGetSlotOffset(mem, __slotKey, out int wsOff)`
   - Projects `WorkingState` at `mem + wsOff` via `Unsafe.AsRef<TWorkingState>`
   - Calls the 4-param method `(ref dto, ref ws, ref st, ref ctx)`
   - Fails loud on missing slot: `Debug.Assert(false)` + returns `NodeStatus.Failure`
7. `EmitStatefulWorkingSlotsArray` — emits `StatefulWorkingSlots = new StatefulSlotInfo[] { ... }` in `BehaviorDefinition` initializer.
8. Thunk key format: `{MethodFqn}@{paramOffset}@{slotKey}` (per-node unique).
9. `StatefulSlotInfo` record in `BehaviorRegistry.cs`.
10. `StatefulWorkingSlots` on `BehaviorDefinition`.

### Task 2 (S2-2): Synchronous Input-phase provisioning + tier upgrade

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs` — added `ProvisionStatefulSlots`, `DetachStatefulSlots`, and a suite of tier-inspection helpers.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BehaviorIngressStatefulTests.cs` — new runtime test file (S2-2 tests).

**What was built:**
In `BehaviorIngressSystem.Execute`, after the behavior transition is committed:
1. Reads previous `ActiveBehaviorHash` before overwriting.
2. If new `def.StatefulWorkingSlots` is non-null/non-empty:
   a. Detaches previous behavior's slots (via `TryDetach` on the entity's current tier).
   b. Calls `ProvisionStatefulSlots(repo, entity, def.StatefulWorkingSlots)`.
3. `ProvisionStatefulSlots` computes the aggregate payload + slot count required for the manifest.
4. If no tier present: adds smallest fitting tier and initializes it.
5. If existing tier present: checks free payload and free slot table entries. If insufficient, computes TOTAL needed (existing used + new manifest) and upgrades synchronously via `AddComponent` + `CopyToLargerTier` + `RemoveComponent`.
6. Eager-allocates every manifest slot via `TryAttach` (idempotent — skips already-attached).

---

## Design Decisions

### 1. New attribute for stateful shape
**Decision:** Did NOT introduce a new attribute. Instead added `BTreeDelegateShapeDto.ThreeParamReusableStateful = 2` to the existing enum.  
**Why:** The `[BTreeAction]` / `[BTreeCondition]` attributes drive the FbtActionRegistrar source generator which does NOT support the 4-param stateful shape. Marking `Action_AdvanceCursor` with `[BTreeAction]` causes the generator to emit a broken overload of `ActionRegistry.Register` that fails to compile. The chosen approach was minimal: a new enum value on the existing `DelegateShape` enum, and NOT attributing `Action_AdvanceCursor` with `[BTreeAction]`. The method is discovered/registered purely via the bridge emitter (which checks `DelegateShape == ThreeParamReusableStateful`).

### 2. How the thunk dispatches across tiers
The emitted thunk uses an `if`/`else if` chain in priority order: `BlueprintBlackboard16384` → `BlueprintBlackboard4096` → `BlueprintBlackboard1024`. Only one branch fires per call. If none exist, `Debug.Assert(false)` fires and `NodeStatus.Failure` is returned.

### 3. WorkingState StructureHash computation
`StructureHash` is computed as `ComputeTypeNameHash(wsTypeId)` — FNV-1a-32 over the UTF-8 bytes of the WorkingState type's FQN. This is a stable proxy for struct layout (for the demo stage). A future task could replace this with Roslyn-computed field-level hashing (when hot-reload strictness requires it). The choice avoids a Roslyn dependency in the emit path and is documented as a limitation.

### 4. Behavior-change detach
`BehaviorIngressSystem` reads `BehaviorState.ActiveBehaviorHash` before overwriting it (the previous behavior ID). It then looks up the previous definition via `_registry.TryGetDefinition(previousBehaviorId, ...)` and calls `TryDetach` for each of the previous behavior's `StatefulWorkingSlots`. If the previous definition is not found (e.g. hot-reload wiped it), detach is skipped with no leak-protection. This limitation is documented (S2-3 hot-reload repair covers the full case).

### 5. Tier selection considers existing occupancy
`SelectTierForPayload` computes based on manifest aggregate only (abstract capacity). When an entity already has a tier, `ProvisionStatefulSlots` checks CURRENT FREE SPACE and upgrades if the free space is insufficient for the manifest. This correctly handles the case where an existing blueprint has already consumed most of the tier's payload.

### 6. AlignUp in BehaviorIngressSystem
The overhead calculation uses `AlignUp(PayloadSize, 8) + 16` per slot to match the `BlueprintBlackboardPartitions.TryAttach` allocation which aligns to 8 bytes. This ensures the tier selection correctly accounts for padding.

---

## Deviations

1. **`Action_AdvanceCursor` not attributed `[BTreeAction]`** — The spec says "Attribute it so it is discoverable as a stateful BTree action." The chosen attribute is `ThreeParamReusableStateful` on the action payload DTO, NOT a method-level attribute. The method itself carries no attribute. Risk: the method won't appear in the FbtActionRegistrar auto-registration (which is the correct behavior for the stateful shape). Benefit: avoids a compile error from the source generator.

2. **`StatefulSlotInfo` is a `record` not a plain `struct`** — The spec doesn't specify struct vs record; `record` was used for immutability and equality semantics. Minimal impact.

3. **`SameStatefulPrimitive_TwoNodes_IndependentSlots` rewritten** — The previous agent's test used a `Sequence[NodeA, NodeB]` with `Limit=100`, which causes NodeA to return `Running` indefinitely (Sequence stays stuck at NodeA, NodeB never runs). The test was rewritten to use two independent single-action blobs, advancing NodeA 4 times and NodeB 2 times independently, then asserting NodeA=4, NodeB=2, and after 2 more NodeA advances, NodeA=6, NodeB still=2. This is a stronger test that genuinely proves independence (NodeA advancing doesn't affect NodeB's slot).

---

## Test Results

### Fdp.Toolkits.Tests — Behavior filter
```
Passed:  150, Failed: 0, Skipped: 0
```
Includes:
- `StatefulPrimitive_WorkingStatePersistsAcrossTicks` ✓
- `SameStatefulPrimitive_TwoNodes_IndependentSlots` ✓
- `Assign_UpgradesTierSynchronously_BeforeFirstTick` ✓
- `Assign_ProvisionsWorstCaseReachableStatefulNodes` ✓
- All prior behavior tests ✓

### Fdp.Toolkits.Tests — Full suite (stability filter)
```
Passed: 1862, Failed: 0, Skipped: 0
```

### Hrot.AiEditor.Generators.Tests (stability filter)
```
Passed: 83, Failed: 2, Skipped: 0
```
The 2 failures are the pre-existing `MigrationEquivalenceTests` JSON byte-stability cases documented as known non-regressions. No net-new failures.

- `SlotKey_KnownGuidPair_ProducesKnownInt` ✓
- `StatefulEmitter_EmitsBridge_WithTryGetSlotOffset_AndSlotKeyLiteral` ✓

### Hrot.AiEditor.Persistence.Tests — Byte-identity gate
```
Passed: 129, Failed: 0, Skipped: 0
```
CombatShowcase and SampleScout emit byte-identical output. All emit changes guarded behind `Managed==true`.

### Hrot.AI.Behaviors — Clean rebuild
```
0 errors, 0 warnings
```

---

## Inherited partial work: what I kept / changed / why

### What I KEPT
- `DemoCounterNodes.cs` additions: `DemoCursorParams`, `DemoCursorState`, `Action_AdvanceCursor` — correct and complete per spec.
- `BehaviorTreeAssetDto.cs` additions: `ThreeParamReusableStateful` enum value, `WorkingStateTypeId` property — correct.
- `BTreeBridgeEmitCore.cs` (mostly): `ComputeStatefulSlotKey`, tier-dispatch thunk emission, `EmitStatefulWorkingSlotsArray`, `DeriveWorkingStateTypeFromMethod`, `ComputeTypeNameHash` — all structurally correct.
- `BehaviorRegistry.cs` additions: `StatefulSlotInfo` record, `StatefulWorkingSlots` on `BehaviorDefinition` — correct.
- `StatefulSlotKeyTests.cs` — both tests were structurally correct and pass.

### What I CHANGED

1. **`BTreeBridgeEmitCore.cs` nullable fix (lines ~415, ~518)**: The previous agent had `string wsTypeId = ... : p.WorkingStateTypeId` where `WorkingStateTypeId` is `string?`, causing CS8600/CS8620 nullable errors. Fixed by adding the null-forgiving `!` operator.

2. **`DemoCounterNodes.cs`**: Removed `[BTreeAction]` from `Action_AdvanceCursor`. The previous agent added `[BTreeAction]` which triggers the FbtActionRegistrar source generator to emit a broken overload (`ActionRegistry<DemoCursorParams, BehaviorTreeState>`) that doesn't compile because `BehaviorTreeState` does not implement `IAIContext`.

3. **`StatefulPrimitiveTests.cs` logic fix**: 
   - Changed `static Func<...>` to `Func<...>` (invalid `static` modifier on a variable holding a lambda that captures `sk`).
   - Changed `BlueprintBTreeActionDelegate` return type to `NodeLogicDelegate<BrainBlackboard, BTreeContext>` (incompatible delegate types; `ActionRegistry.Register` needs the latter).
   - Rewrote `SameStatefulPrimitive_TwoNodes_IndependentSlots` test body: the previous agent used a Sequence blob that never advanced to NodeB (NodeA returns Running indefinitely). Replaced with two independent single-action interpreters that advance NodeA and NodeB separately.
   - Fixed `blobB.Nodes[0].RawPayloadIndex = 0` (was 1, causing out-of-bounds in `MethodNames[1]`).

4. **`Fdp.Toolkits.Tests.csproj`**: Added `<ProjectReference>` to `Hrot.AI.Behaviors.csproj` (test file references `DemoCounterNodes`).

### What I IMPLEMENTED NEW (S2-2)
- `BehaviorIngressSystem.cs` provisioning logic (entire S2-2 implementation).
- `BehaviorIngressStatefulTests.cs` (both S2-2 runtime tests).

---

## Developer Insights

- **FbtActionRegistrar generator trap**: Any `[BTreeAction]` method that deviates from the standard 3-param shape `(ref TDto, ref BehaviorTreeState, ref BTreeContext)` will cause a compile error in the generated registrar. The ThreeParamReusableStateful shape is intentionally excluded from this generator.
- **Sequence BTree semantics**: When a child returns `Running`, the BTree Sequence stays on that node and does NOT advance to siblings. Tests that need to drive multiple nodes independently should use separate interpreters, not a shared sequence with `Limit=100`.
- **`AlignUp` must match TryAttach's internal alignment**: The provisioning system adds `AlignUp(size, 8)` overhead per slot. This matches `BlueprintBlackboardPartitions.TryAttach`'s internal `AlignUp(requestedSize, Alignment)` call. Mismatching these would cause the tier selection to pick a smaller tier that then fails to attach at runtime.
- **Tier free-space check is per-call, not per-manifest-entry**: The single aggregate `freePayload >= requiredPayload` check is valid because `TryAttach` does sequential bump allocation. Fragmentation can cause false positives (tier shows enough free space but it's fragmented), but for the demo/test scenarios this is not an issue.
- **Hot-reload ghost slot (Flaw 2) and cross-region validator (Flaw 3) are NOT implemented** — these are S2-3 and S2-4 respectively, out of scope for this batch.

---

## Known Issues

1. **StructureHash is type-name-based**: Using FNV-1a-32 of the type FQN as a structural hash doesn't detect field layout changes. If `DemoCursorState` gains a new field, the hash doesn't change — the old slot will be considered valid. A proper fix requires Roslyn field-layout hashing (deferred).

2. **Hot-reload ghost slot (DEBT-AIB)**: S2-3 (not this batch). If `Action_AdvanceCursor`'s WorkingState grows on Hard Reload, the old slot survives with the wrong size.

3. **Cross-region concurrency (DEBT-AIB)**: S2-4 (not this batch). Two parallel HSM regions running the same stateful Subtree will collision on the same slot key.

4. **Detach on behavior change**: If the previous behavior's definition was cleared from the registry (e.g. by a hot-reload that wiped it), `TryGetDefinition` returns false and old slots are not detached. The slot space is leaked until the next full tier reset.

5. **DEBT-AIB-025 recorded**: The full blueprint-AiPrimitive composition path (BTree node → blueprint TickCore via `AiPrimitive`) is deferred. The current implementation proves the mechanism via the demo primitive.

---

## Suggested Commit Message

```
feat(btree-binding): S2-1+S2-2 stateful per-node WorkingState + synchronous provisioning

- Add DemoCursorParams/State + Action_AdvanceCursor (ThreeParamReusableStateful shape)
- BTreeBridgeEmitCore: emit partition-slot adapter thunk (dispatches 16384→4096→1024,
  fails loud on missing slot) + StatefulWorkingSlots manifest per asset
- BehaviorIngressSystem: synchronous Input-phase tier provisioning + upgrade
  (AddComponent+CopyToLargerTier+RemoveComponent inline, safe outside Simulation lock)
- 4 new named tests all green; byte-identity gate 129/0; no net-new failures
```
