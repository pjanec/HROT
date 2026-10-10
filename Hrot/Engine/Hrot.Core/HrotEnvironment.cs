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
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(tkb);   // ⭐ CE-1032 — the munition types mounts load (warheads by name)
            Hrot.Core.Tkb.EffectTkbCatalog.RegisterAll(tkb);     // ⭐ CE-1042 E1 — the realism effect types + the munitions' effect sets
            RouteTkbExtensions.ApplyRoutePlanToBlueprint(tkb);
            return tkb;
        }

        /// <summary>
        /// A node's geo transform, at origin 0,0,0 until a terrain commit sets the terrain's own origin
        /// (<c>TerrainResidency.ApplyGeoOrigin</c>). ⛔ <c>CE-3126</c> (R-229) — this used to set a hard-coded Berlin origin;
        /// 🔒 user, <c>2026-10-08</c>: <i>"No default berlin. Missing geo = zeros."</i> The shipped terrains carry Berlin as their
        /// own data. ⚠ Call it ONCE per node — the node builder adopts the network factory's instance; a second one would not
        /// follow the terrain. 📄 docs/DESIGN_Geo_Origin.md §2.
        /// </summary>
        public static WGS84Transform CreateGeoTransform() => new WGS84Transform();

        /// <summary>
        /// A geo transform at a CALLER-SUPPLIED origin (degrees, degrees, metres) — for tests and tools whose data is authored
        /// against a known place without loading a terrain. ⛔ Production nodes take the origin from the terrain instead.
        /// </summary>
        public static WGS84Transform CreateGeoTransform(double latDeg, double lonDeg, double altMeters)
            => new WGS84Transform(latDeg, lonDeg, altMeters);

        public static DdsParticipant CreateParticipant(int domainId)
        {
            return new DdsParticipant((uint)domainId);
        }
    }
}