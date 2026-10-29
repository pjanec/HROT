using System;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.RunMode;
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
    private bool _popupOpen;

    /// <param name="choice">The owner host's run choice.</param>
    public LoadingScreen(RunModeChoice choice) => _choice = choice ?? throw new ArgumentNullException(nameof(choice));

    /// <summary>Registers it on <paramref name="bar"/> (every perspective); the status bar is drawn every frame, which is where the modal is driven from.</summary>
    public static LoadingScreen Register(StatusBarManager bar, RunModeChoice choice)
    {
        var screen = new LoadingScreen(choice);
        bar.RegisterSection(SectionId, 1, screen.Render);
        return screen;
    }

    /// <summary>The run choice it reads (the picked owner's).</summary>
    public RunModeChoice Choice => _choice;

    /// <summary>A load is in progress: the screen is up.</summary>
    public bool Visible => _choice.Gate.Loading;

    /// <summary>What the screen says: the load's progress (the cluster's, else this node's step).</summary>
    public string Text => _choice.Gate.Progress.Length > 0 ? $"Loading — {_choice.Gate.Progress}" : "Loading…";

    /// <summary>The Cancel button: the host's slave cancels the load.</summary>
    public void Cancel() => _choice.CancelLoad(CancelReason);

    /// <summary>Called each frame inside an active ImGui frame.</summary>
    public void Render()
    {
        if (!Visible) { _popupOpen = false; return; }
        if (!_popupOpen) { ImGui.OpenPopup(PopupId); _popupOpen = true; }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(PopupId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)) return;
        ImGui.TextWrapped(Text);
        ImGui.Separator();
        if (ImGui.Button("Cancel")) Cancel();
        ImGui.EndPopup();
    }
}
