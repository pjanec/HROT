using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-1437</c> (AQ108 §2.10) — a host subsystem that builds its own time transport and run choice and OFFERS them to the process's one toolbar time control.
/// ⛔ The host does not register the toolbar itself: the window composer (<c>LocalWindowController.ComposeRunControls</c>) picks ONE offer by rule and registers
/// it once. Each host still builds its OWN adapter — every node drives its own bus; only the toolbar surface is shared.
/// </summary>
public interface IProvidesRunControls
{
    /// <summary>What kind of host this is — the pick rule's tie-break (the editor wins among several operators) and the key of <see cref="RunArmedDefaults"/>.</summary>
    RunHostKind HostKind { get; }

    /// <summary>The host's transport; <see langword="null"/> while it has none (before its bus exists / headless) — the host then offers nothing.</summary>
    ITimeTransportFacade? Transport { get; }

    /// <summary>The host's run choice; <see langword="null"/> until built. An <i>operator</i> host is one whose choice <see cref="RunModeChoice.HasScenarioControl"/>.</summary>
    RunModeChoice? RunChoice { get; }

    /// <summary>
    /// ⭐⭐ <c>CE-1439</c> (AQ108 §2.12) — called by the composer on the PICKED owner only (after <c>ComposeRunControls</c> registered its toolbar): a host that does NOT register
    /// the scenario menu items (<c>File › Open Scenario / New Scenario / Open Recording</c>, which the Start surface draws) in its own <c>RegisterWindows</c> registers them here.
    /// ⛔ Default: nothing — the editor and CGF register theirs themselves (a follow-up folds those two here). ⭐ The point: the process's ONE scenario menu belongs to the host that owns
    /// ▶, picked by the same rule, never to whichever host registered last.
    /// </summary>
    void ComposeScenarioActions(Fdp.Presentation.WindowManager.WindowManager windowManager) { }
}
