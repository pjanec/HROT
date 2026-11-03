using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Combat.Events;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Effects;
using Xunit;

namespace Hrot.Presentation.Tests.Effects;

/// <summary>
/// ⭐ CE-4148 (Node Features S2; DESIGN_Player_Join §7a G1/G2) — the hit memory is recorded only while a <c>hud.*</c> reader of it is on;
/// the declarations carry the right defaults per host kind.
/// Red-proof (compiles): drop <c>IsOn(gate, HitMarker)</c> from <c>HudFeatures.RecordsHitFeedback</c> ⇒ the "marker alone keeps the memory" rail fails.
/// </summary>
public sealed partial class EffectTests
{
    internal static NodeFeatures Gate(bool playerDefault, params (string Id, bool On)[] overrides)
    {
        var f = new NodeFeatures(new FeatureRegistry(HudFeatures.Declarations(playerDefault)), NodeRole.Map2D);
        foreach (var (id, on) in overrides) f.SetNodeOverride(id, on ? "{\"Enabled\":true}" : "{\"Enabled\":false}");
        return f;
    }

    private (EntityRepository World, int Recorded) HitOnce(IFeatureGate? gate)
    {
        var (world, _) = World();
        At(world, 10.0);
        var shooter = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(0, 0, 0));
        var victim = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(30, 0, 0));
        world.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.DetonationNotification { Shooter = shooter, Target = victim, HitX = 30, HitY = 0, HitZ = 1.2f, TravelX = 1f, Damage = 30f });
        world.Bus.Publish(new Fdp.Toolkit.Combat.StrikeNotification { Striker = shooter, Target = victim, Weapon = Fdp.Toolkit.Combat.MeleeWeapon.MeleeFist });
        Tick(world, new EffectSpawnSystem { FeatureGate = () => gate }, 0.016f);
        return (world, HitFeedback.Of(world)?.Recent(10.0, HitFeedback.KeepSeconds).Count() ?? 0);
    }

    [Fact]
    public void CE4148_WithEveryHudReaderOff_NoHitIsRecorded_OnAndUngated_TodaysBehaviour()
    {
        Assert.Equal(0, HitOnce(Gate(playerDefault: false)).Recorded);                 // a non-Player host: all three readers off
        Assert.Equal(2, HitOnce(Gate(playerDefault: true)).Recorded);                  // the Player: a round and a blow, kept
        Assert.Equal(2, HitOnce(null).Recorded);                                       // no gate: ungated, as before
    }

    [Fact]
    public void CE4148_AnySingleReader_KeepsTheMemory_TheMarkerAloneIncluded()
    {
        foreach (var only in new[] { HudFeatures.HitMarker, HudFeatures.DamageArc, HudFeatures.KillCam })
        {
            var gate = Gate(false, (only, true));
            Assert.True(2 == HitOnce(gate).Recorded, only);
        }
    }

    [Fact]
    public void CE4148_TheDeclarations_DefaultOnForThePlayerOnly_SpreadAndNearMissOnEverywhere()
    {
        var player = Gate(playerDefault: true);
        var other = Gate(playerDefault: false);
        Assert.Equal(17, HudFeatures.All.Length);   // ⭐ CE-1461 — hud.messages joined (7); ⭐ CE-4226..4228 — the player HUD's eight (15); ⭐ CE-4231 — game.scoreboard (16); ⭐ CE-4243 — gm.palette (17)
        foreach (var id in new[] { HudFeatures.HitMarker, HudFeatures.DamageArc, HudFeatures.KillCam, HudFeatures.SquadTakeoverAfterDeath })
        {
            Assert.True(HudFeatures.IsOn(player, id), id);
            Assert.False(HudFeatures.IsOn(other, id), id);
        }
        foreach (var id in new[] { HudFeatures.SpreadRing, HudFeatures.NearMiss })
        {
            Assert.True(HudFeatures.IsOn(player, id), id);
            Assert.True(HudFeatures.IsOn(other, id), id);
        }
        foreach (var d in HudFeatures.Declarations(false)) Assert.Equal(FeatureChange.Live, d.Change);
        // a gate whose host did not declare the feature: off; no gate at all: on
        var undeclared = new NodeFeatures(new FeatureRegistry(System.Array.Empty<FeatureDeclaration>()), NodeRole.Map2D);
        Assert.False(HudFeatures.IsOn(undeclared, HudFeatures.KillCam));
        Assert.True(HudFeatures.IsOn(null, HudFeatures.KillCam));
    }
}
