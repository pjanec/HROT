namespace Fdp.Toolkit.Behavior
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

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-307</c> — THE REAL CEILING ON A BEHAVIOUR'S ROOT PARAMS: the largest tier's
        /// payload.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §30.25.
        ///
        /// <para>🔴 <b>This REPLACES <see cref="MaxBehaviorParamByteSize"/> as the bound</b>, in every
        /// site that used 100 as a LIMIT. ⛔ 100 was a <b>buffer-overrun guard</b>: params lived inline
        /// in a fixed-layout struct <i>with neighbours after them</i>, so overflow silently overwrote
        /// unrelated state. ⇒ after <c>P3-C</c> params land in an occurrence slot sized
        /// <see cref="RootParamsAccess.RootParamsBytes"/>, promoted up the 256/1024/4096/16384 ladder,
        /// with <c>TryAttach</c> failing <b>structurally</b> and ingress throwing a named error.
        /// <b>No neighbours, no cap.</b></para>
        ///
        /// <para>⭐ <b>What is left is a CAPACITY bound, not a corruption guard</b> — a params region
        /// wider than the largest tier's whole payload cannot be stored by any tier, so it is still
        /// worth refusing at build time rather than at run time. ⚠ It is a CEILING, not a budget: a
        /// region this wide would consume the entire 16384 tier and leave no room for the behaviour's
        /// stateful slots, and ingress would then throw. ⛔ Do not read agreement with this number as
        /// "it will fit".</para>
        /// </summary>
        public const int MaxRootParamsByteSize = Blueprints.Shared.BlueprintTierLadder.Tier16384PayloadSize;

        /// <summary>
        /// ⚠ <b>The declared width of <c>BrainBlackboard.BehaviorParameters</c> — a LEGACY BUFFER
        /// SIZE, no longer a cap.</b>
        ///
        /// <para>⛔⛔ <b>Do NOT reintroduce this as a limit.</b> <c>CE-307</c> repointed every bound at
        /// <see cref="MaxRootParamsByteSize"/>; what remains here is the width of a <c>fixed byte[]</c>
        /// in a component that <c>P4</c> is retiring, plus the reservation width
        /// <see cref="RootParamsAccess.RootParamsBytes"/> hands an under-declared behaviour (a parser
        /// with neither a manifest nor a layout type — the documented escape hatch, which reproduces
        /// the pre-cut behaviour exactly).</para>
        ///
        /// <para>⚠ <b>It dies with the struct</b> (<c>P4</c> §2 ②). Until then it is load-bearing for
        /// the buffer declaration and for tests that size a scratch host blackboard from it.</para>
        /// </summary>
        public const int MaxBehaviorParamByteSize = 100;

        /// <summary>Size of BrainBlackboard inline memory.</summary>
        // ⭐ `O2` (2026-09-20): with the entity-fact tail moved to BrainInterrupts, the blackboard IS
        //   the params region — 128 → 100. The 28 bytes between the params region and the old
        //   interrupt registers were dead weight on every brain entity.
        // ⛔ CE-307 (2026-09-22): the second reason this name existed is GONE. BehaviorIngressSystem's
        //   transactional-parse shadow no longer sizes itself from it — it is sized per behaviour, from
        //   RootParamsBytes, because a constant-width shadow made a >100-byte behaviour a stack smash.
        //   ⇒ this now has exactly ONE consumer, the struct's own [StructLayout(Size=…)], and it dies
        //   with the struct.
        public const int BrainBlackboardByteSize = MaxBehaviorParamByteSize;



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
        /// Must match the event ID registered in behavior HSM definitions (by convention: 1).
        /// </summary>
        public const ushort EventId_MobilityLost = 1;

        /// <summary>
        /// Interaction action ID for the <see cref="Executors.EjectPassengersExecutor"/>.
        /// Registered with <see cref="Systems.InteractionDispatcherSystem"/> at application startup.
        /// Value must match the action ID used when registering the executor.
        /// </summary>
        public const ushort ActionIdEjectPassengers = 3;

        /// <summary>Action ID for OpenDoor (Slice 1 Demo).</summary>
        public const ushort ActionIdOpenDoor = 4;

        // ── Unmanaged event IDs (Behavior behavior range: 3100–3199) ─────────────
        /// <summary>EventId for <c>ClearBehaviorEvent</c>.</summary>
        public const int EventId_ClearBehavior = 3100;

        /// <summary>EventId for <c>BehaviorFinishedEvent</c>.</summary>
        public const int EventId_BehaviorFinished = 3101;

        /// <summary>EventId for <c>AssignBehaviorHashEvent</c>.</summary>
        public const int EventId_AssignBehaviorHash = 3102;

        /// <summary>EventId for <c>CognitiveInterruptEvent</c>.</summary>
        public const int EventId_CognitiveInterrupt = 3103;

        // ── Embarkation command IDs (edit-1/EDIT1-E001) ──────────────────────
        /// <summary>EventId for <c>EmbarkEntityCommand</c>.</summary>
        public const int EventId_EmbarkEntity    = 3201;

        /// <summary>EventId for <c>DisembarkEntityCommand</c>.</summary>
        public const int EventId_DisembarkEntity = 3202;
    }
}
