using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐ <c>CE-1248</c> — the Brain tab's reader and its place on every host. 📄 docs/DESIGN_Entity_Brain_View.md §1–§8.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheBrainViewTests : IDisposable
{
    private const int Patrol = 11, Duck = 22;

    public TheBrainViewTests() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }

    public void Dispose() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }

    private static string? Names(int hash) => hash switch { Patrol => "Patrol", Duck => "Duck", _ => null };

    private static EntityRepository World(bool brainNode = true)
    {
        var w = new EntityRepository();
        w.RegisterComponent<BehaviorState>();
        if (brainNode)
        {
            w.RegisterComponent<ActionStatus>();
            w.RegisterComponent<BrainJournal>();
        }
        return w;
    }

    private static Entity Soldier(EntityRepository w, bool brainNode = true)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new BehaviorState
        {
            ActiveBehaviorHash = Duck, Origin = BehaviorOrigin.Reaction, Urgency = ReactionUrgency.UnderFire,
        });
        if (!brainNode) return e;
        var status = new ActionStatus();
        status.Weapon.ActionId = 7;
        status.Weapon.Reason = ActionReason.HoldNotSeen;
        w.AddComponent(e, status);
        w.AddComponent(e, new BrainJournal());
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskStarted, a: Patrol, origin: (byte)BehaviorOrigin.Superior);
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskPaused, a: Patrol, b: Duck);
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskStarted, a: Duck, origin: (byte)BehaviorOrigin.Reaction,
            urgency: (byte)ReactionUrgency.UnderFire);
        return e;
    }

    /// <summary>⭐ Doing / Ordered by / Right now / Recent changes read from what the brain records, names resolved, newest change first.
    /// 🔴 Red-proof: list the journal oldest first in <c>BrainSnapshot.Read</c> ⇒ red.</summary>
    [Fact]
    public void CE1248_TheReader_TellsWhatItDoes_WhoOrderedIt_AndWhatChanged()
    {
        using var w = World();
        var b = BrainSnapshot.Read(w, Soldier(w), Names)!;

        Assert.True(b.OnBrainNode);
        Assert.Equal("Duck", b.Doing);
        Assert.Equal("Reaction", b.Origin);
        Assert.Equal("UnderFire", b.Urgency);
        var row = Assert.Single(b.RightNow);
        Assert.Equal("Weapon", row.Channel);
        Assert.True(row.IsHold);
        Assert.Equal(3u, b.JournalSeq);
        Assert.Equal(3, b.Changes.Count);
        Assert.StartsWith("started Duck (Reaction, UnderFire)", b.Changes[0].Text);
        Assert.Equal("paused Patrol for Duck", b.Changes[1].Text);
    }

    /// <summary>⭐ BV-6 — a node without the brain says so instead of showing empty rows; no BehaviorState = no brain.</summary>
    [Fact]
    public void CE1248_ANodeWithoutTheBrain_SaysSo()
    {
        using var w = World(brainNode: false);
        var b = BrainSnapshot.Read(w, Soldier(w, brainNode: false), Names)!;
        Assert.False(b.OnBrainNode);
        Assert.False(b.JournalOn);
        Assert.Equal("Duck", b.Doing);

        var bare = w.CreateEntity();
        Assert.Null(BrainSnapshot.Read(w, bare, Names));
    }

    /// <summary>⭐ BV-1 — the shared Details bundle offers the Brain tab for a brain unit (every host composes it), and the tab dumps what
    /// it shows. 🔴 Red-proof: drop the <c>EntityBrainViewDescriptor</c> line from <c>EntityDetailsBundle</c> ⇒ red.</summary>
    [Fact]
    public void CE1248_TheBundle_OffersTheBrainTab_ForABrainUnit_AndItDumps()
    {
        using var w = World();
        var e = Soldier(w);
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var bundle = new EntityDetailsBundle(new EntityDetailsHostServices(
            WindowId: "details_ce1248", Perspective: "IG", World: () => w,
            Inspector: () => null, Session: () => null, RunState: () => VariableRunState.Running, Host: () => null));
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(windows));

        var ctx = DetailsContext.Empty("IG") with { Entities = new[] { e } };
        var offered = bundle.Workspace.DetailsViews.OfferSet(ctx);
        Assert.Contains(offered, d => d.Id == EntityBrainViewDescriptor.ViewId);
        Assert.Equal(EntitySummaryViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(ctx)?.Id);   // Summary stays the card

        var plain = w.CreateEntity();
        Assert.DoesNotContain(bundle.Workspace.DetailsViews.OfferSet(DetailsContext.Empty("IG") with { Entities = new[] { plain } }),
            d => d.Id == EntityBrainViewDescriptor.ViewId);

        PanelSnapshot.CaptureEnabled = true;
        var view = (EntityBrainView)Array.Find(System.Linq.Enumerable.ToArray(offered), d => d.Id == EntityBrainViewDescriptor.ViewId)!.Create();
        view.SimulateDraw(ctx, "details_ce1248");
        var dump = PanelSnapshot.TryGet($"details_ce1248/{EntityBrainViewDescriptor.ViewId}")!.Dump();
        Assert.Equal(3u, dump["brain"]!["journal"]!["seq"]!.GetValue<uint>());
    }

    /// <summary>⭐ A region entered from no state (0xFFFF — the machine's start) reads "entered", not <c>State_65535 → …</c> (measured live
    /// `2026-10-23`). 🔴 Red-proof: drop the 0xFFFF branch in <c>BrainJournalText.Describe</c> ⇒ red.</summary>
    [Fact]
    public void ARegionEnteredFromNoState_ReadsEntered()
    {
        var e = new BrainJournalEntry { Kind = BrainJournalKind.StateChanged, A = 0xFFFF | (3 << 16), B = 7 };
        Assert.Equal("M region 0: entered state 3", BrainJournalText.Describe(e, _ => "M"));
        var moved = new BrainJournalEntry { Kind = BrainJournalKind.StateChanged, A = 2 | (3 << 16), B = 7 };
        Assert.Equal("M region 0: state 2 → state 3", BrainJournalText.Describe(moved, _ => "M"));
    }

    /// <summary>⭐ <c>CE-1254</c> (BV-21) — the locator names the unit's RUNNING behaviour and its tier (what "Open running behaviour"
    /// opens); no behaviour, no answer. 🔴 Red-proof: read the SOP instead of <c>ActiveBehaviorHash</c> ⇒ red.</summary>
    [Fact]
    public void TheLocator_NamesTheRunningBehaviour_AndItsTier()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<BehaviorState>();
        var unit = w.CreateEntity();
        w.AddComponent(unit, new BehaviorState { ActiveBehaviorHash = 5 });
        var def = new Fdp.Toolkit.Behavior.BehaviorDefinition { Name = "PlatoonDefend", BrainTier = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierHsm };

        var running = RunningBehaviourLocator.Resolve(w, unit, id => id == 5 ? "PlatoonDefend" : null, id => id == 5 ? def : null);
        Assert.Equal(new RunningRef(5, "PlatoonDefend", "HSM"), running);
        Assert.Null(RunningBehaviourLocator.Resolve(w, w.CreateEntity(), _ => "x", null));
    }
}
