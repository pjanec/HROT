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
        // BTree links are empty in BTreeGraphModel (parent/child encoded via ChildVisualIds,
        // not as explicit ILinkModel entries — links are reconstructed on demand by the command sink).
        var asset  = MakeAsset(RootSequence2Actions());
        var bundle = MakeBundle();
        var ctx    = BTreeDocumentFactory.Build(asset, bundle);

        // Verify the model is queryable without throwing.
        ctx.View.Model.Links.Should().NotBeNull();
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
