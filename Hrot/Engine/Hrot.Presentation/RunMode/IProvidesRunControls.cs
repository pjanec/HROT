using System.Collections.Generic;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-1437</c> (AQ108 §2.10) — a host subsystem that builds its own time transport and run choice and OFFERS them to the process's one toolbar time control.
/// ⛔ The host does not register the toolbar itself: the window composer (<c>LocalWindowController.ComposeRunControls</c>) picks ONE offer by rule and registers
/// it once. Each host still builds its OWN adapter — every node drives its own bus; only the toolbar surface is shared.
/// </summary>
public interface IProvidesRunControls
{
    /// <summary>What kind of host this is — the pick rule's tie-break (the editor wins among several operators) and the key of <see cref="HostGatePolicies"/>.</summary>
    RunHostKind HostKind { get; }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> (R-3072, AQ110 D) — <b>what this host's controls wait on</b>: the policy the host COMPOSES (⭐ CE-4149: the Player host returns the policy of its <see cref="NodeRunProfile"/>, <see cref="HostGatePolicy.Player"/>),
    /// else the table's default for its <see cref="HostKind"/> (<see cref="HostGatePolicies.For"/>). Shared code reads this value, never <see cref="HostKind"/>.
    /// The host's gate is <c>ClusterView.GateOpen(GatePolicy)</c>.
    /// </summary>
    Fdp.Toolkit.Orchestration.HostGatePolicy GatePolicy => HostGatePolicies.For(HostKind);

    /// <summary>⭐ CE-4330 ui half — this station's NAME on the cluster (the status bar marks a transfer it asked for <i>(yours)</i>; its asks carry it as <c>AskedBy</c>). Default: the host kind; the editor answers its node name.</summary>
    string StationName => HostKind.ToString();

    /// <summary>The host's transport; <see langword="null"/> while it has none (before its bus exists / headless) — the host then offers nothing.</summary>
    ITimeTransportFacade? Transport { get; }

    /// <summary>
    /// ⭐ <c>CE-1445</c> (R-1044) — the host's role EXPLICITLY suppresses the toolbar time control (the Player issues no ticks, Q54/D4): the composer registers none over its transport. Default <see langword="false"/> — an ECS host has it
    /// by construction. It is <see cref="NodeRunProfile.SuppressTimeControl"/> of the profile the host built its controls from.
    /// </summary>
    bool SuppressTimeControl => false;

    /// <summary>The host's run choice; <see langword="null"/> until built. An <i>operator</i> host is one whose choice <see cref="RunModeChoice.HasScenarioControl"/>.</summary>
    RunModeChoice? RunChoice { get; }

    /// <summary>⭐ W-4 (LC-10) — the host's <i>Run to …</i> (the editor's; null on a host with none): the composer hands it to the ONE toolbar time control.</summary>
    RunToController? RunTo => null;

    /// <summary>
    /// ⭐⭐ <c>CE-1439</c> (AQ108 §2.12) — called by the composer on the PICKED owner only (after <c>ComposeRunControls</c> registered its toolbar): a host that does NOT register
    /// the scenario menu items (<c>File › Open Scenario / New Scenario / Open Recording</c>, which the Start surface draws) in its own <c>RegisterWindows</c> registers them here.
    /// ⭐ <c>CE-4101</c> follow-up: the editor and CGF register theirs here too (built in their <c>RegisterWindows</c>, where their pickers and Save-As live; run from here) — ⛔ no host calls the registrar itself. Default: nothing (SimHost has no scenario session). ⭐ The point: the process's ONE scenario menu belongs to the host that owns
    /// ▶, picked by the same rule, never to whichever host registered last.
    /// </summary>
    void ComposeScenarioActions(Fdp.Presentation.WindowManager.WindowManager windowManager) { }

    /// <summary>
    /// ⭐ CE-4141 / AQ108 D24 — the running scenario's run setup as menu lines (<see cref="RunSetupText.Of(Fdp.Core.EntityRepository?)"/>: the actual variant — <i>as authored</i> when there is none — and the params that differ from their defaults), read from what the
    /// host's world holds resident. Drawn by the mode indicator's menu. Default: none (a host with no scenario world); ⭐ a host that HAS one (the Editor, CGF) overrides it — the forwarding rail checks them.
    /// </summary>
    IReadOnlyList<string> RunSetupLines => System.Array.Empty<string>();

    /// <summary>
    /// ⭐ Player Join D-4 — the world this host's gameplay client (<c>DeployFlowSystem.Of(world)</c>) lives in, for the process's ONE deploy screen. Default: none; the Player host (IG) overrides it.
    /// </summary>
    Fdp.Core.EntityRepository? World => null;
}

