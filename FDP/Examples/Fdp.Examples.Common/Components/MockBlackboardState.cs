namespace Fdp.Examples.Common.Components
{
    /// <summary>
    /// Unsafe overlay struct used to represent cognitive blackboard memory state
    /// in test scenarios without a full <c>BrainBlackboard</c> dependency.
    /// </summary>
    public unsafe struct MockBlackboardState
    {
        /// <summary>Whether a threat is currently visible to this entity.</summary>
        public bool ThreatVisible;

        /// <summary>Current ammo count available for combat actions.</summary>
        public int AmmoCount;

        /// <summary>Rules of Engagement byte: 0 = hold fire, 1 = weapons free, 2 = weapons tight.</summary>
        public byte CurrentRoE;
    }
}
