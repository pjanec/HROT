# BATCH-03 Report — Replay Browser Frankenstein

**Batch:** BATCH-03
**Date completed:** 2025-07-14
**Status:** All tasks complete. Build: clean. Tests: 21/21 pass.

---

## Task Summary

| Task ID | Description | Status |
|---------|-------------|--------|
| C0 | Close D01, D02, D03 in DEBT-TRACKER.md | Done |
| C1 | Fix D01 — add SeekAll offset-displacement test | Done |
| C2 | Fix D02 — SetNodeOffset throws for unknown nodeId | Done |
| RBF-P3T5 | Implement `TransientMasterBuilder.Build` (global entity merge) | Done |
| RBF-P3T7 | Local-entities provider injection into transient master repo | Done |

---

## Files Changed

### Production

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs` | Added guard in `SetNodeOffset`: throws `ArgumentOutOfRangeException` for unknown nodeId. |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/TransientMasterBuilder.cs` | **New file.** Implements `Build(FederatedReplayManager)`: correlation by `NetworkIdentity.Value`, consensus extraction (primary-owner wins on conflict), local-entities provider injection, relational handle remapping via `FederatedGuidResolver`. |

### Tests

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests.cs` | Two new tests: `RBF_P2T1_SeekAll_WithNodeOffset_NodeLandsOnDifferentState` (C1/D01) and `RBF_P2T1_SetNodeOffset_UnknownNodeId_Throws` (C2/D02). Helper `MakeTwoFrameRecording` added. |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/TransientMasterBuilderTests.cs` | **New file.** 12 tests across P3T5 (6) and P3T7 (6). |

---

## Test Results

```
Test Run Successful.
Total tests: 21
     Passed: 21
```

Tests run:

- `RBF_P2T1_SeekAll_WithNodeOffset_NodeLandsOnDifferentState`
- `RBF_P2T1_SetNodeOffset_UnknownNodeId_Throws`
- `RBF_P3T5_Build_TwoNodes_SplitAuthority`
- `RBF_P3T5_Build_GhostExcluded`
- `RBF_P3T5_Build_RelationalHandleRemapped`
- `RBF_P3T5_Build_MissingTargetResolvesToEntityNull`
- `RBF_P3T5_Build_SplitBrainConflict_PrimaryOwnerWins`
- `RBF_P3T5_Build_RebuildableCheaply`
- `RBF_P3T7_LocalEntities_ProviderEntitiesAppearInMaster`
- `RBF_P3T7_LocalEntities_NonProviderLocalsExcluded`
- `RBF_P3T7_LocalEntities_UseFullPresenceMask_NotAuthorityMask`
- `RBF_P3T7_LocalEntities_GlobalHandleToLocalResolves`
- `RBF_P3T7_LocalEntities_SwitchProviderRebuilds`
- `RBF_P3T7_SyntheticGuid_ParseableAndDeterministic`
- (plus 7 pre-existing `RBF_P2T1` tests that continued passing)

---

## Build

```
Build succeeded.  0 Error(s).  0 Warning(s).
```

Full solution: `IOS-IG-SimHost.sln`.

---

## Design Questions (Q1-Q5)

**Q1: Does `BitMask512.BitwiseAnd` mutate in place or return a copy?**

It mutates `this` in place.  Always copy the struct before calling:

```csharp
var candidate = presenceMask;        // copy
candidate.BitwiseAnd(authorityMask); // mutates candidate, not presenceMask
```

**Q2: What happens if `NetworkIdentity` is not registered at correlation time?**

`ComponentTypeRegistry.GetId(typeof(NetworkIdentity))` returns -1.  The correlation loop
is guarded by `if (netIdTypeId >= 0)`, so it is skipped entirely.  No global entities are
correlated; all nodes' entities are treated as local (which is correct for a setup where
no network identity types have been registered).

**Q3: If a translator consumes A+B but the extraction mask only has bit A set, does the translator run and does B appear in the output?**

The translator runs if the intersection of its consumed mask and the extraction mask is
non-empty (has A).  After the translator runs, `ClearConsumed` clears both A and B bits
from `remainingMask`.  Whether B appears in the output depends on the translator's
`Extract` implementation — if it writes a JSON entry for B, B appears; the mask clearing
only prevents the auto-serializer from writing B a second time.

**Q4: Must `resolver.SetSaveMap` be called before `SerializeEntity`?**

Yes.  `FederatedGuidResolver.SetSaveMap(saveMap)` must be called before the serialization
loop, because `SerializeEntity` calls the resolver to convert `Entity` values (in
relational fields such as `GuidedTarget.TargetId`) to string keys.  The save map must
also include entries for local entities before serializing global entities that reference
them, otherwise those references resolve to `"null"`.

**Q5: Is `MD5.HashData` available in .NET 8 BCL without extra packages?**

Yes.  `System.Security.Cryptography.MD5.HashData(byte[])` is a static BCL method
introduced in .NET 5.  It is appropriate here for deterministic key generation
(`MakeSyntheticKey`) because the goal is a short, stable, collision-unlikely identifier,
not cryptographic security.

---

## Non-obvious Implementation Notes

### AutoSerializer delegate lifetime and `EntityInlineComp`

`FdpAutoSerializer.Build()` compiles per-type delegates for every type in
`ComponentTypeRegistry` at the moment it is called.  It throws
`InvalidOperationException` for any snapshotable type that has an `[InlineArray]` or
fixed-buffer field with element type `Entity` (e.g., the test-only `EntityInlineComp`,
ComponentId 228).

`RepositoryPriming.RegisterDiscoveredComponents` scans all loaded assemblies, which in
tests includes the test assembly — and thus discovers `EntityInlineComp`.  Calling
`AutoSerializer.Build()` after `RepositoryPriming` would therefore throw in tests.

**Fix applied:**

1. `TransientMasterBuilder.Build()` no longer calls `_serializer.AutoSerializer.Build()`.
   The delegates compiled at `ScenarioSerializerBuilder.Build()` time are sufficient;
   `RepositoryPriming.RegisterDiscoveredComponents(transientRepo)` still populates the
   transient repo's component tables for all discovered types.

2. `TransientMasterBuilderTests` constructor registers the four needed component types
   into a throw-away `EntityRepository` **before** calling
   `ScenarioSerializerBuilder.Build()`.  This ensures the AutoSerializer's delegates are
   compiled for exactly those types, without `EntityInlineComp` in the registry yet.

In production (no `ComponentTypeRegistry.Clear()`, no test types loaded),
`ScenarioSerializerBuilder.Build()` is called at startup with all production types
registered, so the AutoSerializer has full coverage from the start.

### `FederatedGuidResolver` save-map scope

Each node gets its own `FederatedGuidResolver`.  Before calling `SerializeEntity` for a
node's authority entities, `BuildSaveMapForNode` populates the resolver's save map with:

- Global entities on that node (keyed by `NetworkIdentity.Value` encoded as a Guid string)
- Local entities from the provider node (keyed by `MakeSyntheticKey`)

This ensures that `GuidedTarget.TargetId` fields that reference local entities on the
provider resolve correctly even when serializing from a different node.
