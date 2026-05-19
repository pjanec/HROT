using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.SimHost.Diagnostics
{
    /// <summary>
    /// Helper that publishes a <see cref="PatchDebugStateCommand"/> to flip a single
    /// <see cref="BehaviorDebugFlags"/> bit on a target entity. Called from the
    /// AI-trace context-menu action handlers in <c>SimHostApp</c>.
    /// </summary>
    internal static class AiTraceContextMenu
    {
        /// <summary>
        /// Reads the current bit value and publishes a JSON patch that flips it.
        /// JSON property names come from <c>nameof(...)</c> so a rename of the
        /// underlying field or enum member fails at compile time.
        /// </summary>
        public static void PublishToggle(ISimulationView view, Entity target, BehaviorDebugFlags flag)
        {
            if (target == Entity.Null) return;
            if (view is not EntityRepository repo) return;

            // Only show/operate on entities with a brain.
            if (!repo.HasComponent<BehaviorState>(target)) return;

            bool current = false;
            if (repo.HasComponent<DebugState>(target))
                current = (repo.GetComponentRO<DebugState>(target).Behavior & flag) != 0;

            bool next = !current;
            string nextStr = next ? "true" : "false";
            // C# 11 raw string with $$ allows literal '{' / '}' and embedded interpolations.
            string patchJson = $$"""
            {
                "{{nameof(DebugState.Behavior)}}": {
                    "{{flag}}": {{nextStr}}
                }
            }
            """;

            repo.Bus.PublishManaged(new PatchDebugStateCommand
            {
                Target    = target,
                PatchJson = patchJson,
            });
        }
    }
}
