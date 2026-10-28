namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐⭐ <c>CE-4096</c> (AQ108 D12) — what ▶ arms after a scenario is opened on this host: <b>Preview in the editor, Live on CGF, the all-in-one
/// and the server</b>. ONE parameter of the shared bootstrap (⛔ not an <c>if (host == …)</c> at the use sites). Defined and set here;
/// ⭐ <c>CE-4097</c> reads it — nothing behaves differently yet.
/// </summary>
internal enum HostArmedMode
{
    Preview,
    Live,
}

internal static class HostArmedModes
{
    /// <summary>The armed mode of a runner started with these <c>--mode</c> subsystems: the editor ⇒ Preview, anything else ⇒ Live.</summary>
    internal static HostArmedMode For(IEnumerable<string> requestedSubsystems)
        => requestedSubsystems.Any(s => string.Equals(s, "editor", StringComparison.OrdinalIgnoreCase)) ? HostArmedMode.Preview : HostArmedMode.Live;
}
