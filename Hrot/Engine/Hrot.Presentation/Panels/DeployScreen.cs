using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Hrot.Gameplay;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ Player Join §9.2 / D-4, D-7, D-13 — <b>the deploy screen</b>: a thin ImGui overlay over <see cref="DeployFlowSystem"/> (all the logic is there; this only DRAWS its state and
/// calls <see cref="DeployFlowSystem.Request"/> / <see cref="DeployFlowSystem.Observe"/>). A status-bar section registered ONCE per process beside <see cref="LoadingScreen"/>
/// (<c>LocalWindowController.ComposeRunControls</c>, the Player policy) — the status bar is drawn every frame, which is where it is driven from.
/// <para>Shows: the Waiting reason (<i>Waiting for the server to load a scenario</i>) · Observing (<i>no player server — observing</i>, a button to keep observing) · Choosing: a side list, then that
/// side's offers (a taken one greyed, <i>held by station N</i>), an Observer button and the last refusal · a spinner line while Requesting / TakingControl · nothing while Playing.
/// ⛔ Hidden while the loading screen is up (the gate is closed — it speaks then).</para>
/// </summary>
public sealed class DeployScreen
{
    public const string SectionId = "deploy_screen";

    private readonly Func<DeployFlowSystem?> _flow;
    private readonly Func<bool> _loadingVisible;
    private bool _keepObserving;
    private ForceId? _side;

    /// <param name="flow">The node's flow (<c>DeployFlowSystem.Of(world)</c>); null ⇒ nothing to show.</param>
    /// <param name="loadingVisible">Whether the loading screen is up — this screen yields to it.</param>
    public DeployScreen(Func<DeployFlowSystem?> flow, Func<bool> loadingVisible)
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _loadingVisible = loadingVisible ?? throw new ArgumentNullException(nameof(loadingVisible));
    }

    /// <summary>Registers it on <paramref name="bar"/>.</summary>
    public static DeployScreen Register(StatusBarManager bar, Func<DeployFlowSystem?> flow, Func<bool> loadingVisible)
    {
        var screen = new DeployScreen(flow, loadingVisible);
        bar.RegisterSection(SectionId, 2, screen.Render);
        return screen;
    }

    /// <summary>The side whose offers are listed (null until the Player picks; the only side with offers is picked for it).</summary>
    public ForceId? SelectedSide => _side;

    /// <summary>Picks the side whose offers are shown.</summary>
    public void SelectSide(ForceId side) => _side = side;

    /// <summary>Whether the screen draws this frame — the rule the rails read.</summary>
    public bool Visible
    {
        get
        {
            if (_flow() is not { } f || _loadingVisible()) return false;
            return f.State switch
            {
                DeployFlowState.Playing => false,
                DeployFlowState.Waiting => f.Reason.Length > 0,
                DeployFlowState.Observing => !_keepObserving || f.ServerExists,
                DeployFlowState.Dead => f.RespawnAtSim > 0.0 || !_keepObserving,   // a countdown always shows; "killed — observing" until the Player keeps observing
                _ => true,
            };
        }
    }

    /// <summary>The headline the screen says for the flow's state.</summary>
    public string Headline => _flow() is not { } f ? "" : f.State switch
    {
        DeployFlowState.Waiting => f.Reason,
        DeployFlowState.Observing => f.ServerExists ? "Observing" : "No player server — observing",
        DeployFlowState.Choosing => "Deploy",
        DeployFlowState.Requesting => "Asking the server…",
        DeployFlowState.TakingControl => "Taking control…",
        DeployFlowState.Dead => f.Reason,   // "killed — respawn in N s" / "killed — observing"
        _ => "",
    };

    /// <summary>The sides the offers span, in a stable order.</summary>
    public static IReadOnlyList<ForceId> SidesOf(IReadOnlyList<DeployOffer> offers)
    {
        var sides = new List<ForceId>();
        foreach (var o in offers) if (!sides.Contains(o.Side)) sides.Add(o.Side);
        sides.Sort();
        return sides;
    }

    /// <summary>The offers of <paramref name="side"/>.</summary>
    public static IReadOnlyList<DeployOffer> OffersOf(IReadOnlyList<DeployOffer> offers, ForceId side)
    {
        var list = new List<DeployOffer>();
        foreach (var o in offers) if (o.Side == side) list.Add(o);
        return list;
    }

    /// <summary>One offer's row text: a placed unit by its name; a role with its live count — <i>Rifleman  2 / 4</i> (<i>2</i> when unlimited).</summary>
    public static string RowText(DeployOffer o)
    {
        string name = o.SlotName.Length > 0 ? o.SlotName : $"#{o.UnitNetId}";
        return !o.IsRole ? name : o.Max > 0 ? $"{name}  {o.Count} / {o.Max}" : $"{name}  {o.Count}";
    }

    /// <summary>Called each frame inside an active ImGui frame.</summary>
    public void Render()
    {
        if (!Visible) return;
        var f = _flow()!;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(vp.Pos.X + vp.Size.X / 2f, vp.Pos.Y + vp.Size.Y * 0.382f), ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0f));
        ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(320f, 0f), new System.Numerics.Vector2(640f, vp.Size.Y * 0.7f));
        if (!ImGui.Begin("Deploy###mt_deploy_screen", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        { ImGui.End(); return; }

        ImGui.TextUnformatted(Headline);
        switch (f.State)
        {
            case DeployFlowState.Observing when !f.ServerExists:
                if (ImGui.Button("Keep observing")) _keepObserving = true;
                break;
            case DeployFlowState.Observing:
                if (ImGui.Button("Choose a unit")) f.StopObserving();
                break;
            case DeployFlowState.Choosing:
                DrawChoosing(f);
                break;
            case DeployFlowState.Dead when f.RespawnAtSim <= 0.0:
                if (ImGui.Button("Keep observing")) _keepObserving = true;
                break;
            case DeployFlowState.Requesting:
            case DeployFlowState.TakingControl:
                ImGui.TextDisabled("Please wait…");
                break;
        }
        ImGui.End();
    }

    private void DrawChoosing(DeployFlowSystem f)
    {
        var offers = f.Offers;
        var sides = SidesOf(offers);
        if (_side is not { } side || !sides.Contains(side)) side = sides.Count > 0 ? sides[0] : ForceId.Friend;
        _side = side;
        foreach (var s in sides)
        {
            if (ImGui.RadioButton(s.ToString(), _side == s)) _side = s;
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Separator();
        var mine = OffersOf(offers, _side!.Value);
        if (mine.Count == 0) ImGui.TextDisabled("No unit to take on this side.");
        foreach (var o in mine)
        {
            string name = RowText(o);
            bool full = o.IsRole && o.Max > 0 && o.Count >= o.Max;
            if (o.HolderStation != 0 || full)
            {
                ImGui.BeginDisabled();
                ImGui.Selectable(full ? $"{name}  —  full##u{o.UnitNetId}" : $"{name}  —  held by station {o.HolderStation}##u{o.UnitNetId}");
                ImGui.EndDisabled();
            }
            else if (ImGui.Selectable($"{name}##u{o.UnitNetId}")) f.Request(_side!.Value, o.UnitNetId);
        }
        ImGui.Separator();
        if (ImGui.Button("Observer")) f.Observe(_side!.Value);
        if (f.LastRefusal.Length > 0) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.45f, 0.35f, 1f), f.LastRefusal);
    }
}
