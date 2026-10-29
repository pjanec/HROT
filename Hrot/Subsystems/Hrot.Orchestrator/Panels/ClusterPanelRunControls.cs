using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// ⭐⭐⭐ <c>CE-1439</c> (AQ108 §2.12, R-3066 audit #8) — <b>the verbs of <see cref="ClusterScenarioPanel"/>'s controls, routed through the host's ONE run surfaces</b>:
/// the time transport (<see cref="UserTransport"/>), the <see cref="RunModeChoice"/> and the scenario session. The panel used to publish its own pause / resume / step intents,
/// run its own <c>ScenarioLoadSequence</c>, build its own replay transition and its own checkpoint op — a second control surface with no run choice, no <i>Stop &amp; Load</i> guard
/// and no unsaved check. ⛔ The panel keeps being the MONITOR (state, nodes, transactions, inventory); what it ASKS for goes through here.
///
/// <para>⭐ Headless on purpose (the suite's rule: <i>pin the routing, not the rendering</i>): every panel button is one call on this type.</para>
/// </summary>
public sealed class ClusterPanelRunControls
{
    private readonly ClusterTimeTransportAdapter? _owned;   // set when the panel built these controls itself (ExCon, which hosts no run surfaces of its own)
    private readonly EditorScenarioSession?       _ownedSession;

    /// <summary>The host's transport (the one the toolbar's ▶ drives).</summary>
    public ITimeTransportFacade Transport { get; }

    /// <summary>The host's run choice.</summary>
    public RunModeChoice Choice { get; }

    /// <summary>The host's scenario session.</summary>
    public IScenarioSession Session { get; }

    /// <param name="transport">The host's transport.</param>
    /// <param name="choice">The host's run choice — the guarded open, the checkpoint and the unsaved check live there.</param>
    /// <param name="session">The host's scenario session.</param>
    public ClusterPanelRunControls(ITimeTransportFacade transport, RunModeChoice choice, IScenarioSession session)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Choice    = choice    ?? throw new ArgumentNullException(nameof(choice));
        Session   = session   ?? throw new ArgumentNullException(nameof(session));
    }

    private ClusterPanelRunControls(ClusterTimeTransportAdapter adapter, EditorScenarioSession session)
        : this(adapter, adapter.RunChoice!, session)
    {
        _owned = adapter;
        _ownedSession = session;
    }

    /// <summary>
    /// The controls over the panel's own bus, for a host that composes no run surfaces of its own (ExCon). ⭐ The SAME shared classes every host uses
    /// (<see cref="ClusterTimeTransportAdapter"/>, <see cref="RunModeChoice"/>, a world-less <see cref="EditorScenarioSession"/>) — ⛔ not a private copy of their logic. The panel then advances them
    /// (<see cref="Update"/>); a host that passes its own does that itself.
    /// </summary>
    public static ClusterPanelRunControls OverBus(FdpEventBus bus, Func<ClusterView?> view, Func<double>? simTime = null)
    {
        var adapter = new ClusterTimeTransportAdapter(bus, view, simTime);
        var session = new EditorScenarioSession(bus, view, () => OrchestrationConstants.GetSharedScenariosRoot());
        adapter.AttachRunChoice(RunArmedDefaults.For(RunHostKind.Other), session.RunControl);
        return new ClusterPanelRunControls(adapter, session);
    }

    /// <summary>Once per frame, ONLY for controls the panel built itself; a no-op for a host's (it advances its own adapter and session).</summary>
    public void Update()
    {
        _owned?.Update();
        _ownedSession?.Update();
    }

    // ── ▶ ⏸ Step · speed ─────────────────────────────────────────────────────────────

    /// <summary>The Pause / Resume button is enabled: the facade says so and — from Edit — the armed mode can run here.</summary>
    public bool CanPlayPause => Transport.IsUserPlayEnabled(Choice);

    /// <summary>The Step button is enabled (same rule).</summary>
    public bool CanStep => Transport.IsUserStepEnabled(Choice);

    /// <summary>Pause / Resume — the USER's ▶ (<see cref="UserTransport.UserPlayPause"/>): from Edit the armed mode's start (Live: the unsaved check, then the load), elsewhere the facade's toggle.</summary>
    public void PauseOrResume() => Transport.UserPlayPause(Choice);

    /// <summary>Step by <paramref name="seconds"/> — the USER's Step (<see cref="UserTransport.UserStepBy"/>).</summary>
    public void Step(float seconds) => Transport.UserStepBy(seconds, Choice);

    /// <summary>The speed slider — the facade's one <c>SetTimeScale</c> (what <c>POST /sim/timescale</c> calls).</summary>
    public void SetTimeScale(float scale) => Transport.SetTimeScale(scale);

    // ── scenario / recording ─────────────────────────────────────────────────────────

    /// <summary><i>Load into Edit</i> — the run choice's GUARDED open (<c>Stop &amp; Load</c> over a run; <i>Save / Discard / Cancel</i> over unsaved edits): the very call <c>File › Open Scenario</c> ends in.</summary>
    public void LoadIntoEdit(string scenarioName) => Choice.OpenScenario(scenarioName);

    /// <summary><i>Load into Live</i> — the session's live load, through its ONE guarded door (⭐ CE-4101: <i>Stop &amp; Load</i> over a run, <i>Save / Discard / Cancel</i> over unsaved edits — it asked neither before; the explicit HTTP <c>/scenario/load/live</c> is D22's own).</summary>
    public void LoadIntoLive(string scenarioName) => Session.LoadForLive(scenarioName);

    /// <summary><i>Open AAR</i> — the run choice's recording open (the guard and the unsaved check, then the session's ONE replay intent): what <c>File › Open Recording</c> ends in.</summary>
    public void OpenAar(Guid exerciseId) => Choice.OpenRecording(exerciseId);

    /// <summary>Why <i>Take Checkpoint</i> cannot run now (null = it can) — the choice's own answer, the same one the timeline's menu reads.</summary>
    public string? CheckpointDisabledReason => Choice.CheckpointDisabledReason;

    /// <summary><i>Take Checkpoint</i> — the choice's checkpoint (the session's <c>TakeCheckpoint</c> plus the toast).</summary>
    public void TakeCheckpoint() => Choice.TakeCheckpoint();
}
