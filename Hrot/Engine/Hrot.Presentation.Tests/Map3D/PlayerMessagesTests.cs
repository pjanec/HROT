using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Messages;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1461 W-M7 (docs/DESIGN_Node_Features.md §10 M-1; SR-106) — the player's message model: who a message is for, how long a subtitle lives and stacks,
/// the log's order and cap, the dedupe, the wrap and the fade.
/// Red-proof (compiles): make <c>Addressed</c> return true for <c>"Instructor"</c> ⇒ the filter rail fails.
/// </summary>
public sealed class PlayerMessagesTests
{
    private static readonly MessageViewer Me = new(UnitNetId: 42, Side: ForceId.Friend, StationId: (StationId)7);

    private static PlayerMessageEvent M(int id, string kind = "All", int value = 0, string text = "hello", float seconds = 6f, byte priority = 0, double at = 10, int origin = 0)
        => new() { MessageId = id, OriginNodeId = (Fdp.Core.NodeId)origin, ToKind = kind, ToValue = value, Text = text, Seconds = seconds, Priority = priority, SentAt = (SimInstant)at };

    [Fact]
    public void TheFilter_AllAlways_UnitBySideAndStation_ByTheViewer_InstructorNever()
    {
        var p = new PlayerMessages();
        p.Record(M(1, "All"));
        p.Record(M(2, "Unit", 42));
        p.Record(M(3, "Unit", 43));                          // another unit
        p.Record(M(4, "Side", (int)ForceId.Friend));
        p.Record(M(5, "Side", (int)ForceId.Hostile));        // the other side
        p.Record(M(6, "Station", 7));
        p.Record(M(7, "Station", 8));                        // another station
        p.Record(M(8, "Instructor"));
        p.Record(M(9, "Bogus", 42));                         // an unknown kind matches nothing
        Assert.Equal(new[] { 1, 2, 4, 6 }, p.Log(Me).Select(e => e.MessageId).OrderBy(x => x));
        Assert.Equal(new[] { 1 }, p.Log(default).Select(e => e.MessageId));   // no unit, no station: only All (no side message is addressed to a nobody)
    }

    /// <summary>
    /// ⭐ CE-11027 S12-4 (Q114 I) — THE addressing rule (<c>Hrot.Common.Messages.PlayerMessageAddressing</c>, below Presentation so the gateway asks it too): every address kind against a viewer on station index 0 and one on
    /// index 2 of the SAME node — a Station address is the whole station id, so seat 0 and seat 2 each get only their own. 🔴 Mutation: <c>Station</c> compared on the node alone ⇒ seat 2 also gets seat 0's message ⇒ red.
    /// </summary>
    [Fact]
    public void CE11027_TheAddressingRule_EveryKind_AgainstASeatAtIndex0AndOneAtIndex2()
    {
        var node = (NodeId)7;
        var seat0 = new MessageViewer(42, ForceId.Friend, StationId.Of(node));
        var seat2 = new MessageViewer(42, ForceId.Friend, StationId.Of(node, 2));
        int a0 = StationId.Of(node).Value, a2 = StationId.Of(node, 2).Value, other = StationId.Of((NodeId)8, 2).Value;
        Assert.NotEqual(a0, a2);
        foreach (var v in new[] { seat0, seat2 })
        {
            Assert.True(PlayerMessageAddressing.Addressed("All", 0, v));
            Assert.True(PlayerMessageAddressing.Addressed("Unit", 42, v));      Assert.False(PlayerMessageAddressing.Addressed("Unit", 43, v));
            Assert.True(PlayerMessageAddressing.Addressed("Side", (int)ForceId.Friend, v)); Assert.False(PlayerMessageAddressing.Addressed("Side", (int)ForceId.Hostile, v));
            Assert.False(PlayerMessageAddressing.Addressed("Station", other, v));
            Assert.False(PlayerMessageAddressing.Addressed("Instructor", 0, v)); Assert.False(PlayerMessageAddressing.Addressed("Bogus", 42, v));
        }
        Assert.True(PlayerMessageAddressing.Addressed("Station", a0, seat0));  Assert.False(PlayerMessageAddressing.Addressed("Station", a2, seat0));
        Assert.True(PlayerMessageAddressing.Addressed("Station", a2, seat2));  Assert.False(PlayerMessageAddressing.Addressed("Station", a0, seat2));
        Assert.False(PlayerMessageAddressing.Addressed("Station", 0, default));   // the unknown station matches nothing, even for 0
        Assert.False(PlayerMessageAddressing.Addressed("Unit", 0, default));

        // through the map's log (the same rule): seat 2 reads only the message to seat 2
        var p = new PlayerMessages();
        p.Record(M(1, "Station", a0)); p.Record(M(2, "Station", a2));
        Assert.Equal(new[] { 1 }, p.Log(seat0).Select(e => e.MessageId));
        Assert.Equal(new[] { 2 }, p.Log(seat2).Select(e => e.MessageId));
    }

