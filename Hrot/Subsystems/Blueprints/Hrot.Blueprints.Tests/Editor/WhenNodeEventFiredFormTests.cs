using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-10 — the When node's <c>EventFired</c> mode had a stub form: one
/// <c>ImGui.TextDisabled($"… {entries.Count} events available")</c>. The catalog was already
/// injected and already queried; only the result was never rendered, so the node could be placed
/// and its mode selected, but never pointed at an event.
/// </summary>
public sealed class WhenNodeEventFiredFormTests
{
    private sealed class FakeEventCatalog : IEngineEventCatalog
    {
        private readonly List<EngineEventCatalogEntry> _entries;
        public FakeEventCatalog(params EngineEventCatalogEntry[] entries) => _entries = entries.ToList();
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() => _entries;
    }

    private sealed class FakeChannelCatalog : IChannelCommandCatalog
    {
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries()
            => Array.Empty<ChannelCommandCatalogEntry>();
    }

    private sealed class SpyEditService : IEditService
    {
        public List<(string Label, Action Apply, Action Undo)> Recorded { get; } = new();
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        {
            Recorded.Add((description, apply, undo));
            apply();
        }
        public void NotifyStructureChanged(BlueprintAsset asset) { }
        public void UndoLast() => Recorded[^1].Undo();
    }

    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto p) => (_, _) => true;
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileEntityPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto p) => (_, _) => true;
        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto p) => Array.Empty<Type>();
    }

    private static EngineEventCatalogEntry Entry(
        string name, string fqn, string displayName = "", string targetField = "",
        EventQoS qos = EventQoS.Reliable)
        => new(name, fqn, displayName, Category: "", TargetFieldName: targetField, QoS: qos);

    private static (WhenNodeSession, WhenNode, SpyEditService) MakeSession(params EngineEventCatalogEntry[] entries)
    {
        var svc   = new SpyEditService();
        var asset = BlueprintAssetBuilder.Instance("WhenAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        var node   = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.EventFired };
        var drawer = new WhenNodeDrawer(
            new FakeChannelCatalog(), new FakeEventCatalog(entries), svc, new NullPredicateCompiler());
        return ((WhenNodeSession)drawer.CreateSession(node, asset), node, svc);
    }

    [Fact]
    public void EventTypeId_IsSelectable_AndUndoable()
    {
        var e = Entry("MontageEnded", "Hrot.Events.MontageEnded");
        var (session, node, svc) = MakeSession(e);

        session.SetEventTypeIdForTest(e.EventTypeFqn);
        Assert.Equal(e.EventTypeFqn, node.EventFired?.EventTypeId);

        svc.UndoLast();
        Assert.Equal("", node.EventFired?.EventTypeId);
    }

    [Fact]
    public void EventList_FiltersOverName_DisplayName_AndFqn()
    {
        var a = Entry("MontageEnded", "Hrot.Events.MontageEnded", displayName: "Montage Finished");
        var b = Entry("HitTaken",     "Hrot.Events.HitTaken");
        var (session, _, _) = MakeSession(a, b);

        Assert.Equal(new[] { a }, session.GetFilteredEventsForTest("montage"));
        Assert.Equal(new[] { a }, session.GetFilteredEventsForTest("Finished"));
        Assert.Equal(new[] { b }, session.GetFilteredEventsForTest("hittaken"));
        Assert.Equal(2, session.GetFilteredEventsForTest("").Count);
    }

    /// <summary>
    /// The target field belongs to the event's own payload shape, so it cannot survive a change of
    /// event. Adopting the new entry's field in the same edit keeps the payload coherent — and
    /// keeps it to one undo entry.
    /// </summary>
    [Fact]
    public void ChangingEvent_AdoptsTheNewEventsTargetField()
    {
        var a = Entry("A", "Ns.A", targetField: "TargetEntity");
        var b = Entry("B", "Ns.B", targetField: "Victim");
        var (session, node, _) = MakeSession(a, b);

        session.SetEventTypeIdForTest(a.EventTypeFqn);
        Assert.Equal("TargetEntity", node.EventFired?.TargetFieldName);

        session.SetEventTypeIdForTest(b.EventTypeFqn);
        Assert.Equal("Victim", node.EventFired?.TargetFieldName);
    }

    [Fact]
    public void ChangingEvent_ClearsTheTargetFieldWhenTheNewEventHasNone()
    {
        var a = Entry("A", "Ns.A", targetField: "TargetEntity");
        var b = Entry("B", "Ns.B");
        var (session, node, _) = MakeSession(a, b);

        session.SetEventTypeIdForTest(a.EventTypeFqn);
        session.SetEventTypeIdForTest(b.EventTypeFqn);

        Assert.Null(node.EventFired?.TargetFieldName);
    }

    [Fact]
    public void ChangingEvent_IsOneUndoableEdit_RestoringBothFields()
    {
        var a = Entry("A", "Ns.A", targetField: "TargetEntity");
        var b = Entry("B", "Ns.B", targetField: "Victim");
        var (session, node, svc) = MakeSession(a, b);

        session.SetEventTypeIdForTest(a.EventTypeFqn);
        int before = svc.Recorded.Count;

        session.SetEventTypeIdForTest(b.EventTypeFqn);
        Assert.Equal(before + 1, svc.Recorded.Count);

        svc.UndoLast();
        Assert.Equal(a.EventTypeFqn, node.EventFired?.EventTypeId);
        Assert.Equal("TargetEntity", node.EventFired?.TargetFieldName);
    }

    [Fact]
    public void TargetFilter_IsToggleable_AndUndoable()
    {
        var e = Entry("A", "Ns.A", targetField: "TargetEntity");
        var (session, node, svc) = MakeSession(e);

        session.SetEventTypeIdForTest(e.EventTypeFqn);
        session.SetTargetFilterForTest(EventTargetFilter.None);
        Assert.Equal(EventTargetFilter.None, node.EventFired?.TargetFilter);

        svc.UndoLast();
        Assert.Equal(EventTargetFilter.Self, node.EventFired?.TargetFilter);
    }

    /// <summary>
    /// An event type that matches no catalog entry is a subscription that can never fire; the form
    /// must flag it rather than render it as ordinary.
    /// </summary>
    [Fact]
    public void UnlistedEvent_IsFlagged()
    {
        var (session, node, _) = MakeSession(Entry("A", "Ns.A"));
        node.EventFired = new EventFiredPayload { EventTypeId = "Ns.Removed" };

        Assert.True(session.IsCurrentEventUnlistedForTest());
    }

    [Fact]
    public void UnconfiguredEvent_IsNotFlaggedAsUnlisted()
    {
        var (session, _, _) = MakeSession(Entry("A", "Ns.A"));
        Assert.False(session.IsCurrentEventUnlistedForTest());
    }

    /// <summary>A catalog entry may be referenced by its short Name as well as its FQN.</summary>
    [Fact]
    public void EventStoredByName_Resolves()
    {
        var (session, node, _) = MakeSession(Entry("MontageEnded", "Hrot.Events.MontageEnded"));
        node.EventFired = new EventFiredPayload { EventTypeId = "MontageEnded" };

        Assert.False(session.IsCurrentEventUnlistedForTest());
    }
}
