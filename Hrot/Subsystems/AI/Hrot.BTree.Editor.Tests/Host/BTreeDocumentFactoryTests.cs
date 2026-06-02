using System;
using System.Linq;
using Fbt;
using FluentAssertions;
using Fdp.Presentation.Icons;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// Tests for <see cref="BTreeDocumentFactory"/> (AIE-021).
/// All tests are headless — no GPU / ImGui context needed.
/// </summary>
public sealed class BTreeDocumentFactoryTests : IDisposable
{
    // ── Shared atlas (fake GPU handle = 1) ────────────────────────────────────

    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private AiEditorAdapterBundle MakeBundle() => new(_atlas);

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "TestTree",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeBlob RootSequence2Actions() =>
        new BehaviorTreeBlob
        {
            TreeName = "S2A",
            Nodes = new[]
            {
                new NodeDefinition { Type = NodeType.Root,     ChildCount = 1, SubtreeOffset = 4 },
                new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 1 },
            },
            MethodNames     = new[] { "Ns.C.Action1", "Ns.C.Action2" },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(BehaviorTreeBlob? blob = null) =>
        BehaviorTreeAssetProjector.Project(
            blob ?? EmptyBlob(), null, null,
            Guid.NewGuid(), "TestTree", "/TestTree.cs", false,
            string.Empty, string.Empty);

    // ── AIE-021 Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void BTreeDocumentFactory_Build_ProducesHostServices_WithAllAdapters()
    {
        // Build returns a non-null context; the host services are wired.
        var asset  = MakeAsset();
        var bundle = MakeBundle();

        var ctx = BTreeDocumentFactory.Build(asset, bundle);

        ctx.Should().NotBeNull();
        ctx.View.Should().NotBeNull();
        ctx.Kind.Should().Be(AssetKind.BTree.ToString()); // "BTree"

        // Host services are exposed via GraphView.
        var host = ctx.View.Host;
        host.Should().NotBeNull();

        // All adapters from the bundle must be non-null.
        host.NodeCatalog  .Should().NotBeNull();
        host.TypeSystem   .Should().NotBeNull();
        host.LinkValidator.Should().NotBeNull();
        host.CommandSink  .Should().NotBeNull();
        host.Pickers      .Should().NotBeNull();
        host.Clipboard    .Should().NotBeNull();
        host.Icons        .Should().NotBeNull();
        host.Input        .Should().NotBeNull();
        host.Theme        .Should().NotBeNull();
        // Diagnostics is non-null from NLogDiagnosticsSink.
        host.Diagnostics  .Should().NotBeNull();
        // Debug session is null by default.
        host.Debug        .Should().BeNull();
    }

    [Fact]
    public void BTreeDocumentFactory_Build_GraphViewConstructs()
    {
        // GraphView builds without throwing; the graph model exposes the projected nodes.
        var asset  = MakeAsset(RootSequence2Actions());
        var bundle = MakeBundle();

        var ctx = BTreeDocumentFactory.Build(asset, bundle);

        // GraphView must be non-null.
        ctx.View.Should().NotBeNull();

        // The underlying graph model must expose the projected nodes (4 in this blob).
        ctx.View.Model.Nodes.Should().HaveCount(4);

        // All node IDs in the graph model must correspond to VisualIds in the asset.
        foreach (var node in ctx.View.Model.Nodes)
        {
            var editorNode = asset.FindNode(node.Id.Value);
            editorNode.Should().NotBeNull("each graph model node should map to an asset node");
        }
    }

