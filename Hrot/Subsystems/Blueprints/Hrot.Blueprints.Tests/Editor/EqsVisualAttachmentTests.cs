using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class EqsVisualAttachmentTests
{
    // ── EqsTemplateAttachment ────────────────────────────────────────────

    [Fact]
    public void EqsTemplate_NoTemplate_LabelIsNoTemplate()
    {
        var registry = new EqsTemplateRegistry();
        var node     = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = Guid.Empty };
        var att      = new EqsTemplateAttachment(node, registry);
        Assert.Equal("(no template)", att.Label);
        Assert.Equal(AttachmentState.Warning, att.State);
    }

    [Fact]
    public void EqsTemplate_WithTemplate_LabelIsTemplateName()
    {
        var registry   = new EqsTemplateRegistry();
        var templateId = Guid.NewGuid();
        registry.Register(new EqsTemplateEntry { AssetId = templateId, DisplayName = "CoverQuery" });

        var node = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = templateId };
        var att  = new EqsTemplateAttachment(node, registry);

        Assert.Equal("CoverQuery", att.Label);
        Assert.Equal(AttachmentState.Normal, att.State);
    }

    [Fact]
    public void EqsTemplate_UnknownTemplate_LabelIsNotFound()
    {
        var registry = new EqsTemplateRegistry();
        var node     = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = Guid.NewGuid() };
        var att      = new EqsTemplateAttachment(node, registry);
        Assert.Equal("(template not found)", att.Label);
        Assert.Equal(AttachmentState.Warning, att.State);
    }

    [Fact]
    public void EqsTemplate_HostNodeId_MatchesNodeId()
    {
        var id       = Guid.NewGuid();
        var registry = new EqsTemplateRegistry();
        var node     = new SpawnEqsSensorNode { Id = id, TemplateAssetId = Guid.Empty };
        var att      = new EqsTemplateAttachment(node, registry);
        Assert.Equal(new NodeId(id), att.HostNodeId);
    }

    [Fact]
    public void EqsTemplate_Provider_Handles_SpawnNode()
    {
        var provider = new EqsTemplateAttachmentProvider(new EqsTemplateRegistry());
        Assert.True(provider.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
        Assert.False(provider.Handles(new WhenNode { Id = Guid.NewGuid() }));
    }

    // ── ReadEqsResultAttachment ───────────────────────────────────────────

    [Fact]
    public void ReadEqs_EmptyVariableName_LabelIsNoVariable()
    {
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "" };
        var att  = new ReadEqsResultAttachment(node);
        Assert.Equal("(no variable)", att.Label);
        Assert.Equal(AttachmentState.Warning, att.State);
    }

    [Fact]
    public void ReadEqs_WithVariableName_LabelIsVariableName()
    {
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "CoverSensor" };
        var att  = new ReadEqsResultAttachment(node);
        Assert.Equal("CoverSensor", att.Label);
        Assert.Equal(AttachmentState.Normal, att.State);
    }

    [Fact]
    public void ReadEqs_Refresh_UpdatesLabel()
    {
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "Alpha" };
        var att  = new ReadEqsResultAttachment(node);
        Assert.Equal("Alpha", att.Label);

        node.SensorVariableName = "Beta";
        att.Refresh(node);
        Assert.Equal("Beta", att.Label);
    }

    [Fact]
    public void ReadEqs_Provider_Handles_ReadNode()
    {
        var provider = new ReadEqsResultAttachmentProvider();
        Assert.True(provider.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
        Assert.False(provider.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void ReadEqs_Provider_ReusesExistingAttachment()
    {
        var provider = new ReadEqsResultAttachmentProvider();
        var node     = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "Sensor1" };
        var first    = provider.CreateOrRefresh(node, null);
        var second   = provider.CreateOrRefresh(node, first);
        Assert.Same(first, second);
    }
}
