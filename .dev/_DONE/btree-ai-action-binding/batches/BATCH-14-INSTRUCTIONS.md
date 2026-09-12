# BATCH-14 — S3-4: Shared-slot provisioning / dedup (Behavior scope)

**Task:** TASK-DETAIL.md → S3-4. **Slice 3 (§4.4 Behavior-scope shared working state MVP).**
**Design of record:** `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` §4.4 / §4.4.2 (key-formula resolution note 2026-07-12).
**Depends on:** S3-1 ✓ (`Role`/`Scope` on `BlackboardVariableDto`) and S3-2 ✓ (`ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)`). Both already on the branch — do NOT re-implement them.
**Nature:** one focused emitter change (scope-aware slot key in the *manifest* emit) + runtime provisioning tests. **You are NOT touching the emitted thunk** (`EmitStatefulActionThunks`) — that is S3-3 / BATCH-15, a separate batch. Stay in your lane.

---

## Background — how provisioning works today (read this before editing)

Storage reuses Slice-2's partitioned tier unchanged. The relevant chain:

1. **Emit (`BTreeBridgeEmitCore.cs`):**
   - `EmitStatefulActionThunks` (lines ~508–616) bakes a `const int __slotKey` into each stateful thunk and registers it under key `{MethodFqn}@{paramOffset}@{slotKey}`. **DO NOT TOUCH in this batch.**
   - `EmitStatefulWorkingSlotsArray` (lines ~624–677) emits the `StatefulWorkingSlots = new StatefulSlotInfo[]{ … }` manifest on the `BehaviorDefinition`. It **already dedups by slot key** via a `Dictionary<int,…> slotsBySeen` (line ~628, `if (slotsBySeen.ContainsKey(slotKey)) continue;`). **THIS is the method you change.**
2. **Runtime (`BehaviorIngressSystem.cs`):** on `AssignBehaviorEvent`, `ProvisionStatefulSlots(repo, entity, def.StatefulWorkingSlots)` (line ~240) provisions/attaches one partition slot per manifest entry. `AttachSlotsToMemory` (line ~669) is already idempotent per slot key. So **the manifest's entry count === the number of provisioned slots.**

Both emit sites currently compute the key with the **2-arg Node-scope** overload:
`ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId)` — i.e. `FNV-1a(assetId, nodeVisualId)`, unique per node.

**The whole of S3-4:** make the *manifest* emit use the **scope-aware** key. When three nodes bind the same `Behavior`-scoped variable, S3-2's `Behavior` key = `FNV-1a(assetId, variableId)` is **identical** for all three → the existing `slotsBySeen` dedup collapses them into **one** manifest entry → provisioning allocates **one** shared slot. Node-scoped variables keep their per-node key (byte-identical) → unchanged.

---

## Concrete change (exactly one production file: `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs`)

### 1. Add a private scope-resolution helper
Add a helper that, given a stateful action node's `ExpressionTargetField`, resolves the bound variable's `WorkingStateScope` from the asset blackboard and returns the scope-aware slot key. Place it near `ComputeStatefulSlotKey` (after line ~250):

```csharp
/// <summary>
/// S3-4: resolves the scope-aware stateful slot key for a binding. Looks up the bound
/// variable (by Name == <paramref name="targetField"/>) in the asset blackboard; a
/// State-role variable contributes its declared <see cref="WorkingStateScope"/>. Any other
/// case (Input role, variable absent, null target) falls back to <see cref="WorkingStateScope.Node"/>,
/// which yields the byte-identical legacy per-node key (Slice-2 untouched).
/// </summary>
private static int ResolveStatefulSlotKey(BehaviorTreeAssetDto dto, string? targetField, Guid nodeVisualId)
{
    var scope = WorkingStateScope.Node;
    if (!string.IsNullOrEmpty(targetField) && dto.Blackboard?.Variables != null)
    {
        foreach (var v in dto.Blackboard.Variables)
        {
            if (string.Equals(v.Name, targetField, StringComparison.Ordinal) && v.Role == BlackboardVariableRole.State)
            {
                scope = v.Scope;
                break;
            }
        }
    }
    return ComputeStatefulSlotKey(dto.AssetId, scope, nodeVisualId, targetField ?? string.Empty);
}
```
- The 4-arg `ComputeStatefulSlotKey` for `Node` scope already delegates to the 2-arg overload (verified in S3-2), so the fallback is **byte-identical** to today.
- `BlackboardVariableRole` / `WorkingStateScope` live in `Hrot.AiEditor.Persistence` (same assembly). Add the `using`/namespace if not already present.

