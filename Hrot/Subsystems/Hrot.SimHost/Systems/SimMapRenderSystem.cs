using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Renders the Sim Map (2-D tactical overlay) presentation canvas when the active
    /// perspective name is <c>"Sim"</c>.
    ///
    /// <para><b>Testability:</b>
    /// In production the system calls <see cref="MapCanvas.Draw"/>. In unit tests
    /// a <c>null</c> canvas can be provided; the system still increments
    /// <see cref="DrawCallCount"/> so tests can assert the condition check without
    /// triggering Raylib graphics calls.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Export)]
    public sealed class SimMapRenderSystem : IEcsModuleSystem
    {
        private readonly MapCanvas? _canvas;

        /// <summary>Number of frames on which drawing was permitted (for unit-test assertions).</summary>
        public int DrawCallCount { get; private set; }

        /// <param name="canvas">
        ///   The <see cref="MapCanvas"/> to call <c>Draw()</c> on when the perspective is
        ///   <c>"Sim"</c>. Pass <c>null</c> in headless/test contexts.
        /// </param>
        public SimMapRenderSystem(MapCanvas? canvas = null)
        {
            _canvas = canvas;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // Guard: only render when this is the active perspective.
            if (!((EntityRepository)view).HasSingletonManaged<Hrot.Common.ActivePerspective>()) return;

            var perspective = ((EntityRepository)view).GetSingletonManaged<Hrot.Common.ActivePerspective>();
            if (perspective?.Name != "Sim") return;

            DrawCallCount++;
            _canvas?.Draw();
        }
    }
}
