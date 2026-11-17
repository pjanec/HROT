namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// ComponentId constants for fake navigation ECS components.
    /// IDs 262-269: navigation fake ECS component block (⭐ R-10000, 2026-11-17: the old "262-279" note was stale — 270-276 are the squad
    /// block's production components and 277-279 are the air lane's AirCombatSteer / FighterPilotState / AirCombatState).
    /// Block 250-261 is unavailable:
    ///   250-256 originally reserved here but 251 conflicts with PerceptionReceptor,
    ///   257-261 taken by NavigationContractsComponentIds (nav v2 production components).
    /// </summary>
    public static class NavFakeIds
    {
        public const int FakeNavmeshState        = 262;
        public const int FakeCrowdGlobalState    = 263;
        public const int FakeCrowdAgentState     = 264;
        // 265: RELINQUISHED to NavigationContractsComponentIds.CrowdMotorIntent (the Stride port).
        //      FakeVolumetricState was RESERVED here and never attached to a component -- the constant
        //      below was its only reference -- so moving it costs nothing and leaves 265 with exactly
        //      one claimant. Had it stayed, a fake volumetric state built later would have collided
        //      with a production component, silently.
        // 266: reserved (formerly FakePathPoolEntry -- not an ECS component; stored in dictionary)
        public const int FakeBrainPathCacheEntry = 267;
        public const int FakePathRegistryStats   = 268;
        public const int FakeVolumetricState     = 269;   // moved from 265, see above
    }
}

