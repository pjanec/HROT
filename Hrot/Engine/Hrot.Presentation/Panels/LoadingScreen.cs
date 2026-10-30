using System;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.RunMode;
using Fdp.Toolkit.Orchestration;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ <c>CE-1442</c> (U1, <c>DESIGN_Cluster_Load_Phase.md</c> §11.7 ④) — <b>the loading screen</b>: a modal shown while the run gate says a load is in progress (<see cref="RunGate.Loading"/>), with what the cluster is doing
/// and a Cancel that asks the host's slave to cancel the load (<see cref="RunModeChoice.CancelLoad"/> — the load fails everywhere and the cluster returns to Idle). Composed ONCE per process, beside the toolbar, by
/// <c>LocalWindowController.ComposeRunControls</c> over the picked owner's choice. ⛔ It reads only the choice's gate — never the cluster view.
/// </summary>
public sealed class LoadingScreen
{
    /// <summary>The status-bar section id it draws from (a duplicate registration replaces, never doubles).</summary>
    public const string SectionId = "loading_screen";

    /// <summary>The reason Cancel passes to the slave.</summary>
    public const string CancelReason = "cancelled by the user";

    private const string PopupId = "Loading###mt_loading_screen";

    private readonly RunModeChoice _choice;
    private readonly HostGatePolicy _policy;
    private bool _popupOpen;

    /// <param name="choice">The owner host's run choice.</param>
    /// <param name="policy">⭐ <c>CE-1445</c> (R-1044) — the owner's gate policy. <see cref="HostGatePolicy.Operator"/> (default): up only while a load is in progress, with Cancel.
    /// <see cref="HostGatePolicy.Player"/>: the player's WAITING screen — up whenever its gate is closed (lobby, catching up, loading, not in sync, error), no Cancel (it does not own the load).</param>
    public LoadingScreen(RunModeChoice choice, HostGatePolicy policy = HostGatePolicy.Operator)
    {
        _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        _policy = policy;
        Composed.AddOrUpdate(_choice, this);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RunModeChoice, LoadingScreen> Composed = new();

    /// <summary>⭐ CE-1451 — the loading screen composed over <paramref name="choice"/>, or <see langword="null"/> when none is (the debug API's <c>GET /status</c> reports what it says).</summary>
    public static LoadingScreen? Of(RunModeChoice? choice) => choice is not null && Composed.TryGetValue(choice, out var s) ? s : null;

    /// <summary>Registers it on <paramref name="bar"/> (every perspective); the status bar is drawn every frame, which is where the modal is driven from.</summary>
    public static LoadingScreen Register(StatusBarManager bar, RunModeChoice choice, HostGatePolicy policy = HostGatePolicy.Operator)
    {
        var screen = new LoadingScreen(choice, policy);
        bar.RegisterSection(SectionId, 1, screen.Render);
        return screen;
    }

    /// <summary>The run choice it reads (the picked owner's).</summary>
    public RunModeChoice Choice => _choice;

    /// <summary>The player's waiting screen (the owner's policy is <see cref="HostGatePolicy.Player"/>).</summary>
    public bool IsPlayer => _policy == HostGatePolicy.Player;

    /// <summary>Operator: a load is in progress. Player: the gate is closed for any reason (⭐ CE-1445) — a player parked in the lobby sees it too.</summary>
    public bool Visible => IsPlayer ? !_choice.Gate.Open : _choice.Gate.Loading;

    /// <summary>
    /// What the screen says: the load's progress (the cluster's, else this node's step). The player's, while not loading, is <i>Waiting for the scenario…</i> with the gate's reason on a second line.
    /// </summary>
    public string Text
    {
        get
        {
            var gate = _choice.Gate;
            if (gate.Loading || !IsPlayer) return gate.Progress.Length > 0 ? $"Loading — {gate.Progress}" : "Loading…";
            return gate.Reason is { Length: > 0 } reason ? $"Waiting for the scenario…\n{reason}" : "Waiting for the scenario…";
        }
    }

    /// <summary>⭐ CE-1445 — whether the screen offers Cancel: an operator's does (it owns the load); a player's never.</summary>
    public bool CanCancel => !IsPlayer;

    /// <summary>The Cancel button: the host's slave cancels the load. A player's screen has none — this does nothing there.</summary>
    public void Cancel()
    {
        if (CanCancel) _choice.CancelLoad(CancelReason);
    }

    /// <summary>Called each frame inside an active ImGui frame.</summary>
    public void Render()
    {
        if (!Visible) { _popupOpen = false; return; }
        if (!_popupOpen) { ImGui.OpenPopup(PopupId); _popupOpen = true; }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(PopupId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)) return;
        ImGui.TextWrapped(Text);
        if (CanCancel)
        {
            ImGui.Separator();
            if (ImGui.Button("Cancel")) Cancel();
        }
        ImGui.EndPopup();
    }
}
