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
    public class CycloneNetworkIngressSystem : IEcsModuleSystem
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

        public CycloneNetworkIngressSystem(Fdp.Interfaces.INetworkTranslator[] translators)
        {
             _translators = translators;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // ⚠ Asked ONCE per Execute, not per translator: the answer must not change mid-phase or
            //   half the world-state translators would run against a different decision than the
            //   other half.
            bool frozen = IsWorldStateFrozen?.Invoke() ?? false;

            var cmd = view.GetCommandBuffer();
            for(int i=0; i<_translators.Length; i++)
            {
                    if (frozen && _translators[i].Category == Fdp.Interfaces.TranslatorClass.WorldState)
                        continue;

                    _translators[i].PollIngress(cmd, view);
            }
        }
    }
}
