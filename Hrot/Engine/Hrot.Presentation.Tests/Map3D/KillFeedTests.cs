using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Infrastructure;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ H2 / PH-5 (CE-4228) — the KILL FEED from synthetic <see cref="ManDownedEvent"/>s: killer to victim in side colours, <i>down</i> / <i>killed</i>, the head mark,
/// the rethrown grenade naming its first thrower and the rethrower, "You" for the viewer, the last five, expiry, and a replay seeked back hiding the future.
/// </summary>
public sealed class KillFeedTests
{
    private static (EntityRepository W, Entity Fox, Entity Bobr, Entity Hawk) World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<NetworkIdentity>();
        w.RegisterComponent<EntityInfo>();
        w.RegisterComponent<SimTransform>();
        Entity Man(int net, string name, ForceId side, float x)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity(net));
            w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
            w.AddComponent(e, new SimTransform { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity });
            return e;
        }
        return (w, Man(1, "Fox", ForceId.Hostile, 0), Man(2, "Bobr", ForceId.Friend, 212), Man(3, "Hawk", ForceId.Friend, 5));
    }

    private static ManDownedEvent Down(Entity victim, Entity source, Entity original = default, InjuryState state = InjuryState.Dead, bool head = false, ForceId side = ForceId.Friend)
        => new() { Victim = victim, Source = source, OriginalSource = original, State = state, HasBodyPart = (byte)(head ? 1 : 0), BodyPart = head ? PersonPart.Head : PersonPart.Torso, VictimSide = side };

    [Fact]
    public void ADowning_ReadsKillerArrowVictim_WithSideColoursAndTheWordsDownOrKilled()
    {
        var (w, fox, bobr, _) = World();
        using (w)
        {
            var feed = new KillFeed();
            feed.Record(w, Down(bobr, fox, state: InjuryState.Incapacitated), 10);
            feed.Record(w, Down(bobr, fox, state: InjuryState.Dead), 11);
            var down = feed.All[0]; var dead = feed.All[1];
            Assert.Equal("Fox » Bobr - down · 212 m", KillFeed.Text(down, viewerUnitNet: 0));
            Assert.Equal("Fox » Bobr - killed · 212 m", KillFeed.Text(dead, 0));
            var parts = KillFeed.Line(dead, 0);
            Assert.Equal(ForceId.Hostile, parts[0].Side);   // the killer in his side's colour
            Assert.Equal(ForceId.Friend, parts[2].Side);    // the victim in his
        }
    }

    [Fact]
    public void AHeadshot_CarriesTheHeadMark_AndOnlyAHeadshotDoes()
    {
        var (w, fox, bobr, _) = World();
        using (w)
        {
            var feed = new KillFeed();
            feed.Record(w, Down(bobr, fox, head: true), 1);
            feed.Record(w, Down(bobr, fox, head: false), 2);
            Assert.Contains("(head)", KillFeed.Text(feed.All[0], 0));
            Assert.DoesNotContain("(head)", KillFeed.Text(feed.All[1], 0));
        }
    }

    /// <summary>A rethrown grenade: the killer shown is the FIRST thrower (OriginalSource), the rethrower is named. Same Source and OriginalSource, or no original, is no rethrow.</summary>
    [Fact]
    public void ARethrownRound_NamesItsFirstThrower_AndWhoRethrewIt()
    {
        var (w, fox, bobr, hawk) = World();
        using (w)
        {
            var feed = new KillFeed();
            feed.Record(w, Down(bobr, source: hawk, original: fox), 1);
            feed.Record(w, Down(bobr, source: fox, original: fox), 2);
            feed.Record(w, Down(bobr, source: fox), 3);
            Assert.StartsWith("Fox » Bobr - killed (rethrown by Hawk)", KillFeed.Text(feed.All[0], 0));
            Assert.DoesNotContain("rethrown", KillFeed.Text(feed.All[1], 0));
            Assert.DoesNotContain("rethrown", KillFeed.Text(feed.All[2], 0));
        }
    }

    [Fact]
    public void TheViewer_ReadsAsYou_AndAnUnknownKillerAsAQuestionMark()
    {
        var (w, fox, bobr, _) = World();
        using (w)
        {
            var feed = new KillFeed();
            feed.Record(w, Down(bobr, fox), 1);
            feed.Record(w, Down(bobr, Entity.Null), 2);
            Assert.StartsWith("You » Bobr", KillFeed.Text(feed.All[0], viewerUnitNet: 1));
            Assert.StartsWith("Fox » You", KillFeed.Text(feed.All[0], viewerUnitNet: 2));
            Assert.StartsWith("? » Bobr", KillFeed.Text(feed.All[1], 0));
        }
    }

    [Fact]
    public void TheFeed_ShowsTheLastFive_NewestFirst_ExpiresThem_AndHidesTheFutureOfAReplay()
    {
        var (w, fox, bobr, _) = World();
        using (w)
        {
            var feed = new KillFeed();
            for (int i = 0; i < 8; i++) feed.Record(w, Down(bobr, fox), 10 + i);
            Assert.Equal(new double[] { 17, 16, 15, 14, 13 }, feed.Recent(17).Select(e => e.At));
            Assert.Equal(new double[] { 17, 16, 15 }, feed.Recent(17 + KillFeed.ShowSeconds - 2.5).Select(e => e.At));   // the older ones have expired
            Assert.Equal(new double[] { 12, 11, 10 }, feed.Recent(12).Select(e => e.At));                                   // a replay seeked back: the later ones are not yet
        }
    }

    [Fact]
    public void TheFeedOfAWorld_IsOneStore_RecordedByTheEffectSystemThatReadsTheBus()
    {
        var (w, fox, bobr, _) = World();
        using (w)
        {
            Assert.Null(KillFeed.Of(w));
            KillFeed.In(w).Record(w, Down(bobr, fox), 1);
            Assert.Same(KillFeed.Of(w), KillFeed.In(w));
            Assert.Single(KillFeed.Of(w)!.All);
        }
    }
}
