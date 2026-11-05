using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Gameplay;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ Player Join D-4 — the thin deploy screen's RULES (no ImGui): when it shows, what it says, how it groups the offers. The state machine is <see cref="DeployFlowSystem"/>'s
/// (<c>GameplayDeployFlowTests</c>); the screen only reads it. ✅ Red-proof: the loading-screen yield removed ⇒ the hidden case shows ⇒ red.
/// </summary>
public sealed class DeployScreenRulesTests
{
    private static (DeployFlowSystem Flow, EntityRepository World) Flow(bool open, bool live, bool server)
    {
        var w = new EntityRepository();
        var f = new DeployFlowSystem(new GameplayWire(null), w, new NetworkEntityMap(), 30, new DeployFlowEnvironment(() => open, () => live, () => server, _ => (true, "")));
        f.Execute(w, 0.016f);
        return (f, w);
    }

    [Fact]
    public void ItShowsTheWaitingReason_ButNotWhileTheLoadingScreenIsUp_AndNotWhilePlaying()
    {
        var (flow, _) = Flow(open: true, live: false, server: true);
        bool loading = false;
        var screen = new DeployScreen(() => flow, () => loading);
        Assert.True(screen.Visible); Assert.Equal("Waiting for the server to load a scenario", screen.Headline);
        loading = true;
        Assert.False(screen.Visible);                                  // the gate-closed loading screen speaks, not this one
        var (closed, _) = Flow(open: false, live: false, server: true);
        Assert.False(new DeployScreen(() => closed, () => false).Visible);   // Waiting with no text: nothing to say
        Assert.False(new DeployScreen(() => null, () => false).Visible);     // no flow on this node
    }

    [Fact]
    public void WithNoServerItSaysObserving_AndWithOneItChoosesAndGroupsTheOffersBySide()
    {
        var (none, _) = Flow(open: true, live: true, server: false);
        var screen = new DeployScreen(() => none, () => false);
        Assert.Equal(DeployFlowState.Observing, none.State);
        Assert.Equal("No player server — observing", screen.Headline);

        var (some, _) = Flow(open: true, live: true, server: true);
        Assert.Equal("Deploy", new DeployScreen(() => some, () => false).Headline);
        var offers = new[]
        {
            new DeployOffer(3, ForceId.Hostile, "B", 0), new DeployOffer(1, ForceId.Friend, "A", 0), new DeployOffer(2, ForceId.Friend, "C", 9),
        };
        Assert.Equal(new[] { ForceId.Friend, ForceId.Hostile }, DeployScreen.SidesOf(offers));
        Assert.Equal(new[] { 1, 2 }, DeployScreen.OffersOf(offers, ForceId.Friend).Select(o => o.UnitNetId));
    }
}
