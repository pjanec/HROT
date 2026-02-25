using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FDP.Toolkit.Behavior;

namespace Fdp.Examples.UrbanCombat.Brains
{
    /// <summary>
    /// Compiles the "ConvoyEscort_HSM" state machine definition for the Military APC.
    ///
    /// <para>States (BCS-P7-T6 / DESIGN.md §9.4):</para>
    /// <list type="table">
    ///   <item><c>Cruising</c> (initial) — activity: <see cref="ApcHsmActions.Activity_Cruise"/>.</item>
    ///   <item><c>Disabled</c> — entry: <see cref="ApcHsmActions.OnEnter_Disabled"/>;
    ///         entered on <see cref="BehaviorConstants.EventId_MobilityLost"/>.</item>
    /// </list>
    ///
    /// <para>After BFS normalisation the flat state indices are:</para>
    /// <list type="table">
    ///   <item>0 — synthetic root state (HsmBuilder always inserts this)</item>
    ///   <item><see cref="CruisingStateIndex"/> (1) — Cruising</item>
    ///   <item><see cref="DisabledStateIndex"/> (2) — Disabled</item>
    /// </list>
    /// </summary>
    public static class ApcHsmSetup
    {
        // ── State index constants (BFS order: root=0, then children in definition order) ──

        /// <summary>
        /// Flat state index assigned to the <c>Cruising</c> state after BFS normalisation.
        /// Root gets index 0; Cruising is the first user-defined state → index 1.
        /// </summary>
        public const ushort CruisingStateIndex = 1;

        /// <summary>
        /// Flat state index assigned to the <c>Disabled</c> state after BFS normalisation.
        /// Root=0, Cruising=1, Disabled=2.
        /// </summary>
        public const ushort DisabledStateIndex = 2;

        // ── Builder ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and returns the compiled <see cref="HsmDefinitionBlob"/> for the
        /// "ConvoyEscort_HSM" state machine.
        ///
        /// <para>Uses the fluent <see cref="HsmBuilder"/> API, then normalises,
        /// validates, flattens, and emits the blob in one call.  The blob is
        /// stored in the <see cref="DoctrineRegistry"/> at startup and consumed
        /// by <c>HsmTickSystem&lt;BrainHsm128&gt;</c> each frame.</para>
        /// </summary>
        public static HsmDefinitionBlob Build()
        {
            var builder = new HsmBuilder("ConvoyEscort_HSM");

            // ── Events ──
            builder.Event("MobilityLost", BehaviorConstants.EventId_MobilityLost);

            // ── Action name registration (required before state definition) ──
            builder
                .RegisterAction("Activity_Cruise")
                .RegisterAction("OnEnter_Disabled");

            // ── States ──
            var cruising = builder.State("Cruising")
                .Activity("Activity_Cruise")
                .Initial();

            var disabled = builder.State("Disabled")
                .OnEntry("OnEnter_Disabled");

            // ── Transitions ──
            // Cruising → Disabled on MobilityLost (EventId = 1)
            cruising.On(BehaviorConstants.EventId_MobilityLost).GoTo(disabled);

            // ── Compile ──
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);

            var errors = HsmGraphValidator.Validate(graph);
            if (errors.Count > 0)
                throw new System.InvalidOperationException(
                    $"APC HSM validation failed: {string.Join(", ", errors.Select(e => e.Message))}");

            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
        }
    }
}
