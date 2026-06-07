# BP-4: Unify the editor's NodePinSchema onto the compiler's INodeRegistry (kill the parallel static pin-truth)
**Goal (architect-confirmed):** the editor's `NodePinSchema` currently maintains a **parallel built-in table**
of static pin shapes — technical debt that can drift from the compiler. BP-2 made `BuiltInNodeRegistry`
(`INodeRegistry.GetStaticPins`) the compiler's single source of truth. BP-4 makes `NodePinSchema` **delegate to
that registry** for STATIC shapes, so there is one source and it can't drift. Pure refactor — **no change to the
editor's pin output**.

## Scope (precise)
- **DELEGATE to `BuiltInNodeRegistry.Instance.GetStaticPins(node)`** the node kinds whose pins are PURELY STATIC
  (no asset/graph/catalog/peer context): `BranchNode`, `SequenceNode`, `LiteralNode`, `CastNode`,
  `LatentDelayNode`, `ArrayMakeNode`, `ArrayGetNode`, `ScoreDecisionNode`, `ReadRankedResultNode`, the exec-only
  kinds (`WaitForChannelNode`, `WaitForEventNode`, `CallEventDispatcherNode`, `BindEventDispatcherNode`,
  `SpawnEqsSensorNode`, `PartitionElementsNode`, `AssignRolesNode`, `AdvancePhaseNode`, `AcquireSlotNode`),
  `WhenNode`, `ReadEqsResultNode`. (The registry already returns the full shape for these — verified.)
- **KEEP the existing editor-side computation** for the DYNAMIC kinds that need asset/graph/catalog/peer context:
  `EventEntryNode` (Graph.Inputs), `ReturnNode` (Graph.Outputs), `GetVariableNode`/`SetVariableNode`
  (asset.Variables), `FunctionCallNode` (CLR reflection / target graph), `ChannelCommandNode`
  (IChannelCommandCatalog), `CallCustomEventNode` (asset.CustomEvents), `CallPeerBlueprintNode`
  (peerSignatureLookup). Optionally have these pull their exec skeleton from the registry too, but only if it does
  NOT change output — the priority is the static cases.

## Implementation
- Add a small converter in `NodePinSchema`: `PinSchema` (Name, Direction, IsExec, TypeId) → editor `Pin`
  (assign `Id = Guid.NewGuid()`, `TypeRef = new BlueprintTypeRef { TypeId = schema.TypeId }`, copy Name/
  Direction/IsExec). Pin order MUST be preserved exactly (order is load-bearing for `BlueprintGraphModel`'s
  link-GUID positional assignment).
- Replace the delegated cases in `NodePinSchema.GetCanonicalPins`'s switch with a call to the registry +
  converter. **Delete the now-dead duplicate per-kind static helper methods** in NodePinSchema (`BranchPins`,
  `SequencePins`, `CastPins`, `LatentDelayPins`, `ArrayMakePins`, `ArrayGetPins`, `ScoreDecisionPins`,
  `ReadRankedResultPins`, `ExecInOut` if fully replaced, etc.) so there is truly one source. Keep helpers that
  the dynamic cases still use (`MakeExec`/`MakeData`, the dynamic ones).
- `NodePinSchema` is net8 and already (transitively) references `Hrot.Blueprints.Compiler` — `BuiltInNodeRegistry`
  is reachable. Confirm the using/reference.

## Success Criteria
- [ ] NodePinSchema's static-kind cases come from `BuiltInNodeRegistry.GetStaticPins`; the duplicate static
      tables are removed. Dynamic kinds unchanged.
- [ ] **No editor pin-output change:** `Host/NodePinSchemaEnrichmentTests` and `Host/BlueprintGraphModelTests`
      (Hrot.Blueprints.Tests) stay green (same pins, same order, same link resolution). Add an assertion (or a
      small test) that for a representative static node the editor pins match the registry shapes (name/dir/exec/
      type, in order) — locking the single-source invariant.
- [ ] Build `IOS-IG-SimHost.sln` 0 errors / 0 new warnings.
- [ ] No new test regressions: `Hrot.Blueprints.Tests` stays at the SAME 7 pre-existing failures (3 golden +
      2 snapshot DEBT-006 + ConditionSummary + AllocationFree) — list the exact final failure set; `EditorSubsystemBoot`
      10/10. Report exact counts. Do NOT claim 0 regressions without the explicit failure-set comparison.
- [ ] Report → `.dev/blueprint-compile-fix/BP-4-REPORT.md`.

## Constraints
Branch `blueprint-integ-1`. Do NOT change pin ORDER or shapes (output must be identical — this is a
source-unification refactor, not a behavior change). Do NOT touch the user's WIP (RecipeCreateModal.cs,
AssetBrowserWindow.cs, EditorSubsystem.cs) or the BP-2 compiler files beyond what's needed to reference the
registry. Do NOT regenerate golden snapshots. Do NOT commit (the lead commits). If the running editor locks
dlls, report it.
