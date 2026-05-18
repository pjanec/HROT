using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Hub;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Modules
{
    // Installable IEcsModule that bridges a local Raylib/ImGui window to the backend
    // gizmo pipeline. Creates a LocalGizmoUiStateTransport, registers it with the hub,
    // and increments the execution controller listener count.
    //
    // Design: DESIGN.md §4.1
    public sealed class LocalTerminalModule : IEcsModule, IDisposable
    {
        private readonly GizmoExecutionController _controller;
        private readonly GizmoUiStateHub _uiHub;
        private readonly LocalGizmoUiStateTransport _localUiTransport;

        public string Name => "LocalTerminal";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        // Exposes the transport for the local render loop to call PollAndApply on.
        public LocalGizmoUiStateTransport LocalUiTransport => _localUiTransport;

        public LocalTerminalModule(GizmoExecutionController controller, GizmoUiStateHub uiHub)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _uiHub = uiHub ?? throw new ArgumentNullException(nameof(uiHub));
            _localUiTransport = new LocalGizmoUiStateTransport();
            uiHub.AddEndpoint(_localUiTransport);
            controller.AddListener();
        }

        // Empty: the local terminal reads the DebugPrimitiveBuffer directly (zero-copy).
        public void RegisterSystems(ISystemRegistry registry) { }

        // Empty: all logic is driven by the local render loop, not the ECS tick.
        public void Tick(ISimulationView view, float deltaTime) { }

        // Removes the transport from the hub and decrements the controller listener count.
        public void Dispose()
        {
            _uiHub.RemoveEndpoint(_localUiTransport);
            _controller.RemoveListener();
        }
    }
}
