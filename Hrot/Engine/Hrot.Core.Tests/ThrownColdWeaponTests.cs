using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3247</c> (civilians B-17, TC-1…TC-5, R-3016; docs/DESIGN_Civilians_And_Crowd_Control.md §7.17b) — the built-in THROWN BLADES:
    /// a hatchet, a spear and a throwing star are KINETIC rounds on the stone's arc (a direct hit, no burst), lethal, stopped by body armour,
    /// lying where they land so a hand picks them up again, drawn as blades. ⛔ No throwing knife (R-3016: a knife is melee).
    /// </summary>
    public class ThrownColdWeaponTests
    {
        private static TkbDatabase BuiltIn()
        {
            var db = new TkbDatabase();
            HrotEnvironment.RegisterBuiltInTkb(db);
            return db;
        }

        [Theory]
        [InlineData(MunitionTkbCatalog.TkbThrowingHatchet, MunitionTkbCatalog.ThrowingHatchetDamage, MunitionShape.Axe)]
        [InlineData(MunitionTkbCatalog.TkbThrowingSpear, MunitionTkbCatalog.ThrowingSpearDamage, MunitionShape.Spear)]
        [InlineData(MunitionTkbCatalog.TkbThrowingStar, MunitionTkbCatalog.ThrowingStarDamage, MunitionShape.Star)]
        public void CE3247_EachThrownBlade_IsAKineticRoundOnTheArc_ThatLiesAndIsSeenAsABlade(long ammo, float damage, MunitionShape shape)
        {
            var db = BuiltIn();
            var t = db.GetByType(ammo)!;
            var warhead = ParameterResolver.Warhead(db, ammo).Warhead!;
            Assert.Equal(WarheadKind.Kinetic, warhead.Kind);
            Assert.True(warhead.Indirect, "thrown on the arc");
            var hand = t.GetDescriptor<HandItemDto>();
            Assert.NotNull(hand);
            byte bits = WarheadRound.BitsOf(warhead, hand, out float lies);
            Assert.True((bits & WarheadRound.Arc) != 0 && (bits & WarheadRound.Lingers) != 0, "it arcs and it lies where it lands");
            Assert.Equal(0, bits & WarheadRound.Area);   // a direct hit only
            Assert.Equal(MunitionTkbCatalog.ThrownBladeLiesSeconds, lies);
            var round = t.GetDescriptor<AmmoWeaponBallisticsDto>()!;
            Assert.Equal(damage, round.Damage);
            Assert.True(round.PenetrationMm <= MunitionTkbCatalog.Pistol9mmPenetrationMm, "body armour stops it (as it stops the 9 mm)");
            Assert.Equal(shape, t.GetDescriptor<MunitionVisualDto>()!.Shape);

            using var world = new EntityRepository();
            world.SetSingletonManaged<ITkbDatabase>(db);
            Assert.True(ItemAppearances.IsLethal(world, ammo), "a blade in a man's hand is lethal");
            Assert.False(ItemAppearances.IsLethal(world, MunitionTkbCatalog.TkbStone));   // the stone stays non-lethal
        }

        /// <summary>⛔ R-3016 (user, 2026-10-18): <i>"Knife is usually not thrown unless by a circus artist"</i> — no built-in munition is a knife.</summary>
        [Fact]
        public void CE3247_NoBuiltInMunition_IsAThrowingKnife()
        {
            var db = BuiltIn();
            Assert.DoesNotContain(db.GetAll(), t => t.GetDescriptor<WarheadDto>() != null && t.Name.Contains("knife", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
