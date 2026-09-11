using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// ⭐ A replay gate: when <see cref="Enabled"/> is <c>false</c>,
    /// <see cref="ExecuteGroup"/> iterates zero systems. Toggled by
    /// <c>ReferenceReplayLoadHandler</c> — <c>false</c> on <c>RunningReplay</c>
    /// (<c>CGF1-S0304</c>), <c>true</c> again on <c>RunningLive</c> (<c>CGF1-S0305</c>).
    ///
    /// <para>⛔⛔⛔ <b>MEASURED <c>2026-09-11</c>: TODAY THIS GATE IS INERT. Toggling
    /// <see cref="Enabled"/> changes no behaviour whatsoever.</b> Two independent reasons, and
    /// <b>both</b> must be fixed before it protects anything — 📄 filed as <c>CE-259ap</c>:
    /// <list type="number">
    ///   <item>⛔ <b>Every production site passes exactly ONE member —
    ///     <c>GhostCreationSystem</c> — and that system's <c>Execute</c> is an empty body</b>
    ///     (*"No-op: system is registered for pipeline consistency"*). Ghosts are created by
    ///     <c>GhostCreationSystem.CreateGhost(...)</c>, called DIRECTLY by the ingress translators on
    ///     the Input phase — a path this gate cannot reach. ⇒ disabling the group stops a no-op.</item>
    ///   <item>⛔ <b>The same instance is ALSO registered with the scheduler</b>
    ///     (<c>NedReplicationModule.RegisterSystems</c>, <c>BdcReplicationModule</c>), which knows
    ///     nothing about <see cref="Enabled"/>. ⚠ Harmless only because the method is a no-op; it
    ///     means the gate would not hold even if the member did work.</item>
    /// </list></para>
    ///
    /// <para>⚠⚠ <b>An earlier version of this summary said the group holds *"the three network
    /// lifecycle systems — <c>LifecycleSystem</c>, <c>GhostPromotionSystem</c> and
    /// <c>NetworkGatewaySystem</c>"*, and that no *"lifecycle state changes or ghost promotions occur
    /// during playback."* ⛔ NONE of those three has ever been passed to it by any site.</b>
    /// ⭐⭐ <b>But that text was not invention — it was INTENT, and the design still holds it:</b>
    /// <c>docs/designs/replay-and-modules/DESIGN.md</c> §2.1 lists this group as *"Disabled during
    /// replay — block ghost create/promote/destroy during playback"*, and §3.10.3 names two further
    /// systems that *"must be moved inside"* it. ⇒ 🔒 <b>the CODE is behind the DESIGN here; the comment
    /// was ahead of it.</b> This summary now states what the class DOES and cites where the intent
    /// lives, so a reader can tell the two apart.</para>
    ///
    /// <para>⛔⛔⛔ <b>CORRECTED <c>2026-09-11</c> — AND NOTHING ELSE PROTECTS THE GHOST PATH EITHER.</b>
    /// ⚠ An earlier version of this remark said *"<c>TogglableInputGroup</c> is disabled during playback
    /// (§2.1), which is what stops live DDS ingress from reaching the translators that call
    /// <c>CreateGhost</c>."* 🔴 **That was taken from the design's Reason column and is FALSE as built.**
    /// 📐 Measured: <c>TogglableInputGroup</c> holds the LOGIC-PACK input systems
    /// (<c>MissionControlExecutionSystem</c>, <c>FireProcessingSystem</c>, …), while every one of the
    /// <b>11</b> production <c>CycloneNetworkIngressSystem</c> registrations is a DIRECT
    /// <c>RegisterSystem</c>/<c>RegisterGlobalSystem</c> — never into a togglable group — and
    /// <c>ReferenceReplayLoadHandler.SetSystemsEnabled</c> toggles only those four groups, touching no
    /// ingress system and no DDS participant.
    /// ⇒ 🔒 <b>live DDS ingress DOES reach a node in <c>RunningReplay</c></b>, proven by the rail
    /// <c>ReplayLoadClusterOpHandlerTests.RunningReplay_DoesNotStopADirectlyRegisteredInputPhaseSystem</c>.
    /// ⇒ ⛔ <b>all three documented protections for the ghost path are inert or absent</b>, so
    /// <c>BypassLifecycle</c> is LOAD-BEARING rather than dead weight. 📄 <c>CE-259ap</c>.</para>
    ///
    /// <para>⭐⭐ <b>And the gate that WOULD do the job already exists:</b>
    /// <c>CycloneNetworkIngressSystem.IsWorldStateFrozen</c> — a <c>Func&lt;bool&gt;</c> checked once per
    /// <c>Execute</c> that skips exactly the <c>TranslatorClass.WorldState</c> translators while letting
    /// control-plane ingress through. ⛔ Its ONE production writer is
    /// <c>CgfSubsystem.WireWorldStateFreezeGate</c>, driven by the <b>DEBUGGER halt</b> (<c>DQ30-C</c>),
    /// not by replay, and on one host only. ⇒ an under-adopted seam, which is why <c>CE-259ap</c>'s lean
    /// is to ADOPT it rather than to populate this group.</para>
    ///
    /// <para>⭐ <b>And the restore path delivers no ghosts to promote</b> — measured: no record/replay
    /// code writes <c>EntityMetadataCold.LifecycleState</c> at all, and its default is
    /// <c>EntityLifecycle.Constructing</c> (<c>= 0</c>), NOT <c>Ghost</c>, so a restored entity cannot
    /// match <c>GhostPromotionSystem</c>'s <c>WithLifecycle(Ghost)</c> query. ⇒ promotion's absence from
    /// this group is LATENT, not live.</para>
    /// </summary>
    public sealed class NetworkLifecycleSystemGroup
    {
        private readonly IEcsModuleSystem[] _innerSystems;

        /// <summary>
        /// When <c>false</c>, <see cref="ExecuteGroup"/> is a no-op — none of
        /// the inner systems' <c>Execute</c> methods are called.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Creates a group with the given inner systems.
        /// </summary>
        /// <param name="innerSystems">
        /// The ordered list of <see cref="IEcsModuleSystem"/> instances to execute
        /// when the group is enabled.  Typically: <c>LifecycleSystem</c>,
        /// <c>GhostPromotionSystem</c>, <c>NetworkGatewaySystem</c>.
        /// </param>
        public NetworkLifecycleSystemGroup(params IEcsModuleSystem[] innerSystems)
        {
            _innerSystems = innerSystems ?? System.Array.Empty<IEcsModuleSystem>();
        }

        /// <summary>
        /// Executes all inner systems in order, passing <paramref name="view"/>
        /// and <paramref name="deltaTime"/> to each.  Does nothing when
        /// <see cref="Enabled"/> is <c>false</c>.
        /// </summary>
        public void ExecuteGroup(ISimulationView view, float deltaTime)
        {
            if (!Enabled) return;
            foreach (var sys in _innerSystems)
                sys.Execute(view, deltaTime);
        }
    }
}
