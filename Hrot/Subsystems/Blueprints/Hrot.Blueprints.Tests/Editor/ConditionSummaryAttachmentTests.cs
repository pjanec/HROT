using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class ConditionSummaryAttachmentTests
{
    // ── PreviewSynthesizer ────────────────────────────────────────────────

    [Fact]
    public void Synthesize_ValueChanged_WithPropertyPath_ReturnsShortPropName()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload { PropertyPath = "Health.Current" }
        };
        var text = PreviewSynthesizer.Synthesize(node, maxLength: 40);
        Assert.Contains("Current", text);
        Assert.Contains("↑", text);
    }

    [Fact]
    public void Synthesize_EventFired_WithTypeId_ReturnsShortName()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload { EventTypeId = "Hrot.Ai.DamageEvent" }
        };
        var text = PreviewSynthesizer.Synthesize(node);
        Assert.Contains("DamageEvent", text);
    }

    [Fact]
    public void Synthesize_EqsResult_ScoreCrossed_IncludesThreshold()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload { Trigger = EqsTrigger.ScoreCrossed, ScoreThreshold = 0.75f, SensorVariableName = "CoverSensor" }
        };
        var text = PreviewSynthesizer.Synthesize(node);
        Assert.Contains("0.8", text);  // 0.75f rounds to 0.8 with F1 format
        Assert.Contains("CoverSensor", text);
    }

    [Fact]
    public void Synthesize_LongText_TruncatesWithEllipsis()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                PropertyPath = "VeryLongComponentPath.VeryLongPropertyName.VeryLongNestedProperty"
            }
        };
        var text = PreviewSynthesizer.Synthesize(node, maxLength: 20);
        Assert.True(text.Length <= 20, $"Expected ≤20 chars, got: {text}");
        Assert.EndsWith("…", text);
    }

    // ── ConditionSummaryAttachment ────────────────────────────────────────

    [Fact]
    public void Attachment_ForWhenNode_HasNonNullLabel()
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.NotNull(attachment.Label);
        Assert.NotEmpty(attachment.Label);
    }

    [Fact]
    public void Attachment_NoEdge_HasWarningState()
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.None };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.Equal(AttachmentState.Warning, attachment.State);
    }

    [Fact]
    public void Attachment_RisingEdge_HasNormalState()
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.Equal(AttachmentState.Normal, attachment.State);
    }

    [Fact]
    public void Attachment_HostNodeId_MatchesNodeId()
    {
        var id   = Guid.NewGuid();
        var node = new WhenNode { Id = id, Mode = WhenMode.ValueChanged, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.Equal(new NodeId(id), attachment.HostNodeId);
    }

    [Theory]
    [InlineData(WhenMode.ValueChanged)]
    [InlineData(WhenMode.EventFired)]
    [InlineData(WhenMode.ConditionMet)]
    [InlineData(WhenMode.EqsResult)]
    public void Attachment_AllModes_NonEmptyLabel(WhenMode mode)
    {
        var node = new WhenNode { Id = Guid.NewGuid(), Mode = mode, Edges = WhenEdge.RisingEdge };
        var attachment = new ConditionSummaryAttachment(node);
        Assert.NotNull(attachment.Label);
        Assert.NotEmpty(attachment.Label);
    }

    [Fact]
    public void Attachment_Refresh_UpdatesLabel()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload { PropertyPath = "Alpha" }
        };
        var attachment = new ConditionSummaryAttachment(node);
        var firstLabel = attachment.Label;

        node.ValueChanged!.PropertyPath = "Beta";
        attachment.Refresh(node);

        Assert.NotEqual(firstLabel, attachment.Label);
        Assert.Contains("Beta", attachment.Label);
    }

    // ── WhenNodeAttachmentProvider ────────────────────────────────────────

    [Fact]
    public void Provider_Handles_WhenNode()
    {
        var provider = new WhenNodeAttachmentProvider();
        Assert.True(provider.Handles(new WhenNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Provider_DoesNotHandle_ReadEqsResultNode()
    {
        var provider = new WhenNodeAttachmentProvider();
        Assert.False(provider.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Provider_CreateOrRefresh_ReusesExistingAttachment()
    {
        var provider   = new WhenNodeAttachmentProvider();
        var node       = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ConditionMet, Edges = WhenEdge.RisingEdge };
        var first      = provider.CreateOrRefresh(node, null);
        var second     = provider.CreateOrRefresh(node, first);
        Assert.Same(first, second);  // same instance reused
    }
}
