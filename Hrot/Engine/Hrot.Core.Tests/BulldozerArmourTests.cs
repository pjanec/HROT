using System.Linq;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core;
using Hrot.Core.Tkb;
using Hrot.Map.Common;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-6006</c> (showcase world, DESIGN_City_Battle_Showcase §6 / D-6; 🔒 user: <i>"it should stand some shots as armored one would"</i>) — the
    /// bulldozer's ARMOUR, asserted on the REAL catalogue's numbers through the ONE armour law (<see cref="ArmorModel"/>, R-212): every small-arms round
    /// the catalogue carries does nothing to it on any face, an RPG hit does its stated damage, and exactly the THIRD rocket kills it. The blast of a
    /// rocket's burst on the hull is bounded too (it must not move the "2 survive, 3 kill" arithmetic).
    /// 🔴 Red-proof: ArmorSide 40 → 4 (a rifle round's 5 mm pen then goes through) ⇒ the rifle and the HMG rails fail; MaxHealth 1000 → 700 ⇒ the
    /// second rocket kills.
    /// </summary>
    public class BulldozerArmourTests
    {
        private static readonly Fdp.Toolkit.Tkb.TkbDatabase Db = HrotEnvironment.CreateTkb();

        private static CombatPlatformDefDto Dozer() => Db.GetByType(TkbEntityTypes.Engineer_Bulldozer)!.GetDescriptor<CombatPlatformDefDto>()!;

        private static WeaponMountDto MountOf(long type, int index = 0) => Db.GetByType(type)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[index];

        /// <summary>What ONE hit of <paramref name="mount"/>'s round does to the bulldozer's <paramref name="facing"/> (the direct hit, as the sim applies it).</summary>
        private static float HitOn(WeaponMountDto mount, Facing facing)
        {
            var (pen, dmg) = CombatTkb.RoundOf(Db, mount, 0);
            var penetrator = CombatTkb.PenetratorOf(Db, unchecked((long)mount.AmmoGuid));
            return ArmorModel.HitDamage(pen, dmg, ArmorModel.ArmourFor(Dozer(), facing, penetrator));
        }

        private static readonly Facing[] Faces = { Facing.Front, Facing.Side, Facing.Rear, Facing.Top };

        [Fact]
        public void TheCatalogue_HasTheBulldozer_AsATrackedUnarmedEngineeringVehicle_InThePickerWithAnEngineerClass()
        {
            var t = Db.GetByType(TkbEntityTypes.Engineer_Bulldozer);
            Assert.NotNull(t);
            Assert.Equal(112L, TkbEntityTypes.Engineer_Bulldozer);   // a permanent wire value
            Assert.Equal(LocomotionClass.Tracked, t!.GetDescriptor<LocomotionDto>()!.Class);
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            Assert.True(suite == null || suite.Mounts.Count == 0, "an engineering vehicle carries no weapon");
            Assert.Equal("Bulldozer", EntityClassTable.Default.MostSpecific(t.DisType)!.Name);
            Assert.False(t.GetDescriptor<TkbMasterDto>()!.HideFromPalette, "listed in the Add Entity picker like the other vehicles");
        }

        [Theory]
        [InlineData(TkbEntityTypes.Infantry_Rifleman, "rifle 5.56")]
        [InlineData(TkbEntityTypes.Infantry_AutomaticRifleman, "squad automatic weapon")]
        [InlineData(TkbEntityTypes.Infantry_MachineGunnerPkm, "PKM 7.62")]
        [InlineData(TkbEntityTypes.Infantry_MarksmanSvd, "SVD marksman rifle")]
        [InlineData(TkbEntityTypes.Infantry_Sniper, "sniper rifle")]
        [InlineData(TkbEntityTypes.WeaponSystem_M2Hmg, "M2 12.7 mm HMG")]
        [InlineData(TkbEntityTypes.WeaponSystem_DshkHmg, "DShK 12.7 mm HMG")]
        public void SmallArms_AndTheHeavyMachineGuns_DoNothing_OnAnyFace_SoAnyNumberOfHitsLeavesItAlive(long shooterType, string what)
        {
            var mount = MountOf(shooterType);
            foreach (var face in Faces)
                Assert.True(HitOn(mount, face) == 0f, $"{what} on the {face}: a hit did {HitOn(mount, face)}");
            // 200 hits (a squad's whole magazines) — still the full health
            float health = Dozer().MaxHealth;
            for (int i = 0; i < 200; i++) health -= HitOn(mount, Facing.Side);
            Assert.Equal(Dozer().MaxHealth, health);
        }

        [Fact]
        public void TheRpgRocket_DoesItsFullStatedDamage_OnEveryFace_AndTheThirdHitKills_TheFirstTwoDoNot()
        {
            var rpg = MountOf(UrbanCombatTkbCatalog.TkbInsurgent, 0);   // the RPG man's launcher
            Assert.Equal(MunitionTkbCatalog.TkbRpg7Rocket, unchecked((long)rpg.AmmoGuid));
            foreach (var face in Faces)
                Assert.Equal(rpg.DamagePerHit, HitOn(rpg, face));

            float perRocket = rpg.DamagePerHit + WorstBlastOnTheHull();   // the direct hit + the burst's blast at the hull's skin
            float health = Dozer().MaxHealth;
            health -= perRocket; Assert.True(health > 0f, $"after ONE rocket: {health}");
            health -= perRocket; Assert.True(health > 0f, $"after TWO rockets: {health} (a rocket costs {perRocket})");
            health -= perRocket; Assert.True(health <= 0f, $"after THREE rockets: {health}");
            // and the direct hits alone already settle it: 2 × damage leave it standing, 3 × damage kill it
            Assert.True(Dozer().MaxHealth - 2 * rpg.DamagePerHit > 0f);
            Assert.True(Dozer().MaxHealth - 3 * rpg.DamagePerHit <= 0f);
        }

        /// <summary>The rocket's blast on the hull at the burst point (distance 0) — an UPPER bound: no barrier, no shadow, full falloff.</summary>
        private static float WorstBlastOnTheHull()
        {
            var w = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.WarheadOnly(Db, MunitionTkbCatalog.TkbRpg7Rocket)!;
            float susceptibility = ArmorModel.BlastSusceptibility(Dozer(), 4f);
            return w.BlastLethalDamage * AreaEffect.BlastFalloff(0f, w.BlastLethalRadiusM, w.BlastInjuryRadiusM) * susceptibility;
        }

        [Fact]
        public void TheRpgBlast_OnTheHull_IsASmallShareOfARocket_SoItCannotMoveTheThreeRocketArithmetic()
        {
            Assert.True(WorstBlastOnTheHull() < 60f, $"blast per rocket on the dozer: {WorstBlastOnTheHull()}");   // 2 × (400 + 59) < 1000 ≤ 3 × 400
        }
    }
}