    /// <summary>
    /// ⭐ CE-11027 S12-4 (Q114 I) — ONE rule: the map's <c>PlayerMessages</c> carries no rule of its own, it asks <c>PlayerMessageAddressing</c>. 🔴 Mutation: a private copy of the switch put back into
    /// <c>PlayerMessages.cs</c> (or its call site pointed at it) ⇒ red.
    /// </summary>
    [Fact]
    public void CE11027_TheMapHasNoPrivateCopyOfTheAddressRule()
    {
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(Fdp.Testing.TestRepo.Root, "Hrot", "Engine", "Hrot.Presentation", "Map3D", "PlayerMessages.cs"));
        Assert.Contains("PlayerMessageAddressing.Addressed(", src);
        Assert.DoesNotContain("\"Instructor\"", src.Replace("Instructor-addressed", ""));   // the rule's own words stay in Hrot.Common
        Assert.DoesNotContain("kind switch", src);
        Assert.DoesNotContain("static bool Addressed", src);
    }

    [Fact]
    public void ASubtitle_LivesFromSentAtForSeconds_AtMostTwo_HigherPriorityFirstThenNewer()
    {
        var p = new PlayerMessages();
        p.Record(M(1, text: "old", at: 10, seconds: 6));
        p.Record(M(2, text: "newer", at: 12, seconds: 6));
        p.Record(M(3, text: "urgent", at: 11, seconds: 6, priority: 5));
        Assert.Equal(new[] { "urgent", "newer" }, p.Active(13, Me).Select(e => e.Text));   // two of three, the priority one first
        Assert.Equal(new[] { "urgent", "newer" }, p.Active(16.5, Me).Select(e => e.Text));  // "old" ended at 16
        Assert.Equal(new[] { "newer" }, p.Active(17.5, Me).Select(e => e.Text));            // "urgent" ended at 17
        Assert.Empty(p.Active(9.9, Me));                                                    // not sent yet
        Assert.Empty(p.Active(30, Me));                                                     // all over
        Assert.Equal(3, p.Log(Me).Count);                                                   // ... but the log keeps them
    }

    [Fact]
    public void TheLog_IsNewestFirst_AndCappedAtOneHundred()
    {
        var p = new PlayerMessages();
        for (int i = 1; i <= 130; i++) p.Record(M(i, text: "m" + i, at: i));
        var log = p.Log(Me);
        Assert.Equal(PlayerMessages.LogCap, log.Count);
        Assert.Equal("m130", log[0].Text);
        Assert.Equal("m31", log[^1].Text);
    }

    [Fact]
    public void ARepeat_IsDroppedByMessageIdAndOrigin_ButAnotherOriginIsKept()
    {
        var p = new PlayerMessages();
        Assert.True(p.Record(M(1, origin: 0)));
        Assert.False(p.Record(M(1, origin: 0)));
        Assert.True(p.Record(M(1, origin: 3)));    // the same id from another node is another message
        Assert.False(p.Record(M(2, text: "  ")));   // nothing to say
        Assert.Equal(2, p.Log(Me).Count);
    }

    [Fact]
    public void TheSubtitleFades_OverItsLastHalfSecond_AndWrapKeepsEveryWord()
    {
        var e = new PlayerMessages.Entry(1, 0, "All", 0, "x", 6f, 0, 10);
        Assert.Equal(1f, PlayerMessages.Alpha(e, 12));
        Assert.Equal(0.5f, PlayerMessages.Alpha(e, 15.75), 3);
        Assert.Equal(0f, PlayerMessages.Alpha(e, 16.1));
        var lines = PlayerMessages.Wrap("one two three four five", t => t.Length * 10f, maxWidth: 100f);
        Assert.Equal(new[] { "one two", "three four", "five" }, lines);
        Assert.Equal("one two three four five", string.Join(" ", lines));
        Assert.Equal("0:07", PlayerMessages.Clock(7.4));
        Assert.Equal("2:05", PlayerMessages.Clock(125));
    }
}
