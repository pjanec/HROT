using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class CrossAssetDependencyAttachmentTests
{
    [Fact]
    public void Attachment_Label_IsPeerAssetName()
    {
        var nodeId = new NodeId(Guid.NewGuid());
        var att    = new CrossAssetDependencyAttachment(nodeId, "EntityState");
        Assert.Equal("EntityState", att.Label);
    }

    [Fact]
    public void Attachment_Glyph_IsLink()
    {
        var att = new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "X");
        Assert.Equal("🔗", att.Glyph);
    }

    [Fact]
    public void Attachment_State_IsNormal()
    {
        var att = new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "X");
        Assert.Equal(AttachmentState.Normal, att.State);
    }

    [Fact]
    public void Attachment_StackIndex_IsOne()
    {
        var att = new CrossAssetDependencyAttachment(new NodeId(Guid.NewGuid()), "X");
        Assert.Equal(1, att.StackIndex);
    }

    [Fact]
    public void Attachment_HostNodeId_MatchesInput()
    {
        var id  = new NodeId(Guid.NewGuid());
        var att = new CrossAssetDependencyAttachment(id, "X");
        Assert.Equal(id, att.HostNodeId);
    }

    // ── Provider ────────────────────────────────────────────────────────

    [Fact]
    public void Provider_Handles_PeerBlueprintVariable_WhenNode()
    {
        var peerId   = Guid.NewGuid();
        var provider = new CrossAssetDependencyAttachmentProvider(_ => "EntityState");
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload
            {
                Source              = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId
            }
        };
        Assert.True(provider.Handles(node));
    }

    [Fact]
    public void Provider_DoesNotHandle_SelfComponent_WhenNode()
    {
        var provider = new CrossAssetDependencyAttachmentProvider(_ => null);
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload { Source = ValueChangedSource.SelfComponent }
        };
        Assert.False(provider.Handles(node));
    }

    [Fact]
    public void Provider_DoesNotHandle_EventFired_WhenNode()
    {
        var provider = new CrossAssetDependencyAttachmentProvider(_ => null);
        var node     = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.EventFired };
        Assert.False(provider.Handles(node));
    }

    [Fact]
    public void Provider_CreateOrRefresh_UsesResolvedPeerName()
    {
        var peerId   = Guid.NewGuid();
        var provider = new CrossAssetDependencyAttachmentProvider(id => id == peerId ? "EntityState" : null);
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload
            {
                Source              = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId
            }
        };
        var att = provider.CreateOrRefresh(node, null) as CrossAssetDependencyAttachment;
        Assert.NotNull(att);
        Assert.Equal("EntityState", att.Label);
    }

    [Fact]
    public void Provider_CreateOrRefresh_FallsBackToShortId_WhenNameUnresolved()
    {
        var peerId   = Guid.NewGuid();
        var provider = new CrossAssetDependencyAttachmentProvider(_ => null);
        var node     = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            ValueChanged = new ValueChangedPayload
            {
                Source              = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId
            }
        };
        var att = provider.CreateOrRefresh(node, null) as CrossAssetDependencyAttachment;
        Assert.NotNull(att);
        // Label is an 8-char hex short-id when name resolver returns null
        Assert.Equal(8, att.Label!.Length);
    }
}
