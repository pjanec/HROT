using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.IG.Components;

namespace Hrot.Presentation.Systems
{
    /// <summary>
    /// Evaluates domain rules and writes the canvas context-menu JSON into
    /// <see cref="CanvasContextMenuState"/> each frame.
    ///
    /// <para>The JSON is built once and cached; it is only rewritten when the
    /// relevant state hash changes. <c>CanvasContextMenuGizmo</c> reads the
    /// singleton and projects a <c>ContextMenuBinding</c> meta-primitive into
    /// the gizmo buffer keyed by anchor <c>-1L</c>.</para>
    ///
    /// <para>For the initial cut the canvas menu contains a single item:
    /// Measurement Tool (action ID 200 = <c>GlobalActionIds.Measure</c>).
    /// Subsystem-specific variants can override by registering a different
    /// implementation without changing the architecture.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class CanvasMenuUpdateSystem : IEcsModuleSystem
    {
        // Pre-serialized JSON: [{"id":200,"label":"Measurement Tool"}]
        // Action ID 200 matches GlobalActionIds.Measure.
        private const string CanvasMenuJson = "[{\"id\":200,\"label\":\"Measurement Tool\"}]";

        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;
            repo.SetSingletonManaged(new CanvasContextMenuState { MenuJson = CanvasMenuJson });
        }
    }
}
