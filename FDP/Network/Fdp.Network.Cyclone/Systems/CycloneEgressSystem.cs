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
    [TickMode(TickPolicy.EveryFrame)]
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

        /// <summary>⭐ CE-3427 N-1 — the pipeline this system handed its marked translators to (once per pipeline).</summary>
        private Egress.EgressPipeline? _registeredPipeline;

        private bool RegisteredWith(Egress.EgressPipeline p)
        {
            if (ReferenceEquals(_registeredPipeline, p)) return true;
            _registeredPipeline = p;
            return false;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            ContributeDescriptorMap(view);

            // ⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7) — the world's off-frame scan launched last frame has finished before
            //   any translator is touched; while the pipeline is OFF the frame, a marked translator only begins its frame here and the
            //   pipeline scans it on a replica after the phase. No pipeline (the default for every node but one that registers its launch
            //   system) ⇒ exactly today's loop.
            Egress.EgressPipeline? pipeline = null;
            if (Egress.EgressPipeline.TryGet(view, out var p))
            {
                p.Join();
                if (!RegisteredWith(p))
                    for (int i = 0; i < _translators.Length; i++)
                        if (_translators[i] is Egress.IOffFrameEgress off)
                            p.Register(off, _translatorProfileData.TryGetValue(_translators[i], out var prof) ? prof : null);
                if (p.IsOffFrame) pipeline = p;
            }

            // ⭐⭐ CE-3329 PW — §12c W2: between a world swap and the master's commit this node publishes NOTHING (the cleanup's
            //   disposes still go out — they are another system). 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12a.
            //   ⚠ Nothing is lost: a seek / restore marks every descriptor dirty (W4), so the release republishes everything.
            if (Fdp.Toolkit.Replication.Services.EgressHold.IsHeld(view)) return;

            // ⭐ CE-2418 — ONE walk for every translator: inside this scope each translator's query walks only the entities of its rarest
            //   included type (a candidate index built from the masks once per frame, shared by every egress system of the Export phase).
            //   📐 A/B on one binary (700 soldiers): per entity per frame CGF 14.3 / 15.4 → 7.4 / 9.2 µs, SimHost 25.1 / 26.4 → 5.4 / 14.1,
            //   traffic 20.4 / 20.6 → 18.9 / 9.1. 📄 docs/DESIGN_Egress_Change_Detection.md §6.
            using var oneWalk = view is EntityRepository repo ? repo.QueryIndexScope() : default;

            // Process force-publish requests first
            ProcessForcePublish(view);
            
            //FDP.Kernel.Logging.FdpLog<CycloneEgressSystem>.Info("Publishing via {0} translators", _translators.Length);

            // Normal periodic publishing
            for (int i = 0; i < _translators.Length; i++)
            {
                var translator = _translators[i];
                if (pipeline != null && translator is Egress.IOffFrameEgress offFrame)
                {
                    offFrame.BeginFrame(view);   // the bus / epoch bookkeeping stays on the live world, here; the scan runs off the frame
                    continue;
                }
                // ⭐ CE-2404 (R-2084) — a timestamp, not a Stopwatch object per translator per frame (≈ 90 translators × every world;
                //   📐 Stopwatch.Stop alone was 1.3–2.3 % of the main thread in the --mode all traces of 2026-11-04)
                long started = Stopwatch.GetTimestamp();
               // FDP.Kernel.Logging.FdpLog<CycloneEgressSystem>.Info("Scanning {0}: {1}", i, _translators[i].DescriptorOrdinal);
                translator.ScanAndPublish(view);

                if (_translatorProfileData.TryGetValue(translator, out var profile))
                {
                    profile.RecordExecution(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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
