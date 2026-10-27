using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

using INetworkTranslator = Fdp.Interfaces.INetworkTranslator;

namespace Fdp.Network.Cyclone.Modules
{
    /// <summary>
    /// ⭐⭐ <b><c>AX-021</c> — extracted from <c>CycloneNetworkModule.cs</c> when that module was deleted.</b>
    ///
    /// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §13. 📐 Measured <c>2026-08-26</c>: this class
    /// is constructed at <b>23 production sites across 12 files</b>, while <c>CycloneNetworkModule</c> — which
    /// shared this file — was constructed <b>nowhere at all</b>.</para>
    ///
    /// <para>⭐⭐⭐ <b>That shared file is WHY the dead module stayed invisible for so long.</b> The FILE was
    /// alive because of THIS class, so no unused-file sweep, compiler warning or reference count could ever
    /// flag the module beside it. ⇒ ⛔ one live class and one dead one in a single file defeats every automatic
    /// check — worth remembering as a pattern, not just as this instance.</para>
    ///
    /// <para>⭐ The namespace is unchanged *(<c>Fdp.Network.Cyclone.Modules</c>)*, so this move needed no
    /// <c>using</c> edits at any of the 23 sites.</para>
    /// </summary>
    // Local implementation of Ingress System since it appears missing from Core
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public class CycloneNetworkIngressSystem : IEcsModuleSystem, Fdp.Interfaces.IReplayRestoreGated
    {
        private readonly Fdp.Interfaces.INetworkTranslator[] _translators;
        public IReadOnlyList<INetworkTranslator> Translators => _translators;

        /// <summary>
        /// ⭐⭐⭐ <b><c>DQ30-C</c>'s gate: is a debugger holding this node's world frozen?</b>
        ///
        /// <para>While it answers <c>true</c>, translators carrying
        /// <see cref="Fdp.Interfaces.TranslatorClass.WorldState"/> are NOT polled, and translators
        /// carrying <see cref="Fdp.Interfaces.TranslatorClass.ControlPlane"/> still are — which is
        /// what lets the resume reach a frozen node.</para>
        ///
        /// <para>⭐⭐ <b>A settable property, not a constructor argument, and not a silent default.</b>
        /// 📐 Measured <c>2026-08-25</c>: this system is constructed at <b>12 production sites across
        /// 9 files in 6 assemblies</b>, most of them inside registration helpers shared by SimHost, IG
        /// and CGF — none of which holds a debugger. ⇒ a constructor parameter would have to be
        /// threaded through every one of them and defaulted at almost all, which is precisely the
        /// silent-default shape. ⭐ Left unset the system behaves EXACTLY as before, so a node with no
        /// debugger is unchanged by construction; the node that HAS one sets it explicitly.</para>
        /// </summary>
        public System.Func<bool>? IsWorldStateFrozen { get; set; }

        /// <summary>
        /// ⭐⭐ <c>R-4007</c> (Q91 §4-M) — <b>is this node frozen for a JOIN?</b> True while the node cannot build what it would
        /// receive (its cluster state is <c>Idle</c>: before it commits a load, and again after a reset). While true,
        /// <see cref="Fdp.Interfaces.TranslatorClass.WorldState"/> translators are not polled (DDS keeps the latest sample per
        /// instance), <see cref="Fdp.Interfaces.TranslatorClass.WorldEvent"/> translators are DRAINED and their samples
        /// discarded, and <see cref="Fdp.Interfaces.TranslatorClass.ControlPlane"/> keeps polling.
        /// ⭐ Separate from <see cref="IsWorldStateFrozen"/>: the debugger HOLDS events, a join DISCARDS them.
        /// Wired after composition by <see cref="WireJoinGate"/>; left unset the system behaves exactly as before.
        /// </summary>
        public System.Func<bool>? IsJoinFrozen { get; set; }

        /// <inheritdoc />
        public System.Func<bool>? IsReplayRestoring { get; set; }

        // ⭐⭐ CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — set at the exit's release, consumed by the next Execute that is not gated.
        private volatile bool _exitDiscardPending;

        /// <summary>⭐ CE-3329 W5 — how many exits this system has drained, and how many samples the last one threw away.</summary>
        public int ExitDiscards { get; private set; }

        /// <inheritdoc cref="ExitDiscards"/>
        public int LastExitDiscardedSamples { get; private set; }

        /// <inheritdoc />
        public void DiscardAtReplayExit() => _exitDiscardPending = true;

        /// <summary>
        /// ⭐⭐ <c>R-4007</c> — wires <paramref name="isJoinFrozen"/> onto every ingress system among <paramref name="systems"/>
        /// (the kernel's scheduled systems, swept once after composition — the shape CGF's debugger gate already uses).
        /// Returns how many were wired.
        /// </summary>
        public static int WireJoinGate(System.Collections.IEnumerable systems, System.Func<bool> isJoinFrozen)
        {
            int wired = 0;
            foreach (var system in systems)
            {
                if (system is not CycloneNetworkIngressSystem ingress) continue;
                ingress.IsJoinFrozen = isJoinFrozen;
                wired++;
            }
            return wired;
        }

        public CycloneNetworkIngressSystem(Fdp.Interfaces.INetworkTranslator[] translators)
        {
             _translators = translators;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // ⚠ Asked ONCE per Execute, not per translator: the answer must not change mid-phase or
            //   half the world-state translators would run against a different decision than the
            //   other half.
            bool frozen  = IsWorldStateFrozen?.Invoke() ?? false;
            // ⭐ R-4012 — restoring a replay from its own recording is treated like a join: the node builds the world itself.
            bool joining = (IsJoinFrozen?.Invoke() ?? false) || (IsReplayRestoring?.Invoke() ?? false);

            // ⭐⭐ CE-3329 W5 — the first frame after a replay's / review's exit is released: every world translator's readers are
            //   emptied BEFORE anything is polled. The node already holds its world; what the readers kept is the replay era's (the
            //   newest state per instance, the entry's disposes) and would land on top of it. The owners' republish refills them.
            if (_exitDiscardPending && !joining)
            {
                _exitDiscardPending = false;
                int discarded = 0;
                for (int i = 0; i < _translators.Length; i++)
                    if (_translators[i].Category != Fdp.Interfaces.TranslatorClass.ControlPlane
                        && _translators[i] is Fdp.Interfaces.IDiscardableIngress discardable)   // ⭐ every one is — the sweep rail (§12u) holds it
                        discarded += discardable.DiscardIngress();
                ExitDiscards++;
                LastExitDiscardedSamples = discarded;
            }

            var cmd = view.GetCommandBuffer();
            for(int i=0; i<_translators.Length; i++)
            {
                    var category = _translators[i].Category;
                    if (category != Fdp.Interfaces.TranslatorClass.ControlPlane)
                    {
                        if (joining)
                        {
                            // ⭐ R-4007 — a join DISCARDS events (no stale burst at the opening) and leaves state in DDS.
                            if (category == Fdp.Interfaces.TranslatorClass.WorldEvent)
                                ((Fdp.Interfaces.IDiscardableIngress)_translators[i]).DiscardIngress();
                            continue;
                        }
                        if (frozen) continue;   // the debugger HOLDS state and events alike (DQ30-C)
                    }

                    _translators[i].PollIngress(cmd, view);
            }
        }
    }
}
