using System.Linq;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4650</c> (NA-1, NA-2; docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §9) — the NATIVE AMERICAN WARRIOR (type 2016) of the built-in
    /// catalogue: the Insurgent template with a bow and a thrown tomahawk (no melee mount exists to give him a club), no armour, a silent kit, and
    /// the buckskin wardrobe. ✅ Red-proof: the tomahawk mount dropped from <c>FunTkbCatalog</c> ⇒ the mounts test red.
    /// </summary>
    public class NativeWarriorTests
    {
        private static TkbDatabase BuiltIn()
        {
            var db = new TkbDatabase();
            HrotEnvironment.RegisterBuiltInTkb(db);
            return db;
        }

        [Fact]
        public void CE4650_TheWarrior_CarriesABowAndAThrownTomahawk_AndWearsNoArmour()
        {
            var t = BuiltIn().GetByType(FunTkbCatalog.TkbNativeWarrior);
            Assert.Equal("Native American warrior", t.Name);
            var mounts = t.GetDescriptor<WeaponSuiteDto>()!.Mounts;
            Assert.True(mounts.Count >= 2);
            Assert.Contains(mounts, m => m.Category == WeaponCategory.Bow);
            Assert.Contains(mounts, m => m.AmmoGuid == (ulong)MunitionTkbCatalog.TkbThrowingHatchet);
            var zones = t.GetDescriptor<BodyZonesDto>()!.Zones;
            Assert.All(zones.Where(z => z.Zone == "Head"), z => Assert.Equal(0f, z.ArmourSide));
            Assert.All(zones.Where(z => z.Zone == "Torso"), z => Assert.Equal(0f, z.ArmourFront));
            Assert.NotNull(t.GetDescriptor<TemperamentDto>());
        }

        [Fact]
        public void CE4650_TheWarriorsKit_IsSilent_NoGunshotIsHeard()
        {
            var t = BuiltIn().GetByType(FunTkbCatalog.TkbNativeWarrior);
            var ac = t.GetDescriptor<SignaturesDto>()!.Acoustic!;
            Assert.Equal(0f, ac.FiringAudibleRange);
            Assert.True(ac.MovingAudibleRange > 0f);
        }

        [Fact]
        public void CE4650_TheWardrobe_IsBuckskin_WithHeadbandsFeathersAndBraids()
        {
            var o = BuiltIn().GetByType(FunTkbCatalog.TkbNativeWarrior).GetDescriptor<OutfitDto>()!;
                        Assert.Contains(o.Top.Options, x => x.Kind == nameof(OutfitTop.Buckskin));
            Assert.Contains(o.Over.Options, x => x.Kind == nameof(OutfitOver.Breechcloth));
            Assert.Contains(o.Head.Options, x => x.Kind == nameof(OutfitHead.Headband));
            Assert.Contains(o.Head.Options, x => x.Kind == nameof(OutfitHead.Feather));
            Assert.Contains(o.Hair.Options, x => x.Kind == nameof(OutfitHair.Braids));
            Assert.Contains(o.FacePaint.Options, x => x.Kind == nameof(OutfitFacePaint.Stripes));
        }
    }
}
