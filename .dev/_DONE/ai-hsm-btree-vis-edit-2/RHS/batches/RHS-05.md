# RHS-05 — Attach regions to their parent parallel state (region dividers render)

**Workstream:** RHS (../RHS-PLAN.md). **Layer:** Hrot.Hsm.Editor JSON→model mapper. **Depends:** none.

## Problem (root cause confirmed)

Parallel state `ParallelWork` shows no dashed region dividers. NodeEditor's `ContainerRenderer.DrawRegions` already draws dashed dividers + headers, but only when `container.Regions.Count > 0`. The editor `StateNode.Regions` getter returns empty unless `RegionNodes.Count > 0` (`Model/HsmAsset.cs` ~L796).

`HsmAssetMapper.ToModel()` (`Persistence/HsmAssetMapper.cs:244-263`) builds the `RegionNode` objects into the **asset-level** `regionNodes` list (→ `asset.AllRegions`) but **never attaches them to the owning parallel state's `RegionNodes`** collection. (The assembly-loader path `HsmAssetProjector` does attach, via the kernel blob's `RegionDef.ParentStateIndex` — but the JSON `RegionNodeDto` has no parent reference.) So `RegionNodes.Count == 0` on every state → `Regions` empty → dividers never draw.

## Fix

In `HsmAssetMapper.ToModel()`, **after** the region-build loop (after line 263), add a pass that attaches each region to the parent state of its `InitialChild`:

```csharp
// Attach each region to the parent state of its initial child so parallel states
// expose IContainerNodeModel.Regions (→ NodeEditor draws region dividers). The flat
// JSON region list carries no parent ref; InitialChild.Parent is the unambiguous owner.
// (RHS-05)
foreach (var region in regionNodes)
{
    var owner = region.InitialChild?.Parent;
    if (owner != null && !owner.RegionNodes.Contains(region))
        owner.RegionNodes.Add(region);
}
// Keep each owner's regions ordered by RegionIndex (divider layout depends on order).
foreach (var s in stateNodes)
    if (s.RegionNodes.Count > 1)
        s.RegionNodes.Sort((a, b) => a.RegionIndex.CompareTo(b.RegionIndex));
```

Notes / requirements:
- Do NOT remove regions from `regionNodes` / `asset.AllRegions` — `ToDto` emits from `AllRegions` (line 88-89), so leaving them there preserves byte-stable round-trip. This pass is purely additive.
- Regions whose `InitialChild` is null, or whose owner is the synthetic root / a non-parallel state, simply don't produce dividers (the `Regions` getter is gated on `IsParallel`). That's fine.
- Confirm the child's `RegionIndex` is actually populated on the editor `StateNode` from `RegionNodeDto`/state DTO during the state-build loop (so `GetRegionIndexForChild` returns 0/1/2 for WorkA/B/C). If it is NOT plumbed through, that's part of this fix — wire `state.RegionIndex = sDto.RegionIndex;` in the state-build loop. Verify and report which was the case.
- Verify the exact `RegionNode` API (ctor, `RegionNodes` property name, `InitialChild`, `Parent` on `StateNode`) by reading `Model/HsmAsset.cs` before editing — match it exactly.

## Scope

- Primary: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Persistence/HsmAssetMapper.cs` (and `Model/HsmAsset.cs` only if `StateNode.RegionIndex` isn't being populated from the DTO).
- Do NOT touch: NodeEditor (`ContainerRenderer`/`RegionLayoutComputer` already work), renderers (RHS-02), theming (RHS-04), the showcase JSON (RHS-06).

## Tests

- Add a unit test (HSM editor test project): build a DTO with a parallel state owning N regions (each with an `InitialChildStableId` pointing at a distinct child carrying that `RegionIndex`), run `HsmAssetMapper.ToModel(dto)`, and assert the parallel `StateNode` has `RegionNodes.Count == N`, ordered by `RegionIndex`, and that `Regions` (the `IContainerNodeModel` descriptor list) is non-empty with correct indices. Also assert `GetRegionIndexForChild` returns the right index per child.
- Run the existing round-trip / byte-stability tests to prove serialization is unaffected.

## Verification (run + paste raw output)

1. `dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj -c Debug -v q -nologo` → 0 errors.
2. `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj -c Debug --nologo -v q` → ≥464 passing, 0 failing.
3. Round-trip guard: `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Persistence.Tests/Hrot.AiEditor.Persistence.Tests.csproj -c Debug --nologo -v q` — report counts (HSM region round-trip must still pass; note any pre-existing failures, but there should be none here).

## Report back

Whether `StateNode.RegionIndex` was already populated or you had to wire it; diff summary; raw build + test output for all three. Do NOT commit — lead reviews & commits.
