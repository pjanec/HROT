namespace Hrot.IG.Components
{
    /// <summary>A single action entry displayed in a context menu.</summary>
    public sealed class ContextAction
    {
        /// <summary>Human-readable label shown in the menu row.</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>
        /// Internal action identifier.
        /// Names prefixed with <c>"IG_"</c> are handled locally by the IG application
        /// (e.g. <c>"IG_Lock_Camera"</c>, <c>"IG_Center"</c>); all other names are
        /// forwarded to ExCon as a <c>ContextActionTriggered</c> managed event.
        /// </summary>
        public string ActionName { get; init; } = string.Empty;
    }
}
