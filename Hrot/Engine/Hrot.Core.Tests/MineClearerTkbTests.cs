using System.Linq;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core;
using Hrot.Core.Tkb;
using Hrot.Map.Common;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> (minefields MS-T4, DESIGN_Minefields.md MF-9, §3a) — the MINE-CLEARING VEHICLE is in the real catalogue as TKB 116: tracked, unarmed, HEAVIER than the bulldozer, in the Add Entity picker, class
    /// <c>MineClearer</c> (DIS 1.1.*.9.2, beside the dozer's 1.1.*.9.1); the line charge's munition (6053) is in the catalogue too. 🔴 Red-proof: drop <c>DefineMineClearer</c> ⇒ the type is absent; mass below the dozer's ⇒ the order fails.
    /// </summary>
    public class MineClearerTkbTests
    {
        private static readonly TkbDatabase Db = HrotEnvironment.CreateTkb();

        [Fact]
        public void TheCatalogue_HasTheMineClearer_TrackedUnarmedHeavierThanTheDozer_ClassMineClearer()
        {
            var t = Db.GetByType(TkbEntityTypes.Engineer_MineClearer);
            Assert.NotNull(t);
            Assert.Equal(116L, TkbEntityTypes.Engineer_MineClearer);   // a permanent wire value (§3a)
            Assert.Equal(LocomotionClass.Tracked, t!.GetDescriptor<LocomotionDto>()!.Class);
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            Assert.True(suite == null || suite.Mounts.Count == 0, "an engineering vehicle carries no weapon");
            Assert.Equal("MineClearer", EntityClassTable.Default.MostSpecific(t.DisType)!.Name);
            Assert.Equal("Bulldozer", EntityClassTable.Default.MostSpecific(Db.GetByType(TkbEntityTypes.Engineer_Bulldozer)!.DisType)!.Name);   // the sibling class is untouched
            Assert.False(t.GetDescriptor<TkbMasterDto>()!.HideFromPalette, "listed in the Add Entity picker like the other vehicles");
            float clearer = t.GetDescriptor<VehicleParametersDto>()!.Mass, dozer = Db.GetByType(TkbEntityTypes.Engineer_Bulldozer)!.GetDescriptor<VehicleParametersDto>()!.Mass;
            Assert.True(clearer > dozer, $"heavier than the dozer ({clearer} vs {dozer} kg)");
            Assert.NotNull(Db.GetByType(Fdp.Toolkit.Terrain.Minefields.MunitionLineCharge));
        }
    }
}
