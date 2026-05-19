using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Managed command event used by UI/context-menu actions to mutate the
    /// per-entity <see cref="DebugState"/> via a compact JSON patch payload.
    /// Drained by <c>DebugStatePatchSystem</c> during <c>SystemPhase.Input</c>.
    /// </summary>
    /// <example>
    /// Patch JSON shape (top-level property = field name on DebugState):
    /// <code>
    /// { "Behavior": { "EnableTraceBuffer": true } }
    /// </code>
    /// </example>
    public sealed class PatchDebugStateCommand
    {
        public Entity Target;
        public string PatchJson = string.Empty;
    }
}
