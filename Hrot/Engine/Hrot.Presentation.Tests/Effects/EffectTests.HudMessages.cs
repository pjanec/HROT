using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Effects;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Effects;

/// <summary>
/// ⭐ CE-1461 W-M7 (docs/DESIGN_Node_Features.md §10) — the effect system keeps the player's messages only while <c>hud.messages</c> is on (null gate = ungated, as every
/// <c>hud.*</c>), once per <c>MessageId</c> + origin, and the declaration is ON for the Player and for every other host.
/// Red-proof (compiles): drop the <c>RecordsMessages</c> test in <c>TrackMessages</c> ⇒ the feature-off rail fails.
/// </summary>
public sealed partial class EffectTests
{
    private static int MessagesKept(IFeatureGate? gate, int sends = 1)
    {
        var (world, _) = World();
        world.Bus.RegisterManaged<PlayerMessageEvent>();
        At(world, 10.0);
        for (int i = 0; i < sends; i++)
        {
            world.Bus.PublishManaged(new PlayerMessageEvent { MessageId = 5, OriginNodeId = 0, ToKind = "All", Text = "go", Seconds = 6f, SentAt = 10.0 });
            Tick(world, new EffectSpawnSystem { FeatureGate = () => gate }, 0.016f);   // the same message offered again on a later frame
        }
        return PlayerMessages.Of(world)?.Log(default).Count ?? 0;
    }

    [Fact]
    public void CE1461_Messages_AreKeptWhileTheFeatureIsOn_NotWhenItIsOff_AndUngatedIsOn()
    {
        Assert.Equal(1, MessagesKept(Gate(playerDefault: false)));                                    // declared ON on every host
        Assert.Equal(1, MessagesKept(Gate(playerDefault: true)));
        Assert.Equal(1, MessagesKept(null));
        Assert.Equal(0, MessagesKept(Gate(playerDefault: true, (HudFeatures.Messages, false))));      // switched off: nothing is kept
        Assert.Equal(1, MessagesKept(Gate(playerDefault: false), sends: 3));                          // dedupe by MessageId + origin
    }

    [Fact]
    public void CE1461_TheMessagesFeature_IsDeclaredLive_AndListedInAll()
    {
        Assert.Contains(HudFeatures.Messages, HudFeatures.All);
        Assert.Equal("hud.messages", HudFeatures.Messages);
        Assert.Equal(HudFeatures.All.Length, HudFeatures.Declarations(true).Count());
    }
}
