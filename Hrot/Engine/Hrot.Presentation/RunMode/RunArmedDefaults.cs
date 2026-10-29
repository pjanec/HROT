namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ <c>CE-1437</c> (AQ108 §2.10) — the kind of host that builds a run choice. The three subsystems that do each name themselves once; the armed default
/// is looked up from <see cref="RunArmedDefaults"/>, never written as a literal at the use site.
/// </summary>
public enum RunHostKind
{
    /// <summary>The scenario editor.</summary>
    Editor,
    /// <summary>The CGF (scenario / operator) node.</summary>
    Cgf,
    /// <summary>The SimHost node.</summary>
    SimHost,
    /// <summary>Any other host (the all-in-one's other subsystems, the server, a test host).</summary>
    Other,
}

/// <summary>
/// ⭐⭐⭐ <c>CE-1437</c> (AQ108 §2.10 ②, D12) — <b>the ONE armed-default table</b>: what ▶ arms after a scenario is opened — Preview in the editor, Live on every
/// other host (CGF, SimHost, the all-in-one, the server). ⛔ Replaces the runner's <c>HostArmedMode(s)</c> (read by nothing) and the three literals at the
/// <c>AttachRunChoice</c> call sites. Lives here so the subsystems and the runner read the same table.
/// </summary>
public static class RunArmedDefaults
{
    /// <summary>The armed default of a host of this kind: <see cref="RunHostKind.Editor"/> ⇒ Preview, every other kind ⇒ Live.</summary>
    public static RunArmed For(RunHostKind hostKind) => hostKind == RunHostKind.Editor ? RunArmed.Preview : RunArmed.Live;
}
