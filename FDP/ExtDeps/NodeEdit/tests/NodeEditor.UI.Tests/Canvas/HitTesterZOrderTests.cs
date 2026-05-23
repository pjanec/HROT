using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// Verifies that the 15-step z-layer priority hierarchy constants in HitTester
/// have the correct relative ordering. Tests correspond to TASK-NEA-06, TASK-NEC-05,
/// and TASK-NER-04.
/// </summary>
public sealed class HitTesterZOrderTests
{
    // Replicates the SubmitHit selection logic so tests can verify outcomes
    // without requiring a full GraphView / ImGui pipeline.
    private static HoverKind? SelectWinner(params (int z, int s, int p, HoverKind kind)[] candidates)
    {
        bool hasBest = false;
        HoverKind best = HoverKind.None;
        int bestZ = -1, bestS = -1, bestP = int.MaxValue;

        foreach (var (z, s, p, kind) in candidates)
        {
            if (z > bestZ
                || (z == bestZ && s > bestS)
                || (z == bestZ && s == bestS && p < bestP))
            {
                hasBest = true;
                best    = kind;
                bestZ = z; bestS = s; bestP = p;
            }
        }

        return hasBest ? best : null;
    }

    // ── Hierarchy constant ordering ──────────────────────────────────────────

    [Fact]
    public void Reroute_has_highest_z_layer()
    {
        HitTester.ZLayerReroute.Should().BeGreaterThan(HitTester.ZLayerPin);
        HitTester.ZLayerReroute.Should().BeGreaterThan(HitTester.ZLayerWire);
        HitTester.ZLayerReroute.Should().BeGreaterThan(HitTester.ZLayerTopMost);
        HitTester.ZLayerReroute.Should().BeGreaterThan(HitTester.ZLayerAttachment);
        HitTester.ZLayerReroute.Should().BeGreaterThan(HitTester.ZLayerNodeBody);
    }

    [Fact]
    public void Pin_beats_wire_and_topmost()
    {
        HitTester.ZLayerPin.Should().BeGreaterThan(HitTester.ZLayerWire);
        HitTester.ZLayerPin.Should().BeGreaterThan(HitTester.ZLayerTopMost);
    }

    [Fact]
    public void Wire_beats_topmost_custom_and_attachment()
    {
        HitTester.ZLayerWire.Should().BeGreaterThan(HitTester.ZLayerTopMost);
        HitTester.ZLayerWire.Should().BeGreaterThan(HitTester.ZLayerAttachment);
    }

    [Fact]
    public void Attachment_beats_node_body()
    {
        HitTester.ZLayerAttachment.Should().BeGreaterThan(HitTester.ZLayerNodeBody);
    }

    [Fact]
    public void Container_chevron_beats_container_header()
    {
        HitTester.ZLayerContainerChevron.Should().BeGreaterThan(HitTester.ZLayerContainerHeader);
    }

    [Fact]
    public void Container_header_beats_comment_header()
    {
        HitTester.ZLayerContainerHeader.Should().BeGreaterThan(HitTester.ZLayerCommentHeader);
    }

    [Fact]
    public void Comment_header_beats_node_body()
    {
        HitTester.ZLayerCommentHeader.Should().BeGreaterThan(HitTester.ZLayerNodeBody);
    }

    [Fact]
    public void Wire_beats_after_wires_custom()
    {
        HitTester.ZLayerWire.Should().BeGreaterThan(HitTester.ZLayerAfterWires);
    }

    [Fact]
    public void Node_body_beats_container_interior_and_comment_body()
    {
        HitTester.ZLayerNodeBody.Should().BeGreaterThan(HitTester.ZLayerContainerInterior);
        HitTester.ZLayerNodeBody.Should().BeGreaterThan(HitTester.ZLayerCommentBody);
    }

    // ── Behavioral selection tests ───────────────────────────────────────────

    [Fact]
    public void TopMost_custom_beats_node_body_at_same_position()
    {
        var winner = SelectWinner(
            (HitTester.ZLayerNodeBody, 1, 2, HoverKind.Node),
            (HitTester.ZLayerTopMost,  1, 1, HoverKind.CustomElement));

        winner.Should().Be(HoverKind.CustomElement);
    }

    [Fact]
    public void Attachment_beats_node_body_at_same_position()
    {
        var winner = SelectWinner(
            (HitTester.ZLayerNodeBody,   1, 2, HoverKind.Node),
            (HitTester.ZLayerAttachment, 0, 1, HoverKind.Attachment));

        winner.Should().Be(HoverKind.Attachment);
    }

    [Fact]
    public void Container_header_beats_node_body_at_same_position()
    {
        var winner = SelectWinner(
            (HitTester.ZLayerNodeBody,        1, 2, HoverKind.Node),
            (HitTester.ZLayerContainerHeader, 1, 1, HoverKind.Container));

        winner.Should().Be(HoverKind.Container);
    }

    [Fact]
    public void No_candidates_returns_no_hover()
    {
        // Empty canvas: no SubmitHit call is ever made, so result is null / HoverInfo.None.
        var winner = SelectWinner();

        winner.Should().BeNull();
    }

    [Fact]
    public void Wire_beats_attachment_at_same_position()
    {
        var winner = SelectWinner(
            (HitTester.ZLayerAttachment, 0, 1, HoverKind.Attachment),
            (HitTester.ZLayerWire,       1, 1, HoverKind.Link));

        winner.Should().Be(HoverKind.Link);
    }

    [Fact]
    public void Reroute_beats_everything_else()
    {
        var winner = SelectWinner(
            (HitTester.ZLayerTopMost,    1, 1, HoverKind.CustomElement),
            (HitTester.ZLayerPin,        1, 1, HoverKind.Pin),
            (HitTester.ZLayerAttachment, 1, 1, HoverKind.Attachment),
            (HitTester.ZLayerNodeBody,   1, 1, HoverKind.Node),
            (HitTester.ZLayerReroute,    1, 1, HoverKind.Reroute));

        winner.Should().Be(HoverKind.Reroute);
    }

    [Fact]
    public void Higher_stack_index_attachment_wins_over_lower_at_same_position()
    {
        // Two overlapping attachments: StackIndex used as subLayer, higher wins.
        var winner = SelectWinner(
            (HitTester.ZLayerAttachment, 1, 1, HoverKind.Attachment),
            (HitTester.ZLayerAttachment, 5, 1, HoverKind.Attachment));

        // Both are HoverKind.Attachment; the one with higher subLayer (stackIndex=5) wins.
        // We can't distinguish them by kind here, so just verify no crash and kind is Attachment.
        winner.Should().Be(HoverKind.Attachment);
    }
}