### 2. Use the helper in `EmitStatefulWorkingSlotsArray` only
Replace the manifest's key computation (currently around line 638):
```csharp
// BEFORE
int slotKey = ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId);
// AFTER
int slotKey = ResolveStatefulSlotKey(dto, p.ExpressionTargetField, actNode.VisualId);
```
The existing `slotsBySeen` dedup then collapses co-bound `Behavior` nodes to one entry. **Change nothing else in this method** (PayloadSize/StructureHash/typeof/NodeLabel emission stay as-is).

### 3. Do NOT change `EmitStatefulActionThunks`
Leave the thunk's `int slotKey = ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId);` (line ~534) and its baked `const int __slotKey` exactly as they are. Making the *thunk* scope-aware is S3-3 (BATCH-15). (Consequence: for a Behavior-scoped asset, the thunk momentarily looks up the Node key while the manifest provisions the Behavior key — this is fine because **your tests do not tick**; they only assign + count slots. S3-3 reconciles the thunk next.)

---

## Success conditions (implement EXACTLY these two named tests — do not invent others)

Both are **runtime provisioning tests** exercising the full emit→generate→compile→provision pipeline, so they must live where both the emitter (`Hrot.AiEditor.Persistence`) and the runtime (`Fdp.Toolkit.Behavior`) are referenceable: the **`Hrot.AiEditor.Generators.Tests`** project (the same project as `Demos/T20_MultiStateful_ProofTests.cs` — mirror its pipeline exactly). Put both in a new file `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Demos/S3_SharedSlotProvisioningTests.cs`.

**Build the asset in-memory and serialize it with `BTreeJsonServices.Serialize(dto)`** (namespace `Hrot.AiEditor.Persistence.BTree`) to get valid JSON with correct `role`/`scope` string encoding (it uses `JsonStringEnumConverter`). Feed that JSON to `BTreeJsonGenerator` via the T20 `GenerateBTreeSourcesWithBehaviorsRef` + `CompileMultiAndLoad` + registrar-scan pattern (copy those helpers; they are `private` in T20 — replicate them or factor a shared helper, your call, but do not modify T20).

Reuse the existing demo primitive so compilation resolves with no new demo code:
- Method FQN: `Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor` (a real 4-param `ThreeParamReusableStateful` method).
- Params DTO type id: `Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorParams` (size 4).
- WorkingStateTypeId: `Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState`.

### Test 1 — `Assign_BehaviorScoped_ProvisionsOneSlot_ForSharedVar`
- Asset: managed blackboard with **one** variable `sharedCursor` (type `DemoCursorParams`, `Role = State`, `Scope = Behavior`). Topology: Root → Sequence → **three** Action nodes (distinct `VisualId`s), each binding `Action_AdvanceCursor` to `ExpressionTargetField = "sharedCursor"`, `DelegateShape = ThreeParamReusableStateful`, `WorkingStateTypeId = DemoCursorState`.
- Generate → compile → register into a live `BehaviorRegistry`.
- Assert `def.StatefulWorkingSlots` is non-null and **`Count == 1`** (three co-bound Behavior nodes ⇒ one manifest entry). Assert the single entry's `SlotKey == ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, Guid.Empty, "sharedCursor")`.
- Assign via `BehaviorIngressSystem` (mirror T20 setup: create world with all three `BlueprintBlackboard*` tiers, add `BehaviorState`/`BrainBlackboard`/`BrainBTreeState`, publish `AssignBehaviorEvent`, `SwapBuffers`, `Execute`).
- Assert the provisioned tier's slot count is **exactly 1**: `BlueprintBlackboardPartitions.GetSlotCount(mem) == 1`, and `TryGetSlotOffset(mem, behaviorKey, out _)` is true.

