using System;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐⭐ <b>CE-279 Layer A — the ONE place every host registers its <see cref="NodeOpType.SerializeLocal"/>
    /// handler pair, in a canonical order.</b>
    ///
    /// <para><c>SerializeLocal</c> is shared by two concerns that used to be registered ad-hoc and divergently
    /// per host (measured `2026-09-15`): the <b>scenario-JSON save</b> (payload
    /// <see cref="Handlers.ScenarioSaveHandlerPayload"/>) and the <b>.fdp exercise-recording archive</b> (payload
    /// <see cref="Handlers.ArchiveHandlerPayload"/>). Hosts diverged in three ways — some registered the save
    /// handler only conditionally (SimHost's was null-gated OFF in production), some registered no archive
    /// handler (IG, Editor), and the relative ORDER flipped (CGF registered Save-before-Archive while
    /// SimHost/ExCon did Archive-before-Save). Because <c>ClusterSlave</c> dispatches to the FIRST
    /// <c>CanHandle</c>-true handler, that divergence caused the archive handler to SHADOW the scenario save on
    /// some hosts — no scenario slice was ever written.</para>
    ///
    /// <para>⭐ This registrar makes the registration IDENTICAL on every ECS host: the scenario-save handler is
    /// registered BEFORE the archive handler, deterministically. Correct selection no longer relies on order —
    /// each handler's payload-aware <see cref="IClusterStateHandler.CanHandle(ExecuteNodeOpIntent)"/> claims only
    /// its own payload (CE-279 Layer C) — but a fixed order keeps behaviour reproducible across hosts.</para>
    ///
    /// <para>⛔ The scenario-save INSTANCE is still built by each host (it needs host-specific inputs — a serializer
    /// and world for the ECS save, an observer state for ExCon's), but the SELECTION and ORDER are unified here.
    /// ⭐⭐ <c>P5b</c> (Q91 §4-G, <c>R-4003</c>) — the ARCHIVE handler is built HERE, from the two things it needs (the
    /// folder the node records to and its id), so no host can register none: an export over a cluster then includes
    /// every node's <c>.fdp</c>. ⛔ Was: a nullable parameter, and IG and the Editor passed <c>null</c>.
    /// 📄 docs/DESIGN_Unified_Cluster_Handler_Registration.md §6 (Layer A).</para>
    /// </summary>
    public static class SerializeLocalRegistrar
    {
        /// <summary>
        /// Registers the <c>SerializeLocal</c>-family handlers on <paramref name="slave"/> in canonical order:
        /// <paramref name="scenarioSaveHandler"/> first (so a scenario payload is never shadowed), then
        /// <paramref name="archiveHandler"/>. Either may be <see langword="null"/> for a host that does not
        /// offer that concern (e.g. a host with no scenario serializer passes a null save handler).
        /// </summary>
        /// <param name="slave">The node's cluster slave to register into. Required.</param>
        /// <param name="scenarioSaveHandler">The host's scenario-save handler (ECS: <c>HrotScenarioSaveHandler</c>;
        /// observer: <c>ExConScenarioSaveHandler</c>), or <see langword="null"/> if this host saves no scenario.</param>
        /// <param name="recordingRoot">The folder this node records its <c>.fdp</c> to — the same folder its load chain's
        /// recording controller is given. A node that has recorded nothing reports an empty manifest.</param>
        /// <param name="nodeId">This node's id, which names its recording files.</param>
        public static void Register(
            ClusterSlave slave,
            IClusterStateHandler? scenarioSaveHandler,
            string recordingRoot,
            int nodeId)
        {
            if (slave is null) throw new ArgumentNullException(nameof(slave));

            // ⭐ Scenario save FIRST, archive SECOND — one order for every host. With payload-aware CanHandle
            //   the order is not load-bearing, but keeping it fixed makes every host's slave identical.
            if (scenarioSaveHandler is not null) slave.RegisterHandler(scenarioSaveHandler);
            slave.RegisterHandler(new Handlers.ReferenceArchiveHandler(recordingRoot, nodeId));
        }
    }
}
