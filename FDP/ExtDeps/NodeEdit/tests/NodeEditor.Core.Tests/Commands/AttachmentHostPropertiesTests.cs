using System.Collections.Generic;
using FluentAssertions;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Commands;

/// <summary>
/// DEC-04-A: tests for IAttachmentModel.HostProperties default-null contract and
/// GraphCommand.AddAttachment round-trip with HostProperties.
/// </summary>
public class AttachmentHostPropertiesTests
{
    // ── IAttachmentModel.HostProperties default-null ──────────────────────────

    private sealed class MinimalAttachmentModel : IAttachmentModel
    {
        public AttachmentId Id { get; } = AttachmentId.NewId();
        public NodeId HostNodeId { get; } = NodeId.NewId();
        public AttachmentCategory Category => AttachmentCategory.Decorator;
        public string? Glyph => null;
        public string? Label => null;
        public string? Tooltip => null;
        public AttachmentState State => AttachmentState.Normal;
        public int StackIndex => 0;
        // HostProperties intentionally not overridden — should default to null.
    }

    [Fact]
    public void IAttachmentModel_HostProperties_DefaultsToNull()
    {
        IAttachmentModel model = new MinimalAttachmentModel();
        model.HostProperties.Should().BeNull(
            "the default interface implementation returns null (DEC-04-A additive contract)");
    }

    // ── GraphCommand.AddAttachment round-trip with HostProperties ─────────────

    [Fact]
    public void AddAttachment_WithHostProperties_PreservesAllEntries()
    {
        var newId      = AttachmentId.NewId();
        var hostNodeId = NodeId.NewId();
        var props      = new Dictionary<string, object?>
        {
            ["decoratorType"] = (object?)42,
            ["intParam"]      = (object?)5,
            ["floatParam"]    = (object?)1.5f,
            ["comment"]       = (object?)"test",
        };

        var cmd = new GraphCommand.AddAttachment(
            newId, hostNodeId, AttachmentCategory.Decorator,
            "R", "x5", null, 0, props);

        cmd.HostProperties.Should().NotBeNull();
        cmd.HostProperties!["decoratorType"].Should().Be(42);
        cmd.HostProperties["intParam"].Should().Be(5);
        cmd.HostProperties["floatParam"].Should().Be(1.5f);
        cmd.HostProperties["comment"].Should().Be("test");
    }

    [Fact]
    public void AddAttachment_WithNullHostProperties_Allowed()
    {
        var cmd = new GraphCommand.AddAttachment(
            AttachmentId.NewId(), NodeId.NewId(),
            AttachmentCategory.Decorator, null, null, null, 0, null);

        cmd.HostProperties.Should().BeNull(
            "null HostProperties is a valid state (existing code paths)");
    }
}
