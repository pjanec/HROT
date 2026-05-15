using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Examples.UrbanCombat.Setup
{
    /// <summary>
    /// TKB blueprint registration for the five Urban Ambush entity types.
    /// Replaces the BATCH-14 <c>EntityBlueprints</c> factory methods, which called
    /// <c>world.AddComponent()</c> directly.  This class uses the <see cref="TkbTemplate"/> /
    /// <see cref="ITkbDatabase"/> pattern (as established by <c>TankTemplate.cs</c> in
    /// <c>Fdp.Examples.NetworkDemo</c>) so that every archetype can be retrieved and spawned
    /// by type ID anywhere in the pipeline (ScenarioDirector, tests, T7, …).
    /// </summary>
    /// <remarks>
    /// Component sets match DESIGN.md §9.2 verbatim, with two back-ports added in BATCH-12/13:
    /// <list type="bullet">
    ///   <item><see cref="PreviousCapabilities"/> on damageable entities (APC, Soldier, Insurgent)
    ///         — required by <c>HsmDamageBridgeSystem</c>.</item>
    ///   <item><see cref="HealthData"/> on damageable entities — required by
    ///         <c>MissionDirectorSystem.HealthCritical</c>.</item>
    /// </list>
    /// </remarks>
    public static class DemoTkbSetup
    {

        // ── Public entry point ────────────────────────────────────────────────────────

        /// <summary>
        /// Registers all five Urban Ambush entity templates with <paramref name="tkb"/>.
        /// Call once at application startup (e.g. from <see cref="HeadlessDemoApp.Initialize"/>).
        /// </summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            RegisterCivilianPedestrian(tkb);
            RegisterCivilianCar(tkb);
            RegisterMilitaryAPC(tkb);
            RegisterInfantrySoldier(tkb);
            RegisterInsurgent(tkb);
        }

        // ── Template: CivilianPedestrian (ID 1001) ───────────────────────────────────

        /// <summary>Tier-1 civilian pedestrian. Brain: hardcoded by TrafficBrainSystem.</summary>
        private static void RegisterCivilianPedestrian(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("CivilianPedestrian", tkbType: 1001);
            t.AddDescriptor(new TkbMasterDto { CustomName = "CivilianPedestrian" });
            tkb.Register(t);
        }

        // ── Template: CivilianCar (ID 1002) ─────────────────────────────────────────

        /// <summary>Tier-1 civilian car. Brain: hardcoded by TrafficBrainSystem (road-graph loop).</summary>
        private static void RegisterCivilianCar(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("CivilianCar", tkbType: 1002);
            t.AddDescriptor(new TkbMasterDto { CustomName = "CivilianCar" });
            tkb.Register(t);
        }

        // ── Template: MilitaryAPC (ID 2001) ─────────────────────────────────────────

        /// <summary>
        /// Tier-2 military APC. Brain: HSM ("ConvoyEscort_HSM").
        /// Damageable: PreviousCapabilities + HealthData back-ported from BATCH-12/13.
        /// </summary>
        private static void RegisterMilitaryAPC(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("MilitaryAPC", tkbType: 2001);
            t.AddDescriptor(new TkbMasterDto { CustomName = "MilitaryAPC" });
            tkb.Register(t);
        }

        // ── Template: InfantrySoldier (ID 2002) ─────────────────────────────────────

        /// <summary>
        /// Tier-2 infantry soldier. Brain: BTree ("InfantryCombat_BT").
        /// Rifle stats: ammo=30, 5 Hz fire rate.
        /// </summary>
        private static void RegisterInfantrySoldier(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("InfantrySoldier", tkbType: 2002);
            t.AddDescriptor(new TkbMasterDto { CustomName = "InfantrySoldier" });
            tkb.Register(t);
        }

        // ── Template: Insurgent (ID 2003) ────────────────────────────────────────────

        /// <summary>
        /// Tier-2 insurgent RPG operator. Brain: BTree ("Ambush_BT").
        /// Same structure as InfantrySoldier but Red faction and RPG stats (ammo=1, 0.1 Hz).
        /// </summary>
        private static void RegisterInsurgent(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("Insurgent", tkbType: 2003);
            t.AddDescriptor(new TkbMasterDto { CustomName = "Insurgent" });
            tkb.Register(t);
        }
    }
}
