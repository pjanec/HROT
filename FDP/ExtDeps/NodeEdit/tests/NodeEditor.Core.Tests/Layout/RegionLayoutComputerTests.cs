using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Layout;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Layout;

/// <summary>Unit tests for RegionLayoutComputer.Compute.</summary>
public sealed class RegionLayoutComputerTests
{
    // ── Stub ──────────────────────────────────────────────────────────────────

    private sealed class StubContainer : IContainerNodeModel
    {
        private readonly IReadOnlyList<RegionDescriptor> _regions;

        public StubContainer(IReadOnlyList<RegionDescriptor>? regions = null, ContainerPadding? padding = null)
        {
            _regions = regions ?? System.Array.Empty<RegionDescriptor>();
            Padding  = padding ?? new ContainerPadding(0f, 0f, 0f, 0f);
        }

        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("c");
        public string Title => "C";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds => System.Array.Empty<NodeId>();
        public IReadOnlyList<RegionDescriptor> Regions => _regions;
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding { get; }
        public Vector2 MinimumInteriorSize => new(100f, 60f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RegionDescriptor Reg(int index, string name, int priority = 0) =>
        new(index, name, priority, CustomColor: null);

    // outerBounds: 200 wide, 120 tall (headerHt=24, outline=1, zero padding)
    private static readonly RectF OuterBounds = new(Vector2.Zero, new Vector2(200f, 120f));
    private const float HeaderHt  = 24f;
    private const float OutlinePx = 1f;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NoRegions_ReturnsEmpty()
    {
        var container = new StubContainer(System.Array.Empty<RegionDescriptor>());
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);
        result.Should().BeEmpty();
    }

    [Fact]
    public void OneRegion_ReturnsSingleStrip()
    {
        var container = new StubContainer([Reg(0, "R0")]);
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);
        result.Should().HaveCount(1);
    }

    [Fact]
    public void TwoRegions_EqualHeight()
    {
        var container = new StubContainer([Reg(0, "R0"), Reg(1, "R1")]);
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);

        result.Should().HaveCount(2);
        result[0].Size.Y.Should().BeApproximately(result[1].Size.Y, precision: 0.01f);
    }

    [Fact]
    public void TwoRegions_StripsTileVertically()
    {
        var container = new StubContainer([Reg(0, "R0"), Reg(1, "R1")]);
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);

        // Second strip's Min.Y == first strip's Min.Y + first strip's height.
        float expectedSecondY = result[0].Min.Y + result[0].Size.Y;
        result[1].Min.Y.Should().BeApproximately(expectedSecondY, precision: 0.01f);
    }

    [Fact]
    public void ThreeRegions_HeightSum_EqualsInnerHeight()
    {
        var container = new StubContainer([Reg(0, "A"), Reg(1, "B"), Reg(2, "C")]);
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);

        float totalH = 0f;
        foreach (var s in result) totalH += s.Size.Y;
        // inner height = 120 - 2*1 (outline) - 24 (header) - 0 (padding) = 94
        totalH.Should().BeApproximately(OuterBounds.Size.Y - 2f * OutlinePx - HeaderHt, precision: 0.1f);
    }

    [Fact]
    public void RegionDescriptorPreserved_CorrectIndex()
    {
        var r0 = Reg(0, "Alpha", priority: 1);
        var r1 = Reg(1, "Beta",  priority: 3);
        var container = new StubContainer([r0, r1]);
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);

        result[0].Descriptor.Should().Be(r0);
        result[1].Descriptor.Should().Be(r1);
        result[0].RegionIndex.Should().Be(0);
        result[1].RegionIndex.Should().Be(1);
    }

    [Fact]
    public void PaddingScaleApplied_InnerWidthReducedByPadding()
    {
        var container = new StubContainer(
            [Reg(0, "R0"), Reg(1, "R1")],
            padding: new ContainerPadding(Top: 0f, Right: 10f, Bottom: 0f, Left: 10f));
        // paddingScale = 1 => left+right = 20
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx, paddingScale: 1f);
        float expectedInnerW = OuterBounds.Size.X - 2f * OutlinePx - 20f;
        result[0].Size.X.Should().BeApproximately(expectedInnerW, precision: 0.01f);
    }

    [Fact]
    public void FirstStrip_MinY_IsBelow_Header()
    {
        var container = new StubContainer([Reg(0, "R0")]);
        var result = RegionLayoutComputer.Compute(container, OuterBounds, HeaderHt, OutlinePx);
        // interior Y starts at outline + headerHt
        result[0].Min.Y.Should().BeApproximately(OutlinePx + HeaderHt, precision: 0.01f);
    }
}
