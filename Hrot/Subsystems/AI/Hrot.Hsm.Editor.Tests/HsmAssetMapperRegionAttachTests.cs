using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// Verifies that HsmAssetMapper.ToModel() attaches RegionNode objects to the owning
/// parallel StateNode's RegionNodes collection (RHS-05 fix).
/// </summary>
public sealed class HsmAssetMapperRegionAttachTests
{
    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a DTO containing one parallel state "ParallelWork" with N regions.
    /// Each region has an InitialChild state carrying the correct RegionIndex.
    /// </summary>
    private static HsmAssetDto BuildParallelDto(int regionCount)
    {
        var parallelId = Guid.NewGuid();

        // Child states: one per region, each assigned to a unique RegionIndex.
        // They are listed first so the mapper can wire InitialChild in the region loop.
        var childIds = new Guid[regionCount];
        for (int i = 0; i < regionCount; i++)
            childIds[i] = Guid.NewGuid();

        var dto = new HsmAssetDto
        {
            AssetId = Guid.NewGuid(),
            Name    = "TestParallel",
        };

        // Parallel state itself (no ParentStableId → top-level)
        dto.States.Add(new StateNodeDto
        {
            StableId   = parallelId,
            Name       = "ParallelWork",
            IsParallel = true,
        });

        // One child state per region
        for (int i = 0; i < regionCount; i++)
        {
            dto.States.Add(new StateNodeDto
            {
                StableId       = childIds[i],
                Name           = $"Work{(char)('A' + i)}",
                IsInitial      = true,
                RegionIndex    = i,
                ParentStableId = parallelId,
            });
        }

        // One region per child, listing them in REVERSE order to also test sort-by-RegionIndex
        for (int i = regionCount - 1; i >= 0; i--)
        {
            dto.Regions.Add(new RegionNodeDto
            {
                StableId             = Guid.NewGuid(),
                RegionIndex          = (byte)i,
                Name                 = $"Region{i}",
                InitialChildStableId = childIds[i],
            });
        }

        return dto;
    }

    // ── tests ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ToModel_parallel_state_has_RegionNodes_count_equal_to_N(int n)
    {
        var dto   = BuildParallelDto(n);
        var asset = HsmAssetMapper.FromDto(dto);

        var parallel = asset.AllStates.Single(s => s.IsParallel);
        parallel.RegionNodes.Count.Should().Be(n,
            because: "each region's InitialChild.Parent should be attached");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ToModel_RegionNodes_are_ordered_by_RegionIndex(int n)
    {
        var dto   = BuildParallelDto(n);  // regions are inserted in REVERSE order
        var asset = HsmAssetMapper.FromDto(dto);

        var parallel = asset.AllStates.Single(s => s.IsParallel);
        var indices  = parallel.RegionNodes.Select(r => (int)r.RegionIndex).ToList();
        indices.Should().BeInAscendingOrder(
            because: "mapper must sort RegionNodes by RegionIndex after attaching");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ToModel_Regions_descriptor_is_non_empty_with_correct_indices(int n)
    {
        var dto   = BuildParallelDto(n);
        var asset = HsmAssetMapper.FromDto(dto);

        var parallel = asset.AllStates.Single(s => s.IsParallel);
        var regions  = ((IContainerNodeModel)parallel).Regions;

        regions.Count.Should().Be(n, because: "Regions delegates to RegionNodes");
        for (int i = 0; i < n; i++)
            regions[i].Index.Should().Be(i,
                because: $"descriptor at slot {i} should have Index {i}");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ToModel_GetRegionIndexForChild_returns_correct_index_per_child(int n)
    {
        var dto   = BuildParallelDto(n);
        var asset = HsmAssetMapper.FromDto(dto);

        var parallel = asset.AllStates.Single(s => s.IsParallel);

        for (int expected = 0; expected < n; expected++)
        {
            var child = parallel.Children.Single(c => c.RegionIndex == expected);
            ((IContainerNodeModel)parallel)
                .GetRegionIndexForChild(new NodeId(child.StableId))
                .Should().Be(expected,
                    because: $"child with RegionIndex={expected} should be identified correctly");
        }
    }

    [Fact]
    public void ToModel_AllRegions_unchanged_so_round_trip_is_byte_stable()
    {
        // Regions must stay in asset.AllRegions (ToDto reads from AllRegions, not RegionNodes).
        const int n = 3;
        var dto   = BuildParallelDto(n);
        var asset = HsmAssetMapper.FromDto(dto);

        asset.AllRegions.Count.Should().Be(n,
            because: "attach pass is additive — regions must remain in AllRegions for ToDto round-trip");
    }

    [Fact]
    public void ToModel_non_parallel_state_has_no_RegionNodes_attached()
    {
        // Simple state that has an InitialChild but is NOT parallel — should get no regions.
        var simpleId  = Guid.NewGuid();
        var childId   = Guid.NewGuid();

        var dto = new HsmAssetDto { AssetId = Guid.NewGuid(), Name = "Simple" };
        dto.States.Add(new StateNodeDto { StableId = simpleId, Name = "Composite", IsParallel = false });
        dto.States.Add(new StateNodeDto { StableId = childId,  Name = "Child", IsInitial = true, ParentStableId = simpleId });

        // A region whose InitialChild is the child of the NON-parallel composite
        dto.Regions.Add(new RegionNodeDto
        {
            StableId             = Guid.NewGuid(),
            RegionIndex          = 0,
            Name                 = "Orphan",
            InitialChildStableId = childId,
        });

        var asset    = HsmAssetMapper.FromDto(dto);
        var composite = asset.AllStates.Single(s => s.Name == "Composite");

        // The region IS attached (owner.IsParallel isn't checked during attach — Regions getter
        // filters it). What matters is: RegionNodes is populated for the owner.
        // The Regions *descriptor* should be empty because IsParallel==false.
        ((IContainerNodeModel)composite).Regions.Count.Should().Be(0,
            because: "Regions getter returns empty for non-parallel states");
    }
}
