using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// BATCH-14 / AIE-052: verifies that <see cref="BlackboardAuthoringWindow.BuildViewModel"/>
/// surfaces a budget-overflow warning when aggregated DTO requirements push the total
/// packed bytes past <see cref="BlackboardBinPacker.MaxInlineBytes"/>.
/// All tests are headless (no ImGui calls).
/// </summary>
public sealed class Batch14AggregatorBinPackTests
{
    // ---- stub asset ---------------------------------------------------------

    /// <summary>
    /// A minimal blackboard-managed asset carrying exactly the variables we set up.
    /// Derives byte sizes from the actual CLR struct layout, not magic numbers.
    /// Default interface implementations handle optional methods.
    /// </summary>
    private sealed class ManagedAsset : IEditableAsset, IBlackboardManagedAsset
    {
        public Guid   AssetId        { get; } = Guid.NewGuid();
        public string Name           { get; set; } = "TestAsset";
        public AssetKind Kind        => AssetKind.BTree;
        public string SourceFilePath => "/test.cs";
        public bool   IsDirty        => false;
        public bool   IsEditorOwned  => true;

        public bool IsBlackboardEditorManaged { get; set; } = true;
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; set; }
            = Array.Empty<BlackboardVariableEntry>();

#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067

        // Required interface members (no defaults).
        public void AddVariable(BlackboardVariableEntry entry)                                        { }
        public void RemoveVariable(string name)                                                       { }
        public void UpdateVariableComment(string name, string? comment)                               { }
        public void MoveVariable(int src, int dst)                                                    { }
        public void RenameVariable(string old, string @new)                                           { }
        public void RemoveVariables(IReadOnlyList<string> names)                                      { }
        public int   CountNodesReferencingVariable(string name)                              => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string name)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string name, BlackboardAliasBinding binding)                             { }
        public void RemoveAlias(string name, Guid assetId, Guid elemId)                               { }
    }

    // ---- DTO stub with known size -------------------------------------------

    // 104-byte struct (8-byte aligned). Puts 13 bytes over the 100-byte inline ceiling.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BigDto
    {
        public long A, B, C, D, E, F, G, H, I, J, K, L, M; // 13 × 8 = 104 bytes
    }

    // 8-byte struct. Stays within the 100-byte limit.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SmallDto
    {
        public long X; // 8 bytes
    }

    // ---- tests --------------------------------------------------------------

    [Fact]
    public void BuildViewModel_AggregatedRequirements_FitInBudget_NoWarning()
    {
        // One managed variable (8 bytes) + one aggregated requirement (8 bytes) = 16 bytes total.
        // 16 < 100 → no warning.
        var asset = new ManagedAsset();
        asset.BlackboardVariables = new[]
        {
            new BlackboardVariableEntry("hp", typeof(SmallDto), null),
        };

        var aggregation = new AggregationResult(
            new[] { new DtoRequirement(typeof(SmallDto), "Tree > ActionNode (Foo)", Guid.NewGuid(), Guid.NewGuid()) },
            Array.Empty<AggregationWarning>());

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggregation);

        Assert.True(vm.IsBlackboardEditorManaged);
        Assert.Equal(PackWarning.None, vm.Warning);
        Assert.True(vm.TotalInlineBytes <= BlackboardBinPacker.MaxInlineBytes,
            $"Expected total inline bytes <= {BlackboardBinPacker.MaxInlineBytes}, got {vm.TotalInlineBytes}");
    }

    [Fact]
    public void BuildViewModel_AggregatedRequirements_DontFitInline_RequiresHeavyComponent()
    {
        // Asset has no explicit variables. Aggregated requirement introduces BigDto (104 bytes).
        // 104 > 100 → the aggregated variable spills to the heavy tier.
        // RequiresHeavyComponent must be true; PackWarning must be None (heavy budget not exceeded).
        var asset = new ManagedAsset
        {
            BlackboardVariables = Array.Empty<BlackboardVariableEntry>()
        };

        var aggregation = new AggregationResult(
            new[]
            {
                new DtoRequirement(
                    typeof(BigDto),
                    "Subtree > ActionNode (FireAtTarget)",
                    Guid.NewGuid(),
                    Guid.NewGuid())
            },
            Array.Empty<AggregationWarning>());

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggregation);

        Assert.True(vm.IsBlackboardEditorManaged);
        // BigDto (104 bytes) > MaxInlineBytes (100) → spills to heavy.
        Assert.True(vm.RequiresHeavyComponent,
            "A BigDto that does not fit inline should cause RequiresHeavyComponent to be true");
        // Heavy budget is 928; 104 is well within it — no warning.
        Assert.Equal(PackWarning.None, vm.Warning);
    }

    [Fact]
    public void BuildViewModel_MasterVars_OverflowInlineBudget_SurfacesInlineWarning()
    {
        // Fill master vars so the inline budget is exceeded (13 × 8-byte long = 104 bytes > 100).
        var asset = new ManagedAsset
        {
            BlackboardVariables = Enumerable.Range(0, 13)
                .Select(i => new BlackboardVariableEntry($"var{i}", typeof(long), null))
                .ToArray()
        };

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: null);

        Assert.True(vm.IsBlackboardEditorManaged);
        Assert.Equal(PackWarning.InlineMemoryExceeded, vm.Warning);
        Assert.True(vm.TotalInlineBytes > BlackboardBinPacker.MaxInlineBytes,
            $"Expected TotalInlineBytes > {BlackboardBinPacker.MaxInlineBytes} (got {vm.TotalInlineBytes})");
    }

    [Fact]
    public void BuildViewModel_NoAggregation_Null_DoesNotSurfaceUnboundRequirements()
    {
        // Without an aggregation result there should be no unbound requirements.
        var asset = new ManagedAsset
        {
            BlackboardVariables = new[]
            {
                new BlackboardVariableEntry("ammo", typeof(SmallDto), null),
            }
        };

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: null);

        Assert.True(vm.IsBlackboardEditorManaged);
        Assert.Empty(vm.UnboundRequirements);
        Assert.Equal(PackWarning.None, vm.Warning);
    }

    [Fact]
    public void BuildViewModel_AggregatedRequirements_UnboundRows_ProvideProvenance()
    {
        // Verify that the UnboundRequirements in the view-model carry the
        // DtoType and provenance path from the aggregation result.
        var asset = new ManagedAsset
        {
            BlackboardVariables = Array.Empty<BlackboardVariableEntry>()
        };

        var providerId = Guid.NewGuid();
        var nodeId     = Guid.NewGuid();
        const string provenancePath = "CombatTree > Sequence > FireAction (Ai.Actions.Fire)";

        var aggregation = new AggregationResult(
            new[]
            {
                new DtoRequirement(typeof(SmallDto), provenancePath, providerId, nodeId)
            },
            Array.Empty<AggregationWarning>());

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggregation);

        // There should be exactly one unbound requirement (no alias defined for it yet).
        Assert.Single(vm.UnboundRequirements);
        var req = vm.UnboundRequirements[0];
        Assert.Equal(typeof(SmallDto), req.DtoType);
        Assert.Equal(provenancePath, req.RequiredByPath);
        Assert.Equal(providerId, req.RequiringAssetId);
        Assert.Equal(nodeId, req.RequiringElementId);
    }
}
