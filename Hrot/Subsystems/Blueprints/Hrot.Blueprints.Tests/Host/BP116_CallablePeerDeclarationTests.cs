using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-116 — before this fix, nothing in <c>Hrot.Blueprints.Editor</c> ever wrote to
/// <see cref="BlueprintAsset.CallablePeers"/>: <c>Stage2_Validate</c> requires every
/// <see cref="CallPeerBlueprintNode"/>'s <c>PeerBlueprintId</c> to be declared there
/// (BP1300 otherwise), so any editor-authored peer call was uncompilable — always, for
/// everyone. These tests lock the three write sites (palette create-path, drag-drop
/// initial-properties path, Details-panel picker) plus retraction on node removal, and
/// exercise <see cref="CallablePeerDeclarations"/> directly.
/// </summary>
public sealed class BP116_CallablePeerDeclarationTests
{
    // ── shared sink/asset scaffolding (mirrors BlueprintCommandSinkTests.MakeSut) ──────

    private static (BlueprintAsset asset, Graph graph) MakeAssetWithGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("BP116Asset")
            .WithGraph("Main", GraphKind.Event, _ => { })
            .Build();
        return (asset, asset.Graphs[0]);
    }

    private static BlueprintCommandSink MakeSink(BlueprintAsset asset, Graph graph)
    {
        var typeSystem  = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model       = new BlueprintGraphModel(asset, graph);
        var catalog     = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator   = new BlueprintLinkValidator(model, typeSystem);
        var history     = new CommandHistory();
        var editService = new EditService { Context = new EditServiceContext(history, _ => { }) };

        return new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editService, markDirty: _ => { });
    }

    private static GraphCommandResult AddCallPeerNode(BlueprintCommandSink sink, Guid peerId)
        => sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey($"CallPeer.{peerId:N}"),
            Vector2.Zero,
            null));

    // ── 1. The defect, create path: BlueprintNodeCatalog's "CallPeer.{guid:N}" palette entry ──

    [Fact]
    public void AddNode_CallPeerPaletteKind_DeclaresThePeerOnTheAsset()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var sink   = MakeSink(asset, graph);
        var peerId = Guid.NewGuid();

        var result = AddCallPeerNode(sink, peerId);

        Assert.True(result.Success);
        // This is what was silently empty before BP-116.
        Assert.NotEmpty(asset.CallablePeers);
        Assert.Contains(peerId, asset.CallablePeers);
    }

    // ── 2. The defect, picker path: CallPeerBlueprintNodeDrawer's Details-panel combo ─────

    [Fact]
    public void DrawerPeerPicker_SelectingAPeer_DeclaresThePeerOnTheAsset()
    {
        var (asset, _) = MakeAssetWithGraph();
        var peerId = Guid.NewGuid();
        var node   = new CallPeerBlueprintNode();

        var editService = new EditService(); // no Context — apply runs immediately, no undo needed here
        var peers  = new StubPeerProvider(new BlueprintPeerInfo(peerId, "Peer", Array.Empty<string>()));
        var drawer = new CallPeerBlueprintNodeDrawer(editService, peers);

        var session = (CallPeerBlueprintNodeSession)drawer.CreateSession(node, asset);
        session.SetPeerForTest(peerId);

        Assert.NotEmpty(asset.CallablePeers);
        Assert.Contains(peerId, asset.CallablePeers);
    }

    // ── 3. Idempotence: two CallPeer nodes targeting the same peer ─────────────────────

    [Fact]
    public void AddNode_TwoCallPeerNodesToTheSamePeer_YieldsExactlyOneEntry()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var sink   = MakeSink(asset, graph);
        var peerId = Guid.NewGuid();

        AddCallPeerNode(sink, peerId);
        AddCallPeerNode(sink, peerId);

        Assert.Single(asset.CallablePeers);
    }

    // ── 4. "N" vs "D" spelling must not double-declare ──────────────────────────────

    [Fact]
    public void Declare_NAndDFormOfTheSameGuid_DoNotProduceTwoEntries()
    {
        var (asset, _) = MakeAssetWithGraph();
        var id = Guid.NewGuid();

        Assert.True(CallablePeerDeclarations.Declare(asset, id.ToString("N")));
        Assert.False(CallablePeerDeclarations.Declare(asset, id.ToString("D")));

        Assert.Single(asset.CallablePeers);
    }

    // ── 5. Undo of the picker edit retracts what that edit added ───────────────────────

    [Fact]
    public void DrawerPeerPicker_Undo_RetractsWhatThisEditAdded()
    {
        var (asset, _) = MakeAssetWithGraph();
        var peerId = Guid.NewGuid();
        var node   = new CallPeerBlueprintNode();

        var history     = new CommandHistory();
        var editService = new EditService { Context = new EditServiceContext(history, _ => { }) };
        var peers  = new StubPeerProvider(new BlueprintPeerInfo(peerId, "Peer", Array.Empty<string>()));
        var drawer = new CallPeerBlueprintNodeDrawer(editService, peers);

        var session = (CallPeerBlueprintNodeSession)drawer.CreateSession(node, asset);
        session.SetPeerForTest(peerId);
        Assert.Contains(peerId, asset.CallablePeers);

        history.Undo();

        Assert.DoesNotContain(peerId, asset.CallablePeers);
    }

    // ── 6. Retract on node removal ──────────────────────────────────────────────────

    [Fact]
    public void RemoveNodes_TheOnlyReferencingNode_RetractsThePeer()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var peerId = Guid.NewGuid();
        var node = new CallPeerBlueprintNode { Id = Guid.NewGuid(), PeerBlueprintId = peerId.ToString("D") };
        graph.Nodes.Add(node);
        asset.CallablePeers.Add(peerId);

        var sink = MakeSink(asset, graph);
        var result = sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(node.Id) }));

        Assert.True(result.Success);
        Assert.DoesNotContain(peerId, asset.CallablePeers);
    }

    [Fact]
    public void RemoveNodes_OneOfTwoReferencingNodes_KeepsThePeer()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var peerId = Guid.NewGuid();
        var node1 = new CallPeerBlueprintNode { Id = Guid.NewGuid(), PeerBlueprintId = peerId.ToString("D") };
        var node2 = new CallPeerBlueprintNode { Id = Guid.NewGuid(), PeerBlueprintId = peerId.ToString("N") };
        graph.Nodes.Add(node1);
        graph.Nodes.Add(node2);
        asset.CallablePeers.Add(peerId);

        var sink = MakeSink(asset, graph);
        var result = sink.Apply(new GraphCommand.RemoveNodes(new[] { new NodeId(node1.Id) }));

        Assert.True(result.Success);
        Assert.Contains(peerId, asset.CallablePeers);
    }

    // ── 7. Helper unit tests ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public void Declare_NullEmptyOrGarbageId_ReturnsFalseAndDoesNotThrow(string? invalid)
    {
        var (asset, _) = MakeAssetWithGraph();

        var result = CallablePeerDeclarations.Declare(asset, invalid);

        Assert.False(result);
        Assert.Empty(asset.CallablePeers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void RetractIfUnreferenced_NullEmptyOrGarbageId_ReturnsFalseAndDoesNotThrow(string? invalid)
    {
        var (asset, _) = MakeAssetWithGraph();
        var id = Guid.NewGuid();
        asset.CallablePeers.Add(id);

        var result = CallablePeerDeclarations.RetractIfUnreferenced(asset, invalid);

        Assert.False(result);
        Assert.Contains(id, asset.CallablePeers);
    }

    [Fact]
    public void RetractIfUnreferenced_PeerStillReferencedByAnotherNode_DoesNotRemove()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var peerId = Guid.NewGuid();
        graph.Nodes.Add(new CallPeerBlueprintNode
        {
            Id = Guid.NewGuid(),
            PeerBlueprintId = peerId.ToString("D"),
        });
        asset.CallablePeers.Add(peerId);

        var removed = CallablePeerDeclarations.RetractIfUnreferenced(asset, peerId.ToString("D"));

        Assert.False(removed);
        Assert.Contains(peerId, asset.CallablePeers);
    }

    [Fact]
    public void RetractIfUnreferenced_NoReferencingNode_Removes()
    {
        var (asset, _) = MakeAssetWithGraph();
        var peerId = Guid.NewGuid();
        asset.CallablePeers.Add(peerId);

        var removed = CallablePeerDeclarations.RetractIfUnreferenced(asset, peerId.ToString("N"));

        Assert.True(removed);
        Assert.DoesNotContain(peerId, asset.CallablePeers);
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────────

    private sealed class StubPeerProvider : IBlueprintPeerProvider
    {
        private readonly IReadOnlyList<BlueprintPeerInfo> _peers;
        public StubPeerProvider(params BlueprintPeerInfo[] peers) => _peers = peers;
        public IReadOnlyList<BlueprintPeerInfo> GetPeers() => _peers;
    }
}
