using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-05 / BP-06 / BP-07 / BP-08 — four node kinds that compiled and ran but had <b>no
/// Details-panel editor at all</b>, so whatever the palette baked at creation was permanent.
///
/// <para>
/// All headless: mutation and list logic live in helpers reachable through internal test hooks, the
/// same split the rest of the drawer suite uses. <c>Draw()</c> needs an ImGui context and is the
/// only part not covered here.
/// </para>
/// </summary>
public sealed class UneditableNodeDrawerTests
{
    private static BlueprintAsset MakeAsset() =>
        BlueprintAssetBuilder.Instance("DrawerGapAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();

    /// <summary>Records what a drawer asks for, and lets a test run the captured undo.</summary>
    private sealed class SpyEditService : IEditService
    {
        public List<(string Label, Action Apply, Action Undo)> Recorded { get; } = new();
        public int MarkDirtyCallCount { get; private set; }
        public int StructureChangedCallCount { get; private set; }

        public void MarkDirty(BlueprintAsset asset) => MarkDirtyCallCount++;

        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        {
            Recorded.Add((description, apply, undo));
            apply();
            MarkDirty(asset);
        }

        public void NotifyStructureChanged(BlueprintAsset asset) => StructureChangedCallCount++;

        /// <summary>Runs the most recent edit's inverse, as the undo stack would.</summary>
        public void UndoLast() => Recorded[^1].Undo();
    }

    // ── BP-05 — ReadRankedResult.Rank ────────────────────────────────────────

    private static (ReadRankedResultNodeSession, ReadRankedResultNode, SpyEditService) MakeRankSession()
    {
        var svc     = new SpyEditService();
        var asset   = MakeAsset();
        var node    = new ReadRankedResultNode { Id = Guid.NewGuid() };
        var drawer  = new ReadRankedResultNodeDrawer(svc);
        return ((ReadRankedResultNodeSession)drawer.CreateSession(node, asset), node, svc);
    }

    [Fact]
    public void Rank_IsEditable_AndUndoable()
    {
        var (session, node, svc) = MakeRankSession();

        session.SetRankForTest(3);
        Assert.Equal(3, node.Rank);

        svc.UndoLast();
        Assert.Equal(0, node.Rank);
    }

    [Fact]
    public void Rank_NegativeInput_IsClampedToZero()
    {
        // A rank indexes the EQS result list; a negative one would index out of range. Clamped
        // rather than rejected, so the stepper cannot author an invalid asset.
        Assert.Equal(0, ReadRankedResultNodeSession.ClampRankForTest(-5));
        Assert.Equal(7, ReadRankedResultNodeSession.ClampRankForTest(7));
    }

    [Fact]
    public void Rank_SettingTheSameValue_RecordsNothing()
    {
        var (session, node, svc) = MakeRankSession();
        session.SetRankForTest(node.Rank);
        Assert.Empty(svc.Recorded);
    }

    // ── BP-06 — WaitForChannel.ChannelType ───────────────────────────────────

    private sealed class FakeChannelCatalog : IChannelCommandCatalog
    {
        private readonly List<ChannelCommandCatalogEntry> _entries;
        public FakeChannelCatalog(params (string Channel, string Action)[] entries)
            => _entries = entries
                .Select(e => new ChannelCommandCatalogEntry(e.Action, e.Channel, 1, ""))
                .ToList();
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => _entries;
    }

    private static (WaitForChannelNodeSession, WaitForChannelNode, SpyEditService) MakeChannelSession(
        IChannelCommandCatalog catalog)
    {
        var svc    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new WaitForChannelNode { Id = Guid.NewGuid() };
        var drawer = new WaitForChannelNodeDrawer(catalog, svc);
        return ((WaitForChannelNodeSession)drawer.CreateSession(node, asset), node, svc);
    }

    [Fact]
    public void ChannelType_IsEditable_AndUndoable()
    {
        var catalog = new FakeChannelCatalog(("Loco", "Move"), ("Aim", "Fire"));
        var (session, node, svc) = MakeChannelSession(catalog);

        session.SetChannelTypeForTest("Aim");
        Assert.Equal("Aim", node.ChannelType);

        svc.UndoLast();
        Assert.Equal("", node.ChannelType);
    }

    /// <summary>
    /// The catalog is keyed by (channel, action), so several entries share a channel. The picker
    /// must offer each channel once — otherwise a channel with eight actions appears eight times.
    /// </summary>
    [Fact]
    public void ChannelList_IsDeduplicatedAndSorted()
    {
        var catalog = new FakeChannelCatalog(
            ("Loco", "Move"), ("Loco", "Stop"), ("Aim", "Fire"), ("Loco", "Turn"));
        var (session, _, _) = MakeChannelSession(catalog);

        Assert.Equal(new[] { "Aim", "Loco" }, session.GetAvailableChannelsForTest());
    }

    [Fact]
    public void ChannelList_FiltersCaseInsensitively()
    {
        var catalog = new FakeChannelCatalog(("Locomotion", "Move"), ("Aim", "Fire"));
        var (session, _, _) = MakeChannelSession(catalog);

        Assert.Equal(new[] { "Locomotion" }, session.GetFilteredChannelsForTest("loco"));
    }

    /// <summary>
    /// A channel from an assembly that is not loaded must be shown and preserved, never silently
    /// blanked by opening and closing the combo.
    /// </summary>
    [Fact]
    public void UnlistedChannel_IsFlagged_NotDiscarded()
    {
        var (session, node, _) = MakeChannelSession(new FakeChannelCatalog(("Loco", "Move")));
        node.ChannelType = "SomeUnloadedChannel";

        Assert.True(session.IsCurrentChannelUnlistedForTest());
        Assert.Equal("SomeUnloadedChannel", node.ChannelType);
    }

    // ── BP-07 — CallCustomEvent.EventId ──────────────────────────────────────

    private static (CallCustomEventNodeSession, CallCustomEventNode, BlueprintAsset, SpyEditService)
        MakeCustomEventSession(params CustomEventDecl[] events)
    {
        var svc   = new SpyEditService();
        var asset = MakeAsset();
        foreach (var e in events) asset.CustomEvents.Add(e);
        var node   = new CallCustomEventNode { Id = Guid.NewGuid() };
        var drawer = new CallCustomEventNodeDrawer(svc);
        return ((CallCustomEventNodeSession)drawer.CreateSession(node, asset), node, asset, svc);
    }

    private static CustomEventDecl Decl(string name, params string[] paramNames)
        => new()
        {
            Id         = Guid.NewGuid(),
            Name       = name,
            Parameters = paramNames.Select(p => new ParameterDecl { Name = p }).ToList(),
        };

    /// <summary>
    /// ⚠ The audit told us to source this picker from <c>UnifiedEventDiscovery</c>. That enumerates
    /// engine events; a custom event is asset-scoped, and <c>NodePinSchema.CallCustomEventPins</c>
    /// resolves the id against <c>asset.CustomEvents</c>. This pins the correct source — an engine
    /// event chosen here would resolve to nothing.
    /// </summary>
    [Fact]
    public void CustomEventList_ComesFromTheOwningAsset()
    {
        var a = Decl("OnAlarm");
        var b = Decl("OnStandDown");
        var (session, _, _, _) = MakeCustomEventSession(a, b);

        Assert.Equal(new[] { a.Id, b.Id }, session.GetAvailableEventsForTest().Select(e => e.Id));
    }

    [Fact]
    public void CustomEvent_IsEditable_AndUndoable()
    {
        var decl = Decl("OnAlarm");
        var (session, node, _, svc) = MakeCustomEventSession(decl);

        session.SetEventIdForTest(decl.Id.ToString("D"));
        Assert.Equal(decl.Id.ToString("D"), node.EventId);

        svc.UndoLast();
        Assert.Equal("", node.EventId);
    }

    /// <summary>The chosen event's parameters become the node's data-IN pins, so this is structural.</summary>
    [Fact]
    public void CustomEvent_Edit_NotifiesStructureChanged()
    {
        var decl = Decl("OnAlarm", "Severity");
        var (session, _, _, svc) = MakeCustomEventSession(decl);

        session.SetEventIdForTest(decl.Id.ToString("D"));

        Assert.True(svc.StructureChangedCallCount >= 1);
    }

    /// <summary>
    /// Stage5's <c>FindCustomEventIndex</c> accepts a bare Name as well as a GUID, so a
    /// hand-authored asset must not be shown as dangling just because it used the name.
    /// </summary>
    [Fact]
    public void CustomEvent_StoredAsName_ResolvesRatherThanShowingAsDangling()
    {
        var decl = Decl("OnAlarm");
        var (session, node, _, _) = MakeCustomEventSession(decl);
        node.EventId = "OnAlarm";

        Assert.False(session.IsCurrentEventUnresolvedForTest());
    }

    [Fact]
    public void CustomEvent_DanglingId_IsFlagged()
    {
        var (session, node, _, _) = MakeCustomEventSession(Decl("OnAlarm"));
        node.EventId = Guid.NewGuid().ToString("D");

        Assert.True(session.IsCurrentEventUnresolvedForTest());
    }

    [Fact]
    public void CustomEvent_Label_ShowsParameterNames()
    {
        Assert.Equal("OnAlarm (Severity, Source)",
            CallCustomEventNodeSession.LabelForTest(Decl("OnAlarm", "Severity", "Source")));
        Assert.Equal("OnAlarm", CallCustomEventNodeSession.LabelForTest(Decl("OnAlarm")));
    }

    // ── BP-08 — CallPeerBlueprint target ─────────────────────────────────────

    private sealed class FakePeerProvider : IBlueprintPeerProvider
    {
        private readonly List<BlueprintPeerInfo> _peers;
        public FakePeerProvider(params BlueprintPeerInfo[] peers) => _peers = peers.ToList();
        public IReadOnlyList<BlueprintPeerInfo> GetPeers() => _peers;
    }

    private static (CallPeerBlueprintNodeSession, CallPeerBlueprintNode, SpyEditService)
        MakePeerSession(IBlueprintPeerProvider peers)
    {
        var svc    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new CallPeerBlueprintNode { Id = Guid.NewGuid() };
        var drawer = new CallPeerBlueprintNodeDrawer(svc, peers);
        return ((CallPeerBlueprintNodeSession)drawer.CreateSession(node, asset), node, svc);
    }

    [Fact]
    public void Peer_IsEditable_AndUndoable()
    {
        var peer = new BlueprintPeerInfo(Guid.NewGuid(), "Squadmate", new[] { "Advance" });
        var (session, node, svc) = MakePeerSession(new FakePeerProvider(peer));

        session.SetPeerForTest(peer.AssetId);
        Assert.Equal(peer.AssetId.ToString("D"), node.PeerBlueprintId);

        svc.UndoLast();
        Assert.Equal("", node.PeerBlueprintId);
    }

    [Fact]
    public void FunctionList_IsScopedToTheSelectedPeer()
    {
        var a = new BlueprintPeerInfo(Guid.NewGuid(), "A", new[] { "Alpha" });
        var b = new BlueprintPeerInfo(Guid.NewGuid(), "B", new[] { "Beta", "Gamma" });
        var (session, _, _) = MakePeerSession(new FakePeerProvider(a, b));

        session.SetPeerForTest(b.AssetId);

        Assert.Equal(new[] { "Beta", "Gamma" }, session.GetFunctionsForCurrentPeerForTest());
    }

    /// <summary>
    /// Switching to a peer that does not export the current function must clear it. Leaving it
    /// would silently collapse the node's pins to the untyped exec+Return fallback with nothing on
    /// screen to explain why.
    /// </summary>
    [Fact]
    public void ChangingPeer_ClearsAFunctionTheNewPeerDoesNotExport()
    {
        var a = new BlueprintPeerInfo(Guid.NewGuid(), "A", new[] { "Alpha" });
        var b = new BlueprintPeerInfo(Guid.NewGuid(), "B", new[] { "Beta" });
        var (session, node, _) = MakePeerSession(new FakePeerProvider(a, b));

        session.SetPeerForTest(a.AssetId);
        session.SetFunctionForTest("Alpha");
        Assert.Equal("Alpha", node.FunctionRef);

        session.SetPeerForTest(b.AssetId);

        Assert.Equal("", node.FunctionRef);
    }

    /// <summary>...but a function both peers export survives the switch.</summary>
    [Fact]
    public void ChangingPeer_KeepsAFunctionTheNewPeerAlsoExports()
    {
        var a = new BlueprintPeerInfo(Guid.NewGuid(), "A", new[] { "Shared" });
        var b = new BlueprintPeerInfo(Guid.NewGuid(), "B", new[] { "Shared", "Other" });
        var (session, node, _) = MakePeerSession(new FakePeerProvider(a, b));

        session.SetPeerForTest(a.AssetId);
        session.SetFunctionForTest("Shared");
        session.SetPeerForTest(b.AssetId);

        Assert.Equal("Shared", node.FunctionRef);
    }

    /// <summary>Peer + function clear in ONE edit, so undo restores both together.</summary>
    [Fact]
    public void ChangingPeer_IsASingleUndoableEdit_RestoringBothFields()
    {
        var a = new BlueprintPeerInfo(Guid.NewGuid(), "A", new[] { "Alpha" });
        var b = new BlueprintPeerInfo(Guid.NewGuid(), "B", new[] { "Beta" });
        var (session, node, svc) = MakePeerSession(new FakePeerProvider(a, b));

        session.SetPeerForTest(a.AssetId);
        session.SetFunctionForTest("Alpha");

        int before = svc.Recorded.Count;
        session.SetPeerForTest(b.AssetId);
        Assert.Equal(before + 1, svc.Recorded.Count);

        svc.UndoLast();
        Assert.Equal(a.AssetId.ToString("D"), node.PeerBlueprintId);
        Assert.Equal("Alpha", node.FunctionRef);
    }

    [Fact]
    public void DanglingPeerAndFunction_AreFlaggedSeparately()
    {
        var peer = new BlueprintPeerInfo(Guid.NewGuid(), "A", new[] { "Alpha" });
        var (session, node, _) = MakePeerSession(new FakePeerProvider(peer));

        node.PeerBlueprintId = Guid.NewGuid().ToString("D");
        Assert.True(session.IsCurrentPeerUnresolvedForTest());

        node.PeerBlueprintId = peer.AssetId.ToString("D");
        node.FunctionRef     = "NotExported";
        Assert.False(session.IsCurrentPeerUnresolvedForTest());
        Assert.True(session.IsCurrentFunctionUnresolvedForTest());
    }

    [Fact]
    public void PeerFilter_MatchesNameAndAssetId()
    {
        var a = new BlueprintPeerInfo(Guid.NewGuid(), "Squadmate", Array.Empty<string>());
        var b = new BlueprintPeerInfo(Guid.NewGuid(), "Commander", Array.Empty<string>());
        var (session, _, _) = MakePeerSession(new FakePeerProvider(a, b));

        Assert.Equal(new[] { a.AssetId }, session.GetFilteredPeersForTest("squad").Select(p => p.AssetId));
        Assert.Equal(new[] { b.AssetId },
            session.GetFilteredPeersForTest(b.AssetId.ToString()).Select(p => p.AssetId));
    }

    /// <summary>No provider wired means an explicit empty list, not a crash.</summary>
    [Fact]
    public void PeerDrawer_WithoutAProvider_DegradesToNoPeers()
    {
        var drawer  = new CallPeerBlueprintNodeDrawer(new SpyEditService());
        var session = (CallPeerBlueprintNodeSession)drawer.CreateSession(
            new CallPeerBlueprintNode { Id = Guid.NewGuid() }, MakeAsset());

        Assert.Empty(session.GetAvailablePeersForTest());
    }

    // ── Registration ─────────────────────────────────────────────────────────

    /// <summary>
    /// All four must be registered, or the Details panel falls back to "no editor for this node" —
    /// which is exactly the state BP-05…BP-08 describe.
    /// </summary>
    [Theory]
    [InlineData(typeof(ReadRankedResultNode))]
    [InlineData(typeof(WaitForChannelNode))]
    [InlineData(typeof(CallCustomEventNode))]
    [InlineData(typeof(CallPeerBlueprintNode))]
    public void Drawer_IsRegistered_InTheProductionRegistry(Type nodeType)
    {
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            new FakeChannelCatalog(("Loco", "Move")),
            new EmptyEngineEventCatalog(),
            new SpyEditService(),
            new NullPredicateCompiler(),
            new EqsTemplateRegistry());

        Assert.True(registry.TryGet(nodeType, out var drawer));
        Assert.NotNull(drawer);
    }

    private sealed class EmptyEngineEventCatalog : IEngineEventCatalog
    {
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() => Array.Empty<EngineEventCatalogEntry>();
    }

    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileEntityPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => Array.Empty<Type>();
    }
}