    [Fact]
    public void BTreeDocumentFactory_Build_GraphView_ExposesProjectedLinks()
    {
        // Tree: Root → Sequence → {Action1, Action2}
        // Edges: Root→Sequence (1), Sequence→Action1 (1), Sequence→Action2 (1) = 3 links total.
        var asset  = MakeAsset(RootSequence2Actions());
        var bundle = MakeBundle();
        var ctx    = BTreeDocumentFactory.Build(asset, bundle);

        var links = ctx.View.Model.Links.ToList();

        // Exact link count: 3 parent→child edges.
        links.Should().HaveCount(3, "Root→Sequence, Sequence→Action1, Sequence→Action2");

        // For each link: FromPin == child.OutputPinId, ToPin == parent.InputPinId.
        // Get node lookup from asset.
        var nodes = asset.Nodes.ToDictionary(n => n.VisualId);
        // Build a nodeId→editorNode map through the graph model.
        var modelNodes = ctx.View.Model.Nodes.ToList();

        foreach (var link in links)
        {
            // FromPin must be some child's OutputPinId.
            var childNode = asset.Nodes.FirstOrDefault(
                n => new NodeEditor.Primitives.PinId(n.OutputPinId) == link.FromPin);
            childNode.Should().NotBeNull(
                $"link.FromPin {link.FromPin} must match some child's OutputPinId");

            // ToPin must be the parent's InputPinId.
            // Find the parent: the node that has childNode.VisualId in its ChildVisualIds.
            var parentNode = asset.Nodes.FirstOrDefault(
                n => n.ChildVisualIds.Contains(childNode!.VisualId));
            parentNode.Should().NotBeNull(
                $"child {childNode!.VisualId} must have a parent in the tree");

            link.ToPin.Should().Be(new NodeEditor.Primitives.PinId(parentNode!.InputPinId),
                $"link to-pin must be parent ({parentNode.KernelType}).InputPinId");

            // Link must also be findable by id.
            ctx.View.Model.FindLink(link.Id).Should().NotBeNull(
                $"link {link.Id} must be findable via FindLink");
        }
    }

    [Fact]
    public void BTreeDocumentFactory_Build_CustomRenderers_ArePresent()
    {
        // The factory injects at least the built-in BTree renderers (SubtreeBoundary,
        // ObserverGuard, VariableBinding).
        var asset  = MakeAsset();
        var bundle = MakeBundle();

        var ctx = BTreeDocumentFactory.Build(asset, bundle);

        ctx.View.Host.CustomCanvasRenderers.Should().NotBeEmpty();
        ctx.View.Host.CustomCanvasRenderers.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void BTreeDocumentFactory_Build_Throws_WhenAssetIsWrongType()
    {
        // Passing a non-BTree asset must throw ArgumentException.
        var wrongAsset = new FakeHsmAsset();
        var bundle     = MakeBundle();

        var act = () => BTreeDocumentFactory.Build(wrongAsset, bundle);
        act.Should().Throw<ArgumentException>().WithMessage("*BehaviorTreeAsset*");
    }

    [Fact]
    public void BTreeDocumentFactory_Build_EmptyAsset_ProducesZeroNodes()
    {
        var asset  = MakeAsset(EmptyBlob());
        var bundle = MakeBundle();

        var ctx = BTreeDocumentFactory.Build(asset, bundle);

        ctx.View.Model.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void BTreeDocumentFactory_Build_Pins_AreStableAndDistinct()
    {
        // Each projected node exposes 2 pins (output + input) with distinct, stable IDs.
        var asset  = MakeAsset(RootSequence2Actions());
        var bundle = MakeBundle();
        var ctx    = BTreeDocumentFactory.Build(asset, bundle);

        var allPins = ctx.View.Model.Nodes
            .SelectMany(n => n.Pins)
            .ToList();

        // 4 nodes × 2 pins = 8 pins.
        allPins.Should().HaveCount(8);

        // All pin IDs must be distinct.
        allPins.Select(p => p.Id).Distinct().Should().HaveCount(8);

        // All pins must be findable via FindPin.
        foreach (var pin in allPins)
            ctx.View.Model.FindPin(pin.Id).Should().NotBeNull($"pin {pin.Id} must be findable");
    }

    // ── Fake asset (wrong type) ────────────────────────────────────────────────

    private sealed class FakeHsmAsset : IEditableAsset
    {
        public Guid      AssetId        => Guid.NewGuid();
        public string    Name           => "fake";
        public AssetKind Kind           => AssetKind.Hsm;
        public string    SourceFilePath => "";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => false;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
