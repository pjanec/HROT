using System;
using System.Linq;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ CE-4219 ③ (<c>docs/DESIGN_Rules_View_UI.md</c> §7.5a) — THE predicate-mode table (the breakpoint builder and the Replay Browser's search panel each had a copy): every mode round-trips
    /// (blank predicate → its mode), every blank is a fresh instance of a distinct subtype, labels are unique. 🔴 Red-proof: <c>ModeOf</c> answering Component for a Lifecycle DTO ⇒ the round trip goes red.
    /// </summary>
    public sealed class PredicateModesTests
    {
        [Fact]
        public void CE4219_EveryMode_RoundTrips_ThroughItsBlankPredicate()
        {
            Assert.Equal(Enum.GetValues(typeof(SearchPredicateMode)).Length, PredicateModes.All.Count);
            var types = new System.Collections.Generic.HashSet<Type>();
            foreach (var mode in PredicateModes.All)
            {
                var dto = PredicateModes.CreateDefault(mode);
                Assert.Equal(mode, PredicateModes.ModeOf(dto));
                Assert.NotSame(dto, PredicateModes.CreateDefault(mode));   // a fresh instance each call
                Assert.True(types.Add(dto.GetType()), $"{mode}: its own subtype");
                Assert.False(string.IsNullOrWhiteSpace(PredicateModes.Label(mode)));
            }
            Assert.Equal(PredicateModes.All.Count, PredicateModes.All.Select(PredicateModes.Label).Distinct().Count());
        }
    }
}
