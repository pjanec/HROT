using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Services;

namespace Fdp.Network.Cyclone.Systems
{
    /// <summary>
    /// System responsible for polling all registered translators for incoming network data.
    /// Iterates translators, which now own their data readers.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class CycloneIngressSystem : IEcsModuleSystem
    {
        private readonly DdsParticipant _participant;
        private readonly INetworkTranslator[] _translators;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<INetworkTranslator, SystemProfileData> _translatorProfileData = new();

        public IReadOnlyList<INetworkTranslator> Translators => _translators;
        
        public CycloneIngressSystem(
            DdsParticipant participant, 
            IEnumerable<INetworkTranslator> translators,
            NetworkEntityMap entityMap)
        {
            _participant = participant;
            _translators = new List<INetworkTranslator>(translators).ToArray();
            _entityMap = entityMap;

            foreach (var translator in _translators)
            {
                var translatorName = translator.TopicName;
                _translatorProfileData[translator] = new SystemProfileData(translatorName);
            }
        }
        
        public SystemProfileData? GetTranslatorProfileData(INetworkTranslator translator)
            => _translatorProfileData.TryGetValue(translator, out var data) ? data : null;

        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            
            // Iterate all translators (Polymorphism handles Unsafe vs Managed vs Replay)
            foreach (var translator in _translators)
            {
                var sw = Stopwatch.StartNew();
                // In Owner Model, we just call PollIngress.
                // The translator holds the Reader (or Replay source).
                translator.PollIngress(cmd, view);
                sw.Stop();

                if (_translatorProfileData.TryGetValue(translator, out var profile))
                {
                    profile.RecordExecution(sw.Elapsed.TotalMilliseconds);
                }
            }
        }
    }
}
