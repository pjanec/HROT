using System.Linq;
using Hrot.Core.Ambience;
using Xunit;

namespace Hrot.Core.Tests
{
    /// <summary>
    /// ⭐ <c>CE-11034</c> (R-11014, AM-1, AM-4; 📄 docs/designs/audio/DESIGN_Ambience.md §3) — THE ONE list of ambience kinds: every listed kind resolves to itself, an unknown child falls back to
    /// its parent (<c>alarm.airraid.preliminary</c> → <c>alarm.airraid</c> → <c>alarm</c>), a name with no known ancestor resolves to nothing and is FLAGGED (<c>Check</c>) — never silently accepted
    /// as authored — while run time allows it (the action logs it).
    /// </summary>
    public sealed class AmbienceKindsTests
    {
        /// <summary>The design's §3 table, verbatim: a kind missing from <see cref="AmbienceKinds.All"/> (or an extra one) fails. 🔴 Mutation: drop <c>alarm.bell</c> from the list ⇒ red.</summary>
        [Fact]
        public void TheList_IsTheDesignsTable_EveryKindResolvesToItself()
        {
            string[] design =
            {
                "battle", "battle.distant", "battle.city", "battle.night", "battle.heavy", "battle.sniper", "battle.ambush", "battle.aftermath",
                "event", "event.airstrike", "event.drone", "event.rocket", "event.convoy", "event.evacuation", "event.evacuation.city", "event.nuclear",
                "alarm", "alarm.airraid", "alarm.airraid.preliminary", "alarm.bell", "alarm.intruder", "alarm.emergency",
                "naval", "naval.ship", "naval.warship", "naval.underwater",
            };
            Assert.Equal(design.OrderBy(x => x, System.StringComparer.Ordinal), AmbienceKinds.All.OrderBy(x => x, System.StringComparer.Ordinal));
            Assert.Equal(AmbienceKinds.All.Count, AmbienceKinds.All.Distinct().Count());
            foreach (var kind in AmbienceKinds.All)
            {
                Assert.True(AmbienceKinds.IsKnown(kind), kind);
                Assert.Equal(kind, AmbienceKinds.Resolve(kind));
                Assert.Null(AmbienceKinds.Check(kind));
                Assert.True(kind.Length <= AmbienceKinds.MaxLength, kind);   // fits a layer's FixedString32
            }
        }

        /// <summary>Every kind's parent is a kind too (the chain ends at a family), and the families have none.</summary>
        [Fact]
        public void EveryKindsChain_EndsAtAFamily()
        {
            foreach (var kind in AmbienceKinds.All)
            {
                var k = kind; int steps = 0;
                while (AmbienceKinds.Parent(k) is { } parent) { Assert.True(AmbienceKinds.IsKnown(parent), $"{kind} → {parent}"); k = parent; Assert.True(++steps < 5, kind); }
                Assert.Contains(k, AmbienceKinds.Families);
            }
            foreach (var family in AmbienceKinds.Families) Assert.Null(AmbienceKinds.Parent(family));
        }

        /// <summary>The fallback of the design's example, step by step, and a child nobody listed. 🔴 Mutation: <c>Resolve</c> not walking the parents ⇒ the child resolves to null ⇒ red.</summary>
        [Theory]
        [InlineData("alarm.airraid.preliminary", "alarm.airraid.preliminary")]   // listed: itself
        [InlineData("alarm.airraid.preliminary.v2", "alarm.airraid.preliminary")]   // an unknown child of a listed kind: its parent
        [InlineData("alarm.airraid.zzz", "alarm.airraid")]
        [InlineData("alarm.foghorn", "alarm")]                                    // a new kind of a known family: the family's generic sound
        [InlineData("alarm.foghorn.low", "alarm")]
        [InlineData("battle.city.night", "battle.city")]
        [InlineData("event.evacuation.city.east", "event.evacuation.city")]
        public void AnUnknownChild_FallsBackToItsNearestKnownParent(string kind, string expected)
            => Assert.Equal(expected, AmbienceKinds.Resolve(kind));

        /// <summary>A totally unknown kind resolves to nothing, is FLAGGED by <c>Check</c> (the editor's picker and an authoring check say why), and is not "known". Empty and over-long names too.</summary>
        [Theory]
        [InlineData("weather.rain")]
        [InlineData("zzz")]
        [InlineData("zzz.alarm")]                                                  // a listed word is not a listed kind unless it LEADS
        [InlineData("Alarm.Bell")]                                                 // kinds are exact (lower case)
        public void ATotallyUnknownKind_ResolvesToNothing_AndIsFlagged(string kind)
        {
            Assert.Null(AmbienceKinds.Resolve(kind));
            Assert.False(AmbienceKinds.IsKnown(kind));
            Assert.Contains("not an ambience kind", AmbienceKinds.Check(kind));
        }

        [Fact]
        public void EmptyAndTooLongKinds_AreFlagged()
        {
            Assert.NotNull(AmbienceKinds.Check(""));
            Assert.NotNull(AmbienceKinds.Check("  "));
            Assert.NotNull(AmbienceKinds.Check(null));
            Assert.Contains("at most 31", AmbienceKinds.Check("alarm." + new string('x', 26)));
            Assert.Null(AmbienceKinds.Parent(""));
            Assert.Null(AmbienceKinds.Parent(".x"));
            Assert.Null(AmbienceKinds.Resolve(null));
        }
    }
}
