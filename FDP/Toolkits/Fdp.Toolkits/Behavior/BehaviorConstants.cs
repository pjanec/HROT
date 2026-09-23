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
        /// ⚠ <b>The reservation width for an UNDER-DECLARED behaviour — not a cap, and no longer a
        /// struct's width either.</b>
        ///
        /// <para>⛔⛔ <b>Do NOT reintroduce this as a limit.</b> <c>CE-307</c> repointed every bound at
        /// <see cref="MaxRootParamsByteSize"/>. ⛔ And <c>P4</c> deleted <c>BrainBlackboard</c>, so the
        /// <c>fixed byte[100]</c> this used to declare is gone too.</para>
        ///
        /// <para>⭐ <b>One production reader remains:</b>
        /// <see cref="RootParamsAccess.RootParamsBytes"/> hands back this width for a behaviour that
        /// declares a <c>ParseParams</c> and NEITHER a manifest NOR a layout type — the documented
        /// escape hatch. ⚠ It reproduces the pre-<c>P3-C</c> behaviour exactly, when the full region
        /// existed whether anyone declared it or not; returning 0 there would silently drop the parse
        /// on the floor. ⛔ Every behaviour in the shipped corpus declares one of the two, so this is
        /// the hatch for hand-registered and test behaviours, not a path production takes.</para>
        ///
        /// <para>⚠ Tests also use it as a scratch host-blackboard width, where any value would do.</para>
        /// </summary>
        public const int MaxBehaviorParamByteSize = 100;

        // ⛔ `BrainBlackboardByteSize` IS DELETED with the struct it sized — `P4` (2026-09-22).
        //   ⚠ `CE-307` had already taken its OTHER consumer away: BehaviorIngressSystem's parse shadow
        //   is sized per behaviour from RootParamsBytes(def) now, because a constant-width shadow made
        //   a >100-byte behaviour a stack smash. ⇒ the struct's [StructLayout(Size=…)] was the last
        //   reader, and it went with the struct.




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
