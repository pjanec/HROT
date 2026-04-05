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

        /// <summary>Brain tier value for HSM-driven entities (FastHSM).</summary>
        public const byte BrainTierHsm = 1;

        /// <summary>Brain tier value for BTree-driven entities (FastBTree interpreter).</summary>
        public const byte BrainTierBTree = 2;

        /// <summary>
        /// SimTier value for Tier-1 civilian entities, driven by <see cref="Systems.TrafficBrainSystem"/>.
        /// </summary>
        public const byte SimTierCivilian = 1;

        /// <summary>
        /// SimTier value for Tier-2 tactical entities driven by BTree or HSM brains.
        /// </summary>
        public const byte SimTierTactical = 2;

        /// <summary>
        /// HSM event ID injected by <c>HsmDamageBridgeSystem</c> when <c>CanMove</c> is cleared.
        /// Must match the event ID registered in doctrine HSM definitions (by convention: 1).
        /// </summary>
        public const ushort EventId_MobilityLost = 1;

        /// <summary>
        /// Interaction action ID for the <see cref="Executors.EjectPassengersExecutor"/>.
        /// Registered with <see cref="Systems.InteractionDispatcherSystem"/> at application startup.
        /// Value must match the action ID used when registering the executor.
        /// </summary>
        public const ushort ActionIdEjectPassengers = 3;

        // ── Unmanaged event IDs (Behavior doctrine range: 3100–3199) ─────────────
        /// <summary>EventId for <c>ClearDoctrineEvent</c>.</summary>
        public const int EventId_ClearDoctrine = 3100;

        /// <summary>EventId for <c>DoctrineFinishedEvent</c>.</summary>
        public const int EventId_DoctrineFinished = 3101;

        /// <summary>EventId for <c>AssignDoctrineHashEvent</c>.</summary>
        public const int EventId_AssignDoctrineHash = 3102;

        // ── Embarkation command IDs (edit-1/EDIT1-E001) ──────────────────────
        /// <summary>EventId for <c>EmbarkEntityCommand</c>.</summary>
        public const int EventId_EmbarkEntity    = 3201;

        /// <summary>EventId for <c>DisembarkEntityCommand</c>.</summary>
        public const int EventId_DisembarkEntity = 3202;
    }
}
