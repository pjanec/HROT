namespace Fdp.Examples.UrbanCombat.Blueprints
{
    /// <summary>
    /// Legacy ID constants for the five Urban Ambush entity blueprints.
    ///
    /// <para>
    /// <b>BATCH-15 (BCS-P7-T2):</b> The direct <c>world.AddComponent</c> factory methods that
    /// previously existed in this file have been removed.  All blueprint registration now goes
    /// through <see cref="Setup.DemoTkbSetup.RegisterAll"/>, which builds proper
    /// <c>TkbTemplate</c> objects and registers them with a <c>TkbDatabase</c>.
    /// </para>
    ///
    /// <para>
    /// Use <c>tkb.GetByType(id)</c> + <c>template.ApplyTo(world, entity)</c> to spawn entities.
    /// The ID constants below remain for use as named literals anywhere the numeric IDs are needed
    /// (e.g. ScenarioDirector, tests, telemetry).
    /// </para>
    /// </summary>
    [System.Obsolete("Factory methods removed in BATCH-15. Use DemoTkbSetup.RegisterAll + tkb.GetByType instead.")]
    public static class EntityBlueprints
    {
        // ── Blueprint IDs ────────────────────────────────────────────────────────────

        /// <summary>TKB type ID for <see cref="CivilianPedestrian"/>.</summary>
        public const int Id_CivilianPedestrian = 1001;

        /// <summary>TKB type ID for <see cref="CivilianCar"/>.</summary>
        public const int Id_CivilianCar = 1002;

        /// <summary>TKB type ID for <see cref="MilitaryAPC"/>.</summary>
        public const int Id_MilitaryAPC = 2001;

        /// <summary>TKB type ID for <see cref="InfantrySoldier"/>.</summary>
        public const int Id_InfantrySoldier = 2002;

        /// <summary>TKB type ID for <see cref="Insurgent"/>.</summary>
        public const int Id_Insurgent = 2003;

        // ── Faction IDs (convention per DESIGN.md §4.1) ─────────────────────────────

        private const byte FactionNeutral = 0;
        private const byte FactionBlue    = 1;
        private const byte FactionRed     = 2;

    }
}
