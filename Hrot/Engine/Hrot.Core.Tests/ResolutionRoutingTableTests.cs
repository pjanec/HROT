using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.Core.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-8000</c> (CE-4616 ST-0 ④; SV-2, B-1, B-8, RV-7) — the (class, resolution, order) table: physical is OrderResolver's rule
    /// untouched; a stochastic infantry squad's Defend resolves to the generic play, its SOP to <c>StochasticUnitSop</c>; a Strike is refused.
    /// </summary>
    public sealed class ResolutionRoutingTableTests
    {
        private static readonly DISEntityType Squad = new() { Kind = 11, Domain = 1, Country = 225, Category = 1, Subcategory = 2 };
        private static readonly HashSet<string> Built = new()
        {
            "InfantrySquadDefend", "InfantrySquadAttack",
            ResolutionRoutingTable.Plays.Move, ResolutionRoutingTable.Plays.Defend, ResolutionRoutingTable.Plays.Attack,
            ResolutionRoutingTable.Plays.BreakContact, ResolutionRoutingTable.Plays.Sop,
        };

        private static OrderResolver.Result R(AggregateResolution res, string order, Func<string, bool>? isB = null)
            => ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, Squad, res, order, isB ?? Built.Contains);

        [Fact]
        public void Physical_IsOrderResolversRule_Untouched()
        {
            Assert.Equal("InfantrySquadDefend", R(AggregateResolution.Physical, "Defend").Behaviour);
            Assert.Equal("InfantrySquadDefend", R(AggregateResolution.Unset, "Defend").Behaviour);
            var plain = OrderResolver.Resolve(EntityClassTable.Default, Squad, "Attack", Built.Contains);
            Assert.Equal(plain, R(AggregateResolution.Physical, "Attack"));
        }

        [Fact]
        public void Stochastic_ResolvesTheGenericPlays_TheSopIncluded()
        {
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, R(AggregateResolution.Stochastic, "Defend").Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, R(AggregateResolution.Stochastic, "Hold").Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.Move, R(AggregateResolution.Stochastic, "March").Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.Attack, R(AggregateResolution.Stochastic, "Bound").Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.Attack, R(AggregateResolution.Stochastic, "ClearBuilding").Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.BreakContact, R(AggregateResolution.Stochastic, "BreakContact").Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.Sop, R(AggregateResolution.Stochastic, ResolutionRoutingTable.SopOrder).Behaviour);
        }

        [Fact]
        public void Stochastic_RefusesWhatHasNoRow_AndNeverFallsBackToThePhysicalPlay()
        {
            foreach (var o in new[] { "Strike", "ObserveFire", "Recover" })
                Assert.Equal(OrderResolution.NoBehaviour, R(AggregateResolution.Stochastic, o).Outcome);
            // the physical InfantrySquadDefend exists, but a memberless unit must never run it (RV-7)
            Assert.Equal(OrderResolution.NoBehaviour, R(AggregateResolution.Stochastic, "Defend", n => n == "InfantrySquadDefend").Outcome);
        }

        [Fact]
        public void Fusion_WithNoRow_FallsBackToThePhysicalRule()
            => Assert.Equal("InfantrySquadAttack", R(AggregateResolution.Fusion, "Attack").Behaviour);

        [Fact]
        public void AMoreSpecificClassRow_Wins()
        {
            var t = new ResolutionRoutingTable(new[]
            {
                new ResolutionRoutingTable.Row("Unit", AggregateResolution.Stochastic, "Defend", "A"),
                new ResolutionRoutingTable.Row("InfantrySquad", AggregateResolution.Stochastic, "Defend", "B"),
            });
            Assert.Equal("B", t.Resolve(EntityClassTable.Default, Squad, AggregateResolution.Stochastic, "Defend", _ => true).Behaviour);
        }

        [Fact]
        public void APhysicalRow_OrADuplicate_IsADataError()
        {
            Assert.Throws<ArgumentException>(() => new ResolutionRoutingTable(new[] { new ResolutionRoutingTable.Row("Unit", AggregateResolution.Physical, "Move", "X") }));
            Assert.Throws<ArgumentException>(() => new ResolutionRoutingTable(new[]
            {
                new ResolutionRoutingTable.Row("Unit", AggregateResolution.Stochastic, "Move", "X"),
                new ResolutionRoutingTable.Row("Unit", AggregateResolution.Stochastic, "Move", "Y"),
            }));
        }
    }
}
