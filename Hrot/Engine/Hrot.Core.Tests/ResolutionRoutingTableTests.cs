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
        public void AnUnsetRow_OrADuplicate_IsADataError()
        {
            // ⭐ X2-C2 (R-8356) — a PHYSICAL row is legal now (the fallback); a row with no resolution is not. ⛔ SUPERSEDED: "a physical row is a data error"
            Assert.Throws<ArgumentException>(() => new ResolutionRoutingTable(new[] { new ResolutionRoutingTable.Row("Unit", AggregateResolution.Unset, "Move", "X") }));
            _ = new ResolutionRoutingTable(new[] { new ResolutionRoutingTable.Row("Unit", AggregateResolution.Physical, "Move", "X") });
            Assert.Throws<ArgumentException>(() => new ResolutionRoutingTable(new[]
            {
                new ResolutionRoutingTable.Row("Unit", AggregateResolution.Stochastic, "Move", "X"),
                new ResolutionRoutingTable.Row("Unit", AggregateResolution.Stochastic, "Move", "Y"),
            }));
        }

        /// <summary>⭐⭐ <c>X-2</c> X2-C2 (R-8356, R-8302; F-X2-2) — at Physical a squad's Defend / BreakContact, which the name rule cannot resolve (no
        /// <c>InfantrySquadDefend</c> in production), resolve through the PHYSICAL FALLBACK rows to <c>SquadDefend</c> / <c>BreakContact</c>; where the
        /// name rule DOES resolve, it still wins. ✅ Red-proofs: the fallback branch dropped ⇒ Defend refused ⇒ red · the fallback consulted FIRST ⇒
        /// <c>SquadDefend</c> beats <c>InfantrySquadDefend</c> ⇒ red.</summary>
        [Fact]
        public void X2C2_APhysicalRow_IsAFallback_TheNameRuleStillWins()
        {
            var production = new HashSet<string> { "SquadDefend", "BreakContact", "InfantrySquadMove", "InfantrySquadAttack", "InfantrySquadHold" };
            Assert.Equal("SquadDefend", R(AggregateResolution.Physical, "Defend", production.Contains).Behaviour);
            Assert.Equal("BreakContact", R(AggregateResolution.Physical, "BreakContact", production.Contains).Behaviour);
            Assert.Equal("SquadDefend", R(AggregateResolution.Unset, "Defend", production.Contains).Behaviour);
            Assert.Equal("InfantrySquadHold", R(AggregateResolution.Physical, "Hold", production.Contains).Behaviour);   // untouched
            production.Add("InfantrySquadDefend");
            Assert.Equal("InfantrySquadDefend", R(AggregateResolution.Physical, "Defend", production.Contains).Behaviour);   // the name rule wins
            Assert.Equal(OrderResolution.NoBehaviour, R(AggregateResolution.Physical, "Recover", production.Contains).Outcome);
            // the stochastic rows are not physical fallbacks, nor the reverse
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, R(AggregateResolution.Stochastic, "Defend", n => n is "SquadDefend" or ResolutionRoutingTable.Plays.Defend).Behaviour);
            Assert.Equal(OrderResolution.NoBehaviour, R(AggregateResolution.Physical, "Defend", n => n == ResolutionRoutingTable.Plays.Defend).Outcome);
        }

        /// <summary>⭐ <c>X-2</c> X2-E (R-8358) — the catalogue of the generic stochastic plays is complete: <c>StochasticUnitHold</c> (the SOP's hold, no
        /// order row) is one of them; a physical play is not. ✅ Red-proof: <c>Hold</c> left out of <c>IsStochasticPlay</c> ⇒ red.</summary>
        [Fact]
        public void X2E_IsStochasticPlay_KnowsEveryGenericPlay()
        {
            foreach (var p in ResolutionRoutingTable.StochasticPlays) Assert.True(ResolutionRoutingTable.IsStochasticPlay(p), p);
            Assert.True(ResolutionRoutingTable.IsStochasticPlay("StochasticUnitHold"));
            Assert.Equal(6, ResolutionRoutingTable.StochasticPlays.Count);
            foreach (var r in ResolutionRoutingTable.Default.Rows)
                Assert.Equal(r.Resolution == AggregateResolution.Stochastic, ResolutionRoutingTable.IsStochasticPlay(r.Behaviour));
            Assert.False(ResolutionRoutingTable.IsStochasticPlay("SquadDefend"));
            Assert.False(ResolutionRoutingTable.IsStochasticPlay(null));
        }

        /// <summary>⭐⭐ CE-8307 (R-8386, X2-I; DESIGN_Split_One_Level.md §6 Q-e) — a split PLATOON / COMPANY is a physical command unit: its Defend at
        /// Physical resolves to its own play (<c>PlatoonDefend</c> / <c>CompanyDefend</c>) through the new classes and fallback rows; at Stochastic it
        /// stays the generic play; a SQUAD's Defend is untouched. ✅ Red-proof: the platoon row dropped ⇒ NoBehaviour ⇒ red.</summary>
        [Fact]
        public void CE8307_APlatoonsAndACompanysDefend_AtPhysical_IsTheirOwnPlay()
        {
            var platoon = UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, CountryTable.UnitedStates);
            var company = UnitDis.Land(UnitArm.Infantry, UnitEchelon.Company, CountryTable.UnitedStates);
            Func<string, bool> built = n => n is "PlatoonDefend" or "CompanyDefend" or "SquadDefend" or ResolutionRoutingTable.Plays.Defend
                                                 or "PlatoonAttack" or "CompanyAttack" or "UnitRelayMove" or "UnitRelayBreakContact" or "InfantrySquadMove";
            Assert.Equal("PlatoonDefend", ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, platoon, AggregateResolution.Physical, "Defend", built).Behaviour);
            Assert.Equal("CompanyDefend", ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, company, AggregateResolution.Physical, "Defend", built).Behaviour);
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, platoon, AggregateResolution.Stochastic, "Defend", built).Behaviour);
            Assert.Equal("SquadDefend", R(AggregateResolution.Physical, "Defend", built).Behaviour);
            // ⭐ CE-8307 (R-8392, review F1) — Attack is the echelon's own play; Move / BreakContact the ONE relay; a squad's Move stays its name rule
            Assert.Equal("PlatoonAttack", ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, platoon, AggregateResolution.Physical, "Attack", built).Behaviour);
            Assert.Equal("CompanyAttack", ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, company, AggregateResolution.Physical, "Attack", built).Behaviour);
            Assert.Equal("UnitRelayMove", ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, platoon, AggregateResolution.Physical, "Move", built).Behaviour);
            Assert.Equal("UnitRelayBreakContact", ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, company, AggregateResolution.Physical, "BreakContact", built).Behaviour);
            Assert.Equal("InfantrySquadMove", R(AggregateResolution.Physical, "Move", built).Behaviour);
        }
    }
}
