using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Components;

namespace Hrot.IG.Gizmos
{
    /// <summary>
    /// ImGui panel that exposes <see cref="GlobalDebugSettings"/> to the operator.
    /// Intended to be called from the IG debug overlay each frame when visible.
    /// </summary>
    public static class GlobalDebugSettingsPanel
    {
        /// <summary>
        /// Renders the GlobalDebugSettings collapsible ImGui section.
        /// Reads the current singleton, shows UI controls, and writes changes back
        /// via the command buffer when the operator modifies a value.
        /// </summary>
        /// <param name="view">Read-only ECS view for singleton access.</param>
        /// <param name="cmd">Command buffer to apply singleton mutations.</param>
        public static void Draw(ISimulationView view, IEntityCommandBuffer cmd)
        {
            // Stub: full ImGui integration requires access to Raylib/rlImGui context
            // which is only available on the render thread in Hrot.IG.
            // See BATCH-06 design notes for the planned implementation approach.
        }
    }
}
