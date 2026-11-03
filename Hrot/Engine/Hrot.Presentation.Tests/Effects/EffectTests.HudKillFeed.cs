using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Effects;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Effects;

/// <summary>
/// ⭐ H2 / PH-5 (CE-4228) — the effect system keeps every <see cref="ManDownedEvent"/> of the bus in the kill feed only while <c>hud.kill-feed</c> is on (null gate = ungated),
/// one entry per event, and the player-HUD declarations are ON on every host (R-4115, PH-8 amended).
/// Red-proof (compiles): drop the <c>RecordsKills</c> test in <c>TrackKills</c> ⇒ the feature-off rail fails.
/// </summary>
public sealed partial class EffectTests
{
    private static int KillsKept(IFeatureGate? gate, int events = 1)
    {
        var (world, _) = World();
        world.RegisterEvent<ManDownedEvent>();
        At(world, 10.0);
        for (int i = 0; i < events; i++)
            world.Bus.Publish(new ManDownedEvent { State = InjuryState.Dead, VictimSide = ForceId.Friend });
        Tick(world, new EffectSpawnSystem { FeatureGate = () => gate }, 0.016f);
        return KillFeed.Of(world)?.All.Count ?? 0;
    }

    [Fact]
    public void CE4228_TheKillFeed_KeepsEachDowningWhileTheFeatureIsOn_AndNothingWhenItIsOff()
    {
        Assert.Equal(1, KillsKept(Gate(playerDefault: false)));
        Assert.Equal(1, KillsKept(Gate(playerDefault: true)));
        Assert.Equal(1, KillsKept(null));
        Assert.Equal(3, KillsKept(Gate(playerDefault: false), events: 3));
        Assert.Equal(0, KillsKept(Gate(playerDefault: true, (HudFeatures.KillFeed, false))));
    }

    [Fact]
    public void CE4226_ThePlayerHudFeatures_AreDeclaredLive_OnEverywhere_AndListedInAll()
    {
        string[] ids = { HudFeatures.Objectives, HudFeatures.OwnEvaluation, HudFeatures.Tickets, HudFeatures.CapturePoints, HudFeatures.RoundTimer, HudFeatures.KillFeed, HudFeatures.ScorePopups, HudFeatures.BountyMarker };
        Assert.Equal(new[] { "hud.objectives", "hud.own-evaluation", "game.tickets", "game.capture-points", "game.round-timer", "hud.kill-feed", "game.score-popups", "game.bounty-marker" }, ids);
        foreach (var id in ids) Assert.Contains(id, HudFeatures.All);
        Assert.Equal(HudFeatures.All.Length, HudFeatures.Declarations(true).Count());
        Assert.Equal(HudFeatures.All.Length, HudFeatures.All.Distinct().Count());
        foreach (bool player in new[] { true, false })
        {
            var gate = Gate(player);
            foreach (var id in ids) Assert.True(HudFeatures.IsOn(gate, id), $"{id} is ON by default (playerDefault {player})");
        }
        Assert.False(HudFeatures.IsOn(Gate(true, (HudFeatures.Objectives, false)), HudFeatures.Objectives));
        Assert.True(HudFeatures.IsOn(Gate(true, (HudFeatures.Objectives, false)), HudFeatures.Messages));   // hud.messages stays on everywhere
    }
}
