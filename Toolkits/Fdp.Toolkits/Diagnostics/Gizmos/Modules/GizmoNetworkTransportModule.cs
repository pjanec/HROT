using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Hub;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Modules
{
    // Installable IEcsModule for DDS transport. Installed once for the lifetime of the DDS
    // infrastructure (not once per remote terminal). The internal GizmoCapabilitiesTracker
    // manages per-terminal listener count tracking via OnSample() calls.
    //
    // When networkFactory.Participant is null (headless / test mode), the DDS writer and
    // reader are absent; the module still functions and the tracker can be exercised directly.
    //
    // Design: DESIGN.md §4.2
    public sealed class GizmoNetworkTransportModule : IEcsModule, IDisposable
    {
        private readonly GizmoExecutionController _controller;
        private readonly GizmoUiStateHub _uiHub;
        private readonly IGizmoUiStatePublisher? _ddsUiPublisher;
        private readonly IEcsModuleSystem? _primitivePublisherSystem;
        private readonly IReadOnlyList<INetworkTranslator> _translators;

        // Exposed so tests can drive lifecycle detection without a live DDS participant.
        internal readonly GizmoCapabilitiesTracker Tracker;

        public string Name => "GizmoNetworkTransport";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public GizmoNetworkTransportModule(
            GizmoExecutionController controller,
            GizmoUiStateHub uiHub,
            IGizmoNetworkFactory networkFactory,
            DebugPrimitiveBuffer gizmoBuffer,
            long localNodeId,
            FdpEventBus interactionBus)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _uiHub = uiHub ?? throw new ArgumentNullException(nameof(uiHub));
            if (networkFactory == null) throw new ArgumentNullException(nameof(networkFactory));

            if (networkFactory.Participant != null)
            {
                var writer = new DdsWriterGizmoAdapter<GizmoUiState>(networkFactory.Participant);
                _ddsUiPublisher = new DdsGizmoUiStatePublisher(writer);
                uiHub.AddEndpoint(_ddsUiPublisher);
            }

            _primitivePublisherSystem = networkFactory.CreateGizmoPublisherSystem(gizmoBuffer, localNodeId);
            _translators = networkFactory.CreateGizmoTranslators(interactionBus, localNodeId, headless: true);

            // Tracker drives AddListener/RemoveListener as remote terminals announce.
            // Does NOT call controller.AddListener() here — that is done per-terminal.
            Tracker = new GizmoCapabilitiesTracker(controller, interactionBus);
        }

        // Registers the primitive publisher system and any translator wrapper systems.
        // When Participant is null all collections are empty/null, so nothing is registered.
        public void RegisterSystems(ISystemRegistry registry)
        {
            if (_primitivePublisherSystem != null)
                registry.RegisterSystem(_primitivePublisherSystem);

            foreach (var translator in _translators)
            {
                if ((translator.Direction & TranslatorDirection.Ingress) != 0)
                    registry.RegisterSystem(new NetworkTranslatorIngressSystem(translator));
                if ((translator.Direction & TranslatorDirection.Egress) != 0)
                    registry.RegisterSystem(new NetworkTranslatorEgressSystem(translator));
            }
        }

        // Empty: all logic is driven by registered systems and the Tracker.
        public void Tick(ISimulationView view, float deltaTime) { }

        // Balances any still-connected terminals and removes the hub endpoint.
        public void Dispose()
        {
            Tracker.DrainAll();
            if (_ddsUiPublisher != null)
                _uiHub.RemoveEndpoint(_ddsUiPublisher);
        }

        // Wraps an ingress translator as an IEcsModuleSystem executed in the Input phase.
        [UpdateInPhase(SystemPhase.Input)]
        private sealed class NetworkTranslatorIngressSystem : IEcsModuleSystem
        {
            private readonly INetworkTranslator _translator;
            public NetworkTranslatorIngressSystem(INetworkTranslator translator) { _translator = translator; }
            public void Execute(ISimulationView view, float deltaTime)
                => _translator.PollIngress(view.GetCommandBuffer(), view);
        }

        // Wraps an egress translator as an IEcsModuleSystem executed in the PostSimulation phase.
        [UpdateInPhase(SystemPhase.PostSimulation)]
        private sealed class NetworkTranslatorEgressSystem : IEcsModuleSystem
        {
            private readonly INetworkTranslator _translator;
            public NetworkTranslatorEgressSystem(INetworkTranslator translator) { _translator = translator; }
            public void Execute(ISimulationView view, float deltaTime)
                => _translator.ScanAndPublish(view);
        }
    }
}
