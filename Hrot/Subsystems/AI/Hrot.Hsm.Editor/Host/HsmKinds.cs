namespace Hrot.Hsm.Editor.Host;

internal static class HsmKinds
{
    public const string Simple      = "hsm.state.simple";
    public const string Composite   = "hsm.state.composite";
    public const string Parallel    = "hsm.state.parallel";
    public const string Final       = "hsm.state.final";
    public const string History     = "hsm.state.history";
    public const string DeepHistory = "hsm.state.deepHistory";
    // Shared marker used by HsmEditorTheme to detect pseudo-state nodes
    // that should have a transparent node body.
    public const string Pseudostate = "hsm.pseudostate";
}