### Test 2 — `Assign_MixedNodeAndBehaviorScope_SlotCountsCorrect`
- Asset: managed blackboard with **three** variables:
  - `localA` — type `DemoCursorParams`, `Role = State`, `Scope = Node`.
  - `localB` — type `DemoCursorParams`, `Role = State`, `Scope = Node`.
  - `shared` — type `DemoCursorParams`, `Role = State`, `Scope = Behavior`.
  - Topology (4 Action nodes under a Sequence): node1→`localA`, node2→`localB`, node3→`shared`, node4→`shared` (the two `shared` bindings prove Behavior sharing). All bind `Action_AdvanceCursor` / `ThreeParamReusableStateful` / `WorkingStateTypeId = DemoCursorState`.
- Assert `def.StatefulWorkingSlots.Count == 3` (two distinct Node keys + one shared Behavior key; the two `shared` nodes dedup to one).
- Assign → assert `GetSlotCount(mem) == 3`.

> If `GetSlotCount` is not already public on `BlueprintBlackboardPartitions`, use the same slot-count accessor T20/`BehaviorIngressStatefulTests` use (`BlueprintBlackboardPartitions.GetSlotCount(mem)` is referenced in `BehaviorIngressStatefulTests` — reuse it).

---

## Byte-identity / regression gate (mandatory)
- **Node-scoped assets must stay byte-identical.** The whole existing corpus is Node scope (or Input), so `ResolveStatefulSlotKey` returns the legacy key for every one of them → the emitted manifest is unchanged.
- Re-run these and confirm no net-new failures:
  - `Hrot.AiEditor.Persistence.Tests` — **byte-identity gate** (CombatShowcase / SampleScout) must stay green.
  - `Hrot.AiEditor.Generators.Tests` — including `Demos/T20_MultiStateful_ProofTests` (must still emit its 2 per-node slots and pass unchanged) and `Bridge/StatefulSlotKeyTests` (S3-2, still 6/6+).
- Pre-existing non-regressions you may ignore: the 2 `MigrationEquivalenceTests` JSON byte-stability cases in `Hrot.AiEditor.Generators.Tests` (DEBT-TRACKER). Do NOT "fix" them; just confirm they were already failing before your change.

## Constraints & guardrails
- Production change is **one file, two edits** (`BTreeBridgeEmitCore.cs`: add helper + one call-site swap). Do NOT modify `EmitStatefulActionThunks`, `BehaviorIngressSystem.cs`, `BlueprintBlackboardPartitions`, `StatefulSlotInfo`, or any authoring UI. (Threading `Role`/`Scope` into `StatefulSlotInfo` is S3-7, a later batch — not here.)
- Do NOT commit any new `.btree.json` asset file — the tests build the DTO in-memory and serialize it in-process.
- Do NOT touch the parked-red items (D-8 Presentation, D-13 DistributedTank). Leave them failing.

## Environment
- Build/test: `dotnet test <proj>.csproj -c Debug --nologo`; filter with `--filter "FullyQualifiedName~S3_SharedSlotProvisioning"`.
- If `NU1301 "local source './nugets' doesn't exist"` → `mkdir -p ./nugets` in the worktree root first.
- Never run two `dotnet build`/`dotnet test` concurrently in the same tree (CS2012 DLL lock) — run serially.
- Run `dotnet build-server shutdown` before the codegen-exercising test runs.

## Report back
1. The exact diff of `BTreeBridgeEmitCore.cs` (helper + call-site).
2. The new test file and both tests' pass/fail with counts.
3. Confirmation `def.StatefulWorkingSlots.Count` is 1 (Test 1) and 3 (Test 2), and `GetSlotCount` matches.
4. Before/after pass counts for `Hrot.AiEditor.Generators.Tests` and `Hrot.AiEditor.Persistence.Tests` (byte-identity gate green; T20 unchanged).
5. Confirm you did NOT touch `EmitStatefulActionThunks` or any file outside the two named.
6. `git diff --stat` from the worktree so out-of-scope changes are visible. Verify edits persisted on disk before reporting done.
</content>
</invoke>
