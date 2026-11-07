using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Infrastructure;
using Hrot.Presentation.Tests.Effects;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1461 W-M7 (docs/DESIGN_Node_Features.md §10 M-2, M-3) — what the station shows of the player's messages: nothing with <c>hud.messages</c> off; the held
/// unit's id / side / station address them; the log opens on the key and not at all when the feature is off.
/// Red-proof (compiles): drop the <c>HudOn(Messages)</c> test in <c>MessagesToShow</c> ⇒ the feature-off rail fails.
/// </summary>
public sealed partial class PossessionStationTests
{
    private (PossessionStation Station, EntityRepository W, Entity Me) MessageSetup(IFeatureGate? gate)
    {
        var (w, me, _) = World();
        w.RegisterComponent<EntityInfo>();
        w.SetComponent(me, new EntityInfo { ForceId = ForceId.Friend });
        w.Bus.RegisterManaged<PlayerMessageEvent>();
        w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 12.0 });
        var store = PlayerMessages.In(w);
        void Add(int id, string kind, int value, string text, double at = 10) => store.Record(new PlayerMessageEvent { MessageId = id, ToKind = kind, ToValue = value, Text = text, Seconds = 6f, SentAt = (SimInstant)at });
        Add(1, "All", 0, "to all");
        Add(2, "Unit", 4242, "to my unit");          // 4242: the held unit's network id (World())
        Add(3, "Unit", 99, "to another unit");
        Add(4, "Side", (int)ForceId.Friend, "to my side");
        Add(5, "Side", (int)ForceId.Hostile, "to the other side");
        Add(6, "Instructor", 0, "to the instructor");
        var (_, sw, _) = Map();
        return (new PossessionStation(sw, () => w, featureGate: () => gate), w, me);
    }

    [Fact]
    public void CE1461_TheStation_ShowsOnlyWhatIsAddressedToTheHeldUnit_AndNothingWithTheFeatureOff()
    {
        var (on, w, me) = MessageSetup(EffectTests.Gate(playerDefault: true));
        using (w)
        {
            var shown = on.MessagesToShow(w, me)!.Value;
            Assert.Equal(2, shown.Subtitles.Count);                       // two stack
            Assert.Null(shown.Log);                                       // the log is closed
            on.ToggleMessageLog();
            Assert.True(on.MessageLogOpen);
            Assert.Equal(new[] { "to all", "to my side", "to my unit" }, on.MessagesToShow(w, me)!.Value.Log!.Select(e => e.Text).OrderBy(t => t));   // not the other unit, the other side or the instructor
        }
        var (off, w2, me2) = MessageSetup(EffectTests.Gate(playerDefault: true, (HudFeatures.Messages, false)));
        using (w2)
        {
            Assert.Null(off.MessagesToShow(w2, me2));
            off.ToggleMessageLog();
            Assert.False(off.MessageLogOpen);                                                                                // the key does nothing
        }
    }

    [Fact]
    public void CE1461_AStationAddressedMessage_NeedsTheStationId_AStationWithNoMapMatchesNothing()
    {
        var (st, w, me) = MessageSetup(null);
        using (w)
        {
            Assert.Equal(0, st.ViewerOf(w, me).StationId);                                 // no authority on the unit: the station cannot be told, so nothing is addressed to it
            w.RegisterComponent<NetworkAuthority>();
            w.AddComponent(me, new NetworkAuthority(primaryOwnerId: 5, localNodeId: 5));
            int stationId = st.ViewerOf(w, me).StationId;
            Assert.Equal(5, stationId);
            PlayerMessages.In(w).Record(new PlayerMessageEvent { MessageId = 9, ToKind = "Station", ToValue = stationId, Text = "to my station", Seconds = 6f, SentAt = (SimInstant)11 });
            Assert.Contains(PlayerMessages.In(w).Log(st.ViewerOf(w, me)), e => e.Text == "to my station");
            Assert.DoesNotContain(PlayerMessages.In(w).Log(default), e => e.Text == "to my station");   // the unknown station (0) is no match, even for ToValue 0
        }
    }
}
