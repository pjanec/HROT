using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>⭐ CE-2137 (C1, Hrot docs/DESIGN_Orders_Params_Entity_Classes.md §2) — the DIS pattern: parse, match, specificity.</summary>
    public class DisTypeMaskTests
    {
        private static DISEntityType T(byte kind, byte domain, ushort country = 0, byte category = 0, byte sub = 0)
            => new() { Kind = kind, Domain = domain, Country = country, Category = category, Subcategory = sub };

        [Theory]
        [InlineData("*", "*")]
        [InlineData("1.1.*.1", "1.1.*.1")]
        [InlineData("1.1.*.1.*.*.*", "1.1.*.1")]                 // trailing * dropped
        [InlineData("1.2.*.25,20-24", "1.2.*.20-25")]             // sorted and merged
        [InlineData("1.2.*.1,2,6,7", "1.2.*.1-2,6-7")]            // adjacent numbers merge into ranges
        [InlineData("1.1.65535", "1.1.65535")]                    // Country is 16-bit
        [InlineData(" 3 . 1 ", "3.1")]
        public void CE2137_Parse_IsCanonical(string text, string canonical)
        {
            Assert.True(DisTypeMask.TryParse(text, out var m));
            Assert.Equal(canonical, m.ToString());
            Assert.Equal(DisTypeMask.Parse(canonical), m);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.1.1.1.1.1.1.1")]   // eight segments
        [InlineData("1.x")]
        [InlineData("1.256")]             // a byte segment out of range
        [InlineData("1.1.65536")]         // Country out of range
        [InlineData("1.5-3")]             // backwards range
        [InlineData("1.1,,2")]            // empty list item
        [InlineData("1..1")]              // empty segment
        [InlineData("1.1-2-3")]
        public void CE2137_Parse_RefusesBadText(string text)
        {
            Assert.False(DisTypeMask.TryParse(text, out _));
            var ex = Assert.Throws<System.FormatException>(() => DisTypeMask.Parse(text));
            Assert.Contains(text, ex.Message);
        }

        [Fact]
        public void CE2137_Matches_ByEverySegment()
        {
            var tanks = DisTypeMask.Parse("1.1.*.1");
            Assert.True(tanks.Matches(T(1, 1, 225, 1, 1)));       // M1
            Assert.True(tanks.Matches(T(1, 1, 222, 1, 2)));       // T-72
            Assert.False(tanks.Matches(T(1, 1, 225, 2)));         // an AFV
            Assert.False(tanks.Matches(T(1, 2, 225, 1)));         // an air platform, category 1

            var helis = DisTypeMask.Parse("1.2.*.20-25");
            Assert.True(helis.Matches(T(1, 2, 225, 20)));
            Assert.True(helis.Matches(T(1, 2, 225, 25)));
            Assert.False(helis.Matches(T(1, 2, 225, 26)));

            var list = DisTypeMask.Parse("1.1.*.3,6,7");
            Assert.True(list.Matches(T(1, 1, 0, 6)));
            Assert.False(list.Matches(T(1, 1, 0, 5)));

            Assert.True(DisTypeMask.Any.Matches(default));
            Assert.True(DisTypeMask.Parse("1.1.225").Matches(T(1, 1, 225, 9, 9)));
        }

        [Fact]
        public void CE2137_Specificity_SegmentBySegmentFromKind_ExactBeatsSetBeatsAny()
        {
            int S(string m) => DisTypeMask.Parse(m).Specificity;

            Assert.True(S("1.1.*.1") > S("1.1.*.1,2"));           // Tank beats Armor
            Assert.True(S("1.1.*.1,2") > S("1.1"));               // Armor beats LandPlatform
            Assert.True(S("1.1") > S("1"));
            Assert.True(S("1") > S("*"));
            Assert.True(S("1.1.225") > S("1.1.*.1"));             // an earlier segment decides first
            Assert.True(S("1.2.*.20") > S("1.2.*.20-25"));
            Assert.Equal(S("1.1.*.1,2"), S("1.1.*.3-7"));         // list and range rank alike

            var m = DisTypeMask.Parse("1.1.*.20-25");
            Assert.Equal(DisTypeMask.SegmentKind.Exact, m.KindOf(0));
            Assert.Equal(DisTypeMask.SegmentKind.Any, m.KindOf(2));
            Assert.Equal(DisTypeMask.SegmentKind.Set, m.KindOf(3));
            Assert.Equal(3, m.DeepestPinned);
            Assert.Equal(1, m.ExactValue(1));
            Assert.Null(m.ExactValue(3));
            Assert.Equal(-1, DisTypeMask.Any.DeepestPinned);
        }

        [Fact]
        public void CE2137_Overlaps_WhenSomeTypeMatchesBoth()
        {
            Assert.True(DisTypeMask.Parse("1.1.*.1,2").Overlaps(DisTypeMask.Parse("1.1.*.2-5")));
            Assert.True(DisTypeMask.Parse("1.1").Overlaps(DisTypeMask.Parse("1.*.225.1")));
            Assert.False(DisTypeMask.Parse("1.1.*.1,2").Overlaps(DisTypeMask.Parse("1.1.*.3-5")));
            Assert.False(DisTypeMask.Parse("1.1").Overlaps(DisTypeMask.Parse("1.2")));
        }
    }
}
