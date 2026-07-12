using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// Tests for HsmValidator Rule 8 (S2-4): ConcurrentStatefulSubtree.
/// Verifies that running the same stateful Subtree in ≥2 orthogonal parallel regions
/// is a hard validation error, while a stateless Subtree across regions is allowed.
/// </summary>
public sealed class HsmValidatorStatefulSubtreeTests
{
    // ---- Helpers ------------------------------------------------------------

    private static HsmAsset MakeAsset(
        StateNode rootState,
        List<StateNode> allStates,
        List<RegionNode>? allRegions = null)
    {
        return new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            rootState,
            allStates,
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            allRegions ?? new List<RegionNode>(),
            new List<EventDefinition>());
    }

    /// <summary>
    /// Builds a parallel composite with two direct child states in different regions.
    /// Returns (asset, composite, child0InRegion0, child1InRegion1).
    /// Mirrors <c>HsmValidatorBlackboardConflictTests.MakeParallelAsset</c>.
    /// </summary>
    private static (HsmAsset Asset, StateNode Parallel, StateNode Child0, StateNode Child1)
        MakeParallelAsset()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        var child0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var child1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(child0);
        parallel.Children.Add(child1);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, child0, child1 },
            new List<RegionNode> { rn0, rn1 });

        return (asset, parallel, child0, child1);
    }

    // ---- Tests ---------------------------------------------------------------

    /// <summary>
    /// S2-4 required test #1.
    /// A stateful Subtree placed in both region 0 and region 1 → hard error.
    /// </summary>
    [Fact]
    public void SameStatefulSubtree_InTwoParallelRegions_HardErrors()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        // Both children reference the SAME subtree asset.
        var subtreeId = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeId;
        child1.SubtreeAssetId = subtreeId;

        // Resolver says this subtree is stateful.
        var validator = new HsmValidator(isStatefulSubtree: id => id == subtreeId);
        var diagnostics = validator.Validate(asset, blackboard: null);

        // Exactly one ConcurrentStatefulSubtree diagnostic, with severity Error.
        var diag = Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
        Assert.Equal(HsmDiagnosticSeverity.Error, diag.Severity);
        // The target is the parallel composite.
        Assert.Contains(parallel.StableId, diag.TargetStableIds);
    }

    /// <summary>
    /// S2-4 required test #2.
    /// A stateless Subtree in both regions → no ConcurrentStatefulSubtree diagnostic.
    /// </summary>
    [Fact]
    public void StatelessSubtree_InParallelRegions_Allowed()
    {
        var (asset, _, child0, child1) = MakeParallelAsset();

        // Both children reference the SAME subtree asset.
        var subtreeId = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeId;
        child1.SubtreeAssetId = subtreeId;

        // Resolver says this subtree is STATELESS (returns false).
        var validator = new HsmValidator(isStatefulSubtree: _ => false);
        var diagnostics = validator.Validate(asset, blackboard: null);

        // No ConcurrentStatefulSubtree diagnostic produced.
        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    /// <summary>
    /// S2-4 optional test #3 (feasibility confirmed).
    /// The same stateful Subtree referenced TWICE within ONE region → no error
    /// (only cross-region concurrency is the hazard).
    /// </summary>
    [Fact]
    public void SameStatefulSubtree_SameRegion_NoError()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        // Both children are in region 0 — only one region contains the subtree.
        var subtreeId = Guid.NewGuid();
        var child0a = new StateNode("C0a") { IsInitial = true, RegionIndex = 0, Parent = parallel, SubtreeAssetId = subtreeId };
        var child0b = new StateNode("C0b") { RegionIndex = 0, Parent = parallel, SubtreeAssetId = subtreeId };
        // Region 1 has a child with NO subtree reference.
        var child1  = new StateNode("C1")  { RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(child0a);
        parallel.Children.Add(child0b);
        parallel.Children.Add(child1);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, child0a, child0b, child1 },
            new List<RegionNode> { rn0, rn1 });

        // Resolver says this subtree is stateful.
        var validator = new HsmValidator(isStatefulSubtree: id => id == subtreeId);
        var diagnostics = validator.Validate(asset, blackboard: null);

        // Both usages are in region 0 → only one distinct region → no cross-region error.
        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    // ---- Additional boundary tests ------------------------------------------

    /// <summary>
    /// Default constructor (no isStatefulSubtree resolver provided) → no error even
    /// if the same subtree is in two regions, because the default resolver always
    /// returns false (all subtrees treated as stateless).
    /// </summary>
    [Fact]
    public void DefaultResolver_TreatsAllSubtreesAsStateless_NoError()
    {
        var (asset, _, child0, child1) = MakeParallelAsset();

        var subtreeId = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeId;
        child1.SubtreeAssetId = subtreeId;

        // Default constructor — no resolver supplied.
        var validator = new HsmValidator();
        var diagnostics = validator.Validate(asset, blackboard: null);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    /// <summary>
    /// Two DIFFERENT stateful Subtrees, one per region → no collision (distinct asset IDs
    /// produce distinct FNV-1a keys).
    /// </summary>
    [Fact]
    public void DifferentStatefulSubtrees_OnePerRegion_NoError()
    {
        var (asset, _, child0, child1) = MakeParallelAsset();

        var subtreeIdA = Guid.NewGuid();
        var subtreeIdB = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeIdA;
        child1.SubtreeAssetId = subtreeIdB;

        // Both are stateful, but they are different assets.
        var validator = new HsmValidator(isStatefulSubtree: _ => true);
        var diagnostics = validator.Validate(asset, blackboard: null);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    /// <summary>
    /// Existing BlackboardConflict check is not broken by the new check:
    /// a parallel composite with subtree refs in two regions emits only
    /// ConcurrentStatefulSubtree (not a spurious CrossRegionBlackboardConflict).
    /// </summary>
    [Fact]
    public void NewCheck_DoesNotEmit_CrossRegionBlackboardConflict()
    {
        var (asset, _, child0, child1) = MakeParallelAsset();

        var subtreeId = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeId;
        child1.SubtreeAssetId = subtreeId;

        var validator = new HsmValidator(isStatefulSubtree: id => id == subtreeId);
        // No blackboard passed → the CrossRegionBlackboardConflict check is skipped.
        var diagnostics = validator.Validate(asset, blackboard: null);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.CrossRegionBlackboardConflict);
        // But the new check fires.
        Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    // ---- S3-6: ConcurrentSharedScopeKey (shared-slot analogue) ----------------

    /// <summary>
    /// S3-6 required test #1.
    /// Two DIFFERENT subtree assets, one per orthogonal parallel region, that resolve to the SAME
    /// Behavior-scoped shared-slot key → hard error. Rule 8 (same-subtree) does NOT fire because the
    /// subtree asset ids differ; the new shared-scope-key rule catches the shared-slot race.
    /// </summary>
    [Fact]
    public void SharedBehaviorVar_WrittenInTwoParallelRegions_HardErrors()
    {
        var (asset, parallel, child0, child1) = MakeParallelAsset();

        // Different subtree assets in the two regions...
        var subtreeA = Guid.NewGuid();
        var subtreeB = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeA;
        child1.SubtreeAssetId = subtreeB;

        // ...but both bind the same Behavior-scoped variable → same shared scope key.
        const int sharedKey = 0x5150C0DE;
        var validator = new HsmValidator(
            sharedScopeKeys: id => (id == subtreeA || id == subtreeB)
                ? new[] { sharedKey }
                : System.Array.Empty<int>());
        var diagnostics = validator.Validate(asset, blackboard: null);

        var diag = Assert.Single(diagnostics, d => d.Code == HsmDiagnosticCode.ConcurrentSharedScopeKey);
        Assert.Equal(HsmDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains(parallel.StableId, diag.TargetStableIds);

        // The same-subtree rule must NOT fire (distinct subtree asset ids).
        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree);
    }

    /// <summary>
    /// S3-6 required test #2.
    /// The same shared Behavior variable used by SEQUENTIAL nodes (under a non-parallel composite)
    /// → no error. Behavior scope only races under orthogonal parallel regions; sequential use is
    /// the intended MVP pattern (e.g. Hill Attack) and must stay valid.
    /// </summary>
    [Fact]
    public void SharedBehaviorVar_SequentialNodes_Allowed()
    {
        // Non-parallel composite with two sequential children.
        var root = new StateNode("__root__");
        var seq  = new StateNode("Sequential") { Parent = root }; // IsParallel = false
        root.Children.Add(seq);

        var subtreeA = Guid.NewGuid();
        var subtreeB = Guid.NewGuid();
        var c0 = new StateNode("C0") { IsInitial = true, Parent = seq, SubtreeAssetId = subtreeA };
        var c1 = new StateNode("C1") { Parent = seq, SubtreeAssetId = subtreeB };
        seq.Children.Add(c0);
        seq.Children.Add(c1);

        var asset = MakeAsset(root, new List<StateNode> { seq, c0, c1 });

        // Both resolve to the same shared Behavior key — but they are sequential (not parallel).
        const int sharedKey = 0x5150C0DE;
        var validator = new HsmValidator(
            sharedScopeKeys: id => (id == subtreeA || id == subtreeB)
                ? new[] { sharedKey }
                : System.Array.Empty<int>());
        var diagnostics = validator.Validate(asset, blackboard: null);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentSharedScopeKey);
    }

    /// <summary>
    /// S3-6 boundary: two different subtrees with DIFFERENT shared keys, one per region → no error
    /// (distinct scope keys don't collide).
    /// </summary>
    [Fact]
    public void DifferentSharedKeys_OnePerRegion_NoError()
    {
        var (asset, _, child0, child1) = MakeParallelAsset();
        var subtreeA = Guid.NewGuid();
        var subtreeB = Guid.NewGuid();
        child0.SubtreeAssetId = subtreeA;
        child1.SubtreeAssetId = subtreeB;

        var validator = new HsmValidator(
            sharedScopeKeys: id => id == subtreeA ? new[] { 111 }
                                 : id == subtreeB ? new[] { 222 }
                                 : System.Array.Empty<int>());
        var diagnostics = validator.Validate(asset, blackboard: null);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == HsmDiagnosticCode.ConcurrentSharedScopeKey);
    }
}
