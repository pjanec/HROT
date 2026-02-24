namespace FDP.Toolkit.Behavior
{
    /// <summary>
    /// Compile-time constants governing buffer sizes and capacities in the Behavior toolkit.
    /// Centralised here so a single edit propagates everywhere.
    /// </summary>
    public static class BehaviorConstants
    {
        /// <summary>Byte budget for action parameter inline storage per channel.</summary>
        public const int ActionParamsByteSize = 32;

        /// <summary>Byte budget for per-action executor state inline storage per channel.</summary>
        public const int ActionStateByteSIze = 32;

        /// <summary>Maximum total size of any channel struct (enforced by ComponentLayoutTests).</summary>
        public const int MaxChannelSizeBytes = 96;

        /// <summary>Size of BrainBlackboard inline memory.</summary>
        public const int BrainBlackboardByteSize = 128;

        /// <summary>Maximum number of distinct action types per dispatcher.</summary>
        public const int MaxActionTypes = 64;
    }
}
