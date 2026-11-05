using System.Linq;
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

    /// <summary>S2 — a role row shows its live count against its max ("Rifleman  2 / 4", just the count when unlimited), a placed unit its name. ✅ Red-proof: the max dropped ⇒ red.</summary>
    [Fact]
    public void ARoleRow_ShowsItsCountAgainstItsMax()
    {
        Assert.Equal("Rifleman  2 / 4", DeployScreen.RowText(new DeployOffer(300, ForceId.Friend, "Rifleman", 0, Kind: 1, Count: 2, Max: 4)));
        Assert.Equal("Rifleman  3", DeployScreen.RowText(new DeployOffer(-2002, ForceId.Friend, "Rifleman", 0, Kind: 1, Count: 3, Max: 0)));
        Assert.Equal("Alpha 1", DeployScreen.RowText(new DeployOffer(5, ForceId.Friend, "Alpha 1", 0)));
        Assert.Equal("#5", DeployScreen.RowText(new DeployOffer(5, ForceId.Friend, "", 0)));

    }

    /// <summary>S3 — a role row expands to one button per SET extra kit ("Rifleman — Grenadier"; an unset kit gives none, kit 0 is the row itself); the offers split into units/roles and the side's AREAS (sides are listed for their
    /// units, never for an area alone). The screen's area choice defaults to "Any" (0). ✅ Red-proof: an unset kit listed ⇒ red; areas mixed into the unit rows ⇒ red.</summary>
    [Fact]
    public void ARoleExpandsToItsKits_AndTheAreasAreListedApart()
    {
        var role = new DeployOffer(300, ForceId.Friend, "Rifleman", 0, Kind: 1, Count: 1, Max: 4, Kits: new[] { "Rifleman", "Grenadier", "", "MG" });
        Assert.Equal(new[] { ((byte)1, "Rifleman — Grenadier"), ((byte)3, "Rifleman — MG") }, DeployScreen.KitRows(role));
        Assert.Empty(DeployScreen.KitRows(new DeployOffer(301, ForceId.Friend, "Plain", 0, Kind: 1)));
        Assert.Empty(DeployScreen.KitRows(new DeployOffer(5, ForceId.Friend, "Placed", 0)));

        var offers = new[] { role, new DeployOffer(400, ForceId.Friend, "Church", 0, Kind: 2), new DeployOffer(402, ForceId.Hostile, "Enemy camp", 0, Kind: 2) };
        Assert.Equal(new[] { 300 }, DeployScreen.OffersOf(offers, ForceId.Friend).Select(o => o.UnitNetId));
        Assert.Equal(new[] { 400 }, DeployScreen.AreasOf(offers, ForceId.Friend).Select(o => o.UnitNetId));
        Assert.Equal(new[] { ForceId.Friend }, DeployScreen.SidesOf(offers));
        Assert.Equal(0, new DeployScreen(() => null, () => false).AreaNetId);
    }
}
