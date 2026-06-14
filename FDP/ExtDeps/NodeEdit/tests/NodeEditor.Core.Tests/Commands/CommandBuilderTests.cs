using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Commands;

/// <summary>
/// Tests for <see cref="CommandBuilder"/> helpers added in DEC-02.
/// </summary>
public class CommandBuilderTests
{
    // ── Minimal stub ─────────────────────────────────────────────────────────

    private sealed class StubModel : IGraphModel
    {
        public GraphId Id => GraphId.Empty;
        public string DisplayName => "stub";
        public GraphKindDescriptor Kind => new("stub", "Stub", false, false);
        public IReadOnlyCollection<INodeModel> Nodes => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel> Links => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel? FindNode(NodeId id) => null;
        public IPinModel? FindPin(PinId id) => null;
        public ILinkModel? FindLink(LinkId id) => null;
        public event Action<GraphChangeNotification>? Changed { add { } remove { } }
    }

    // ── CommandBuilder.AddAttachment ─────────────────────────────────────────

    [Fact]
    public void AddAttachment_ForwardIsAddAttachmentWithCorrectFields()
    {
        var model = new StubModel();
        var cb = new CommandBuilder(model);
        var host = NodeId.NewId();
        var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = "decorator.inverter" };

        var (fwd, _) = cb.AddAttachment(
            host,
            AttachmentCategory.Decorator,
            glyph: null,
            label: "Inverter",
            tooltip: "Inverts result",
            stackIndex: 2,
            hostProps: props);

        var addCmd = fwd.Should().BeOfType<GraphCommand.AddAttachment>().Subject;
        addCmd.HostNodeId.Should().Be(host);
        addCmd.Category.Should().Be(AttachmentCategory.Decorator);
        addCmd.Glyph.Should().BeNull();
        addCmd.Label.Should().Be("Inverter");
        addCmd.Tooltip.Should().Be("Inverts result");
        addCmd.StackIndex.Should().Be(2);
        addCmd.HostProperties.Should().BeSameAs(props);
        addCmd.NewId.Should().NotBe(AttachmentId.Empty);
    }

    [Fact]
    public void AddAttachment_InverseIsRemoveAttachmentsWithSameId()
    {
        var model = new StubModel();
        var cb = new CommandBuilder(model);
        var host = NodeId.NewId();

        var (fwd, inv) = cb.AddAttachment(
            host,
            AttachmentCategory.Custom,
            glyph: null,
            label: "Tag",
            tooltip: null,
            stackIndex: 0,
            hostProps: null);

        var addCmd = fwd.Should().BeOfType<GraphCommand.AddAttachment>().Subject;
        var removeCmd = inv.Should().BeOfType<GraphCommand.RemoveAttachments>().Subject;
        removeCmd.AttachmentIds.Should().ContainSingle()
            .Which.Should().Be(addCmd.NewId);
    }

    [Fact]
    public void AddAttachment_EachCallGeneratesDistinctId()
    {
        var model = new StubModel();
        var cb = new CommandBuilder(model);
        var host = NodeId.NewId();

        var (fwd1, _) = cb.AddAttachment(host, AttachmentCategory.Decorator, null, "A", null, 0, null);
        var (fwd2, _) = cb.AddAttachment(host, AttachmentCategory.Decorator, null, "B", null, 1, null);

        var id1 = ((GraphCommand.AddAttachment)fwd1).NewId;
        var id2 = ((GraphCommand.AddAttachment)fwd2).NewId;
        id1.Should().NotBe(id2);
    }

    // ── NodeCatalogEntry back-compat defaults ─────────────────────────────────

    [Fact]
    public void NodeCatalogEntry_DefaultPaletteAction_IsCreateNode()
    {
        var entry = new NodeCatalogEntry(
            new NodeKindKey("test"),
            "Test",
            null,
            null,
            Array.Empty<string>(),
            null,
            false,
            false,
            false,
            Array.Empty<PinSignature>(),
            Array.Empty<PinSignature>());

        entry.PaletteAction.Should().Be(NodePaletteAction.CreateNode);
    }

    [Fact]
    public void NodeCatalogEntry_DefaultAttachmentCategory_IsNull()
    {
        var entry = new NodeCatalogEntry(
            new NodeKindKey("test"),
            "Test",
            null,
            null,
            Array.Empty<string>(),
            null,
            false,
            false,
            false,
            Array.Empty<PinSignature>(),
            Array.Empty<PinSignature>());

        entry.AttachmentCategory.Should().BeNull();
    }

    [Fact]
    public void NodeCatalogEntry_AttachToSelected_CanBeSet()
    {
        var entry = new NodeCatalogEntry(
            new NodeKindKey("decorator.inverter"),
            "Inverter",
            "Inverts",
            null,
            Array.Empty<string>(),
            null,
            false,
            false,
            false,
            Array.Empty<PinSignature>(),
            Array.Empty<PinSignature>(),
            PaletteAction: NodePaletteAction.AttachToSelected,
            AttachmentCategory: AttachmentCategory.Decorator);

        entry.PaletteAction.Should().Be(NodePaletteAction.AttachToSelected);
        entry.AttachmentCategory.Should().Be(AttachmentCategory.Decorator);
    }

    // ── AttachmentHostPropertyKeys constant ──────────────────────────────────

    [Fact]
    public void AttachmentHostPropertyKeys_Kind_IsExpectedString()
    {
        AttachmentHostPropertyKeys.Kind.Should().Be("paletteKind");
    }

    // ── IdGenerator.NewAttachmentId ───────────────────────────────────────────

    [Fact]
    public void IdGenerator_NewAttachmentId_ReturnsNonEmpty()
    {
        var id = IdGenerator.NewAttachmentId();
        id.Should().NotBe(AttachmentId.Empty);
    }

    [Fact]
    public void IdGenerator_NewAttachmentId_EachCallDistinct()
    {
        var a = IdGenerator.NewAttachmentId();
        var b = IdGenerator.NewAttachmentId();
        a.Should().NotBe(b);
    }
}
