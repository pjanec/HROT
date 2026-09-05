using CycloneDDS.Runtime;
using Fdp.Modules.Geographic.Transforms;
using Hrot.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;

namespace Hrot.Map.Common
{
    /// <summary>
    /// Shared stateless factory for common Hrot runtime primitives.
    /// </summary>
    public static class HrotEnvironment
    {
        private const double BerlinLatitudeDeg = 52.52;
        private const double BerlinLongitudeDeg = 13.405;
        private const double BerlinAltitudeMeters = 0.0;

        /// <summary>
        /// Builds the process's TKB database with the catalogue CONTENTS every host must share.
        ///
        /// <para>⭐ <b>Identical contents on every host is the point.</b> There are four independent
        /// <c>CreateTkb()</c> call sites; before 2026-08-31 only the Editor and the Stride editor also
        /// seeded the UrbanCombat templates (types 1001-2003), because they were the only two production
        /// projects referencing <c>Fdp.Examples.Scenarios</c> — so a scenario referencing 1001 resolved in
        /// the Editor and failed on SimHost or CGF. 🔒 User ruling 2026-08-30: "if editor builds UrbanCombat
        /// stuff then everyone should, editor is the most advanced in that matter."</para>
        ///
        /// <para>⛔ <b>A host that calls this must NOT also call</b>
        /// <c>UrbanCombatTkbCatalog.RegisterAll</c> or <c>UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates</c>
        /// — <c>TkbDatabase.Register</c> THROWS on a duplicate name or type. Two production call sites were
        /// removed for exactly this reason. 📄 docs/DESIGN_Entity_Creation_Unification.md §3.3.</para>
        /// </summary>
        public static TkbDatabase CreateTkb()
        {
            var tkb = new TkbDatabase();
            NedTkbCatalog.RegisterAll(tkb);
            // ⭐ 2026-08-31: the UrbanCombat development templates, so all four CreateTkb() sites produce
            //    identical CONTENTS. ⚠ Development default only — the real system loads TKB from files
            //    synced to all nodes (user, 2026-08-31).
            Hrot.Core.Tkb.UrbanCombatTkbCatalog.RegisterAll(tkb);
            RouteTkbExtensions.ApplyRoutePlanToBlueprint(tkb);
            return tkb;
        }

        public static WGS84Transform CreateGeoTransform()
        {
            var transform = new WGS84Transform();
            transform.SetOrigin(BerlinLatitudeDeg, BerlinLongitudeDeg, BerlinAltitudeMeters);
            return transform;
        }

        public static DdsParticipant CreateParticipant(int domainId)
        {
            return new DdsParticipant((uint)domainId);
        }
    }
}