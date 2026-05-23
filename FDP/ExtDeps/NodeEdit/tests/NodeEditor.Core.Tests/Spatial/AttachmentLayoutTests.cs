using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Spatial;

public class AttachmentLayoutTests
{
    // Constants for readability.
    private const float H  = AttachmentLayoutEngine.PillHeight;          // 20
    private const float G  = AttachmentLayoutEngine.GapAboveHost;        // 6
    private const float Ir = AttachmentLayoutEngine.InterRowGap;         // 3
    private const float Ig = AttachmentLayoutEngine.InterAttachmentGap;  // 4
    private const float P  = AttachmentLayoutEngine.PillPaddingH;        // 6 (each side)
    private const float M  = AttachmentLayoutEngine.PillMinWidth;        // 24

    // Simple stub IAttachmentModel for tests.
    private sealed class StubAttachment : IAttachmentModel
    {
        public AttachmentId Id { get; }
        public NodeId HostNodeId => NodeId.Empty;
        public AttachmentCategory Category => AttachmentCategory.Custom;
        public string? Glyph => null;
        public string? Label => null;
        public string? Tooltip => null;
        public AttachmentState State => default;
        public int StackIndex { get; }

        public StubAttachment(int stackIndex = 0)
        {
            Id = AttachmentId.NewId();
            StackIndex = stackIndex;
        }
    }

    [Fact]
    public void EmptyList_ReturnsEmpty()
    {
        var layout = AttachmentLayoutEngine.Compute(
            Array.Empty<IAttachmentModel>(),
            200f,
            _ => 40f);

        layout.Should().BeSameAs(AttachmentLayout.Empty);
        layout.TotalHeightAboveHost.Should().Be(0f);
    }

    [Fact]
    public void SingleAttachment_SingleRow()
    {
        var a = new StubAttachment(stackIndex: 0);
        float contentWidth = 40f;
        float expectedPillWidth = contentWidth + P * 2f; // 52

        var layout = AttachmentLayoutEngine.Compute(
            new IAttachmentModel[] { a },
            200f,
            _ => contentWidth);

        layout.Placements.Should().ContainKey(a.Id);
        var placement = layout.Placements[a.Id];

        placement.TopLeft.Should().Be(new Vector2(0f, -(G + H)));
        placement.Size.X.Should().Be(expectedPillWidth);
        placement.Size.Y.Should().Be(H);
        layout.TotalHeightAboveHost.Should().Be(G + H);
    }

    [Fact]
    public void TwoAttachments_FitInOneRow()
    {
        var a1 = new StubAttachment(stackIndex: 0);
        var a2 = new StubAttachment(stackIndex: 1);

        // Each pill: content=30, width = 30 + 6*2 = 42.
        // Two pills + gap: 42 + 4 + 42 = 88 <= 200 (fits).
        const float contentWidth = 30f;
        const float pillWidth = contentWidth + P * 2f; // 42

        var layout = AttachmentLayoutEngine.Compute(
            new IAttachmentModel[] { a1, a2 },
            200f,
            _ => contentWidth);

        layout.Placements.Should().ContainKey(a1.Id);
        layout.Placements.Should().ContainKey(a2.Id);

        var p1 = layout.Placements[a1.Id];
        var p2 = layout.Placements[a2.Id];

        p1.TopLeft.Should().Be(new Vector2(0f, -(G + H)));
        p2.TopLeft.Should().Be(new Vector2(pillWidth + Ig, -(G + H)));

        // One row -> total height = GapAboveHost + PillHeight.
        layout.TotalHeightAboveHost.Should().Be(G + H);
    }

    [Fact]
    public void TwoAttachments_WrapToSecondRow()
    {
        var a1 = new StubAttachment(stackIndex: 0);
        var a2 = new StubAttachment(stackIndex: 1);

        // Each pill: content=60, width = 60 + 6*2 = 72.
        // Two pills would need 72 + 4 + 72 = 148 > 100 (wrap).
        const float contentWidth = 60f;

        var layout = AttachmentLayoutEngine.Compute(
            new IAttachmentModel[] { a1, a2 },
            100f,
            _ => contentWidth);

        layout.Placements.Should().HaveCount(2);

        var p1 = layout.Placements[a1.Id];
        var p2 = layout.Placements[a2.Id];

        // Row 0 (bottom-most): a1 at Y = -(G+H).
        p1.TopLeft.Y.Should().BeApproximately(-(G + H), 0.001f);

        // Row 1 (above row 0): a2 at Y = -(G+H) - (H+Ir).
        p2.TopLeft.Y.Should().BeApproximately(-(G + H) - (H + Ir), 0.001f);

        // Two rows -> total height = G + 2*H + 1*Ir.
        layout.TotalHeightAboveHost.Should().BeApproximately(G + 2f * H + Ir, 0.001f);
    }

    [Fact]
    public void SortsByStackIndex()
    {
        // a0 has StackIndex 0, a1 has StackIndex 1; even if passed in reverse order.
        var a0 = new StubAttachment(stackIndex: 0);
        var a1 = new StubAttachment(stackIndex: 1);

        const float contentWidth = 30f;
        const float pillWidth = contentWidth + P * 2f;

        var layout = AttachmentLayoutEngine.Compute(
            new IAttachmentModel[] { a1, a0 },   // intentionally reversed
            200f,
            _ => contentWidth);

        var p0 = layout.Placements[a0.Id];
        var p1 = layout.Placements[a1.Id];

        // a0 (StackIndex 0) should be leftmost (X = 0).
        p0.TopLeft.X.Should().Be(0f);
        p1.TopLeft.X.Should().Be(pillWidth + Ig);
    }

    [Fact]
    public void MinWidth_Applied()
    {
        var a = new StubAttachment(stackIndex: 0);

        // content width = 0 -> pill width must be clamped to M (24).
        var layout = AttachmentLayoutEngine.Compute(
            new IAttachmentModel[] { a },
            200f,
            _ => 0f);

        layout.Placements[a.Id].Size.X.Should().Be(M);
    }
}
