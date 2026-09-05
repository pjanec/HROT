using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Replication.Components;
using Fdp.Interfaces; // For Interfaces

namespace Fdp.Network.Cyclone.Systems
{
    /// <summary>
    /// System responsible for publishing owned descriptors to the network.
    /// Handles normal periodic publishing and force-publish requests.
    /// </summary>
    [UpdateInPhase(SystemPhase.Export)]
    public class CycloneEgressSystem : IEcsModuleSystem
    {
        private readonly INetworkTranslator[] _translators;
        private readonly Dictionary<INetworkTranslator, SystemProfileData> _translatorProfileData = new();

        public IReadOnlyList<INetworkTranslator> Translators => _translators;
        
        public CycloneEgressSystem(INetworkTranslator[] translators)
        {
            _translators = translators ?? throw new ArgumentNullException(nameof(translators));

            foreach (var translator in _translators)
            {
                var translatorName = translator.TopicName;
                _translatorProfileData[translator] = new SystemProfileData(translatorName);
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>Q59-E</c> — publishes this system's translators into the world's component→descriptor
        /// map, so the FDP attribute path never has to name a descriptor.</b>
        ///
        /// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §7 · §9.3. 🔒 User ruling: *"attributes
        /// are entity-related, network agnostic. In contrary, descriptors are Ned network concept."*</para>
        ///
        /// <para>⭐⭐⭐ <b>WHY HERE, of all places.</b> 📐 Measured <c>2026-08-26</c>: this is the ONE type that
        /// already receives the translator array AND is handed the world. ⇒ ⛔ no host has to remember a
        /// registration call, which is the silent-default trap this codebase keeps falling into.
        /// ⚠ <c>CycloneNetworkModule</c> looked like the natural home and is <b>never instantiated in
        /// production</b> — measured, not assumed. And the translator lists are assembled in 4+ host-side
        /// places *(a main pack plus a gizmo pack per host)*, so there is no single host-side seam either.</para>
        ///
        /// <para>⭐ <b>Additive and idempotent:</b> several egress systems per world each contribute, and the
        /// map is their union — ⛔ a "set" API would let the second erase the first.</para>
        ///
        /// <para>⭐⭐ It feeds <c>DescriptorOwnershipMap</c>, the EXISTING *"Single Source of Truth for the
        /// descriptor → component mapping"* — 📌 not a new map. A rival type was written and deleted before
        /// shipping once that was measured.</para>
        ///
        /// <para>⚠ <b>The one-frame window, stated rather than hidden.</b> The map is set on the first
        /// Execute, so a patch applied before it would not mark. ⭐ It is benign: egress runs every frame,
        /// attribute patches require a live cluster, and <c>SmartEgressUtil.ShouldPublish</c> returns
        /// <see langword="true"/> for an entity with no publication state at all *(a deliberate fail-safe)*,
        /// so such an entity publishes anyway. ⛔ Unlike <c>AX-015</c>, nothing is permanently lost.</para>
        /// </summary>
        private void ContributeDescriptorMap(ISimulationView view)
        {
            if (_contributedDescriptorMap) return;
            if (view is not EntityRepository repo) return;   // ⭐ the established pattern in the translators

            Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider.ContributeTranslators(
                repo, _translators.OfType<Fdp.Interfaces.IDescriptorTranslator>());

            _contributedDescriptorMap = true;
        }

        public SystemProfileData? GetTranslatorProfileData(INetworkTranslator translator)
            => _translatorProfileData.TryGetValue(translator, out var data) ? data : null;
        
        /// <summary>
        /// ⭐⭐⭐ <c>Q59-E</c> — set once, on the first Execute, so no host has to remember.
        /// See <see cref="ContributeDescriptorMap"/>.
        /// </summary>
        private bool _contributedDescriptorMap;

        public void Execute(ISimulationView view, float deltaTime)
        {
            ContributeDescriptorMap(view);

            // Process force-publish requests first
            ProcessForcePublish(view);
            
            //FDP.Kernel.Logging.FdpLog<CycloneEgressSystem>.Info("Publishing via {0} translators", _translators.Length);

            // Normal periodic publishing
            for (int i = 0; i < _translators.Length; i++)
            {
                var translator = _translators[i];
                var sw = Stopwatch.StartNew();
               // FDP.Kernel.Logging.FdpLog<CycloneEgressSystem>.Info("Scanning {0}: {1}", i, _translators[i].DescriptorOrdinal);
                translator.ScanAndPublish(view);
                sw.Stop();

                if (_translatorProfileData.TryGetValue(translator, out var profile))
                {
                    profile.RecordExecution(sw.Elapsed.TotalMilliseconds);
                }
            }
        }
        
        private void ProcessForcePublish(ISimulationView view)
        {
            var cmd = view.GetCommandBuffer();
            
            // Query entities with ForceNetworkPublish
            var query = view.Query()
                .With<ForceNetworkPublish>()
                .Build();
            
            foreach (var entity in query)
            {
                // Remove the component - it's one-time
                cmd.RemoveComponent<ForceNetworkPublish>(entity);
                
                // Force publish happens implicitly in next ScanAndPublish
                // The translators will see this entity and publish it
            }
        }
    }
}
