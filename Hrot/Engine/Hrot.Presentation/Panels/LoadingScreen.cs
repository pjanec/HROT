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

    /// <summary>⭐ <c>CE-4115</c> — the veil's ONE alpha: black over the whole display, so the world stays barely visible under the splash.</summary>
    public const float VeilAlpha = 0.65f;

    /// <summary>⭐ <c>CE-4115</c> — where the text block's centre sits, as a fraction of the display height from the top (the golden section, 1 - 1/φ).</summary>
    public const float GoldenLine = 0.382f;

    /// <summary>⭐ <c>CE-4115</c> — a rail surface: the text size, the text block's vertical centre (px) and the Cancel button's top (px) of the last drawn frame; <see langword="null"/> before any.</summary>
    public (float TextSize, float BlockCenterY, float? CancelTop)? LastLayout { get; private set; }

    /// <summary>
    /// ⭐ <c>CE-4115</c> — called each frame inside an active ImGui frame. A FULL-DISPLAY modal (so it is on top and swallows every click but its own Cancel), its background replaced by ONE dark veil
    /// (<see cref="VeilAlpha"/>); the text is big — sized by the film subtitles' rule (<c>SubtitleBand.TextSizeFor</c>) — one line per
    /// '\n', each centred, the block centred on <see cref="GoldenLine"/>; Cancel (operator only) is a modest button under the block. ⛔ The layout never follows the text's width: nothing wraps, nothing resizes.
    /// </summary>
    public void Render()
    {
        if (!Visible) { _popupOpen = false; LastLayout = null; return; }
        if (!_popupOpen) { ImGui.OpenPopup(PopupId); _popupOpen = true; }

        var vp = ImGui.GetMainViewport();
        var size = vp.Size;
        ImGui.SetNextWindowPos(vp.Pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, 0u);   // the veil is ours (one alpha), not ImGui's own dim
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        bool open = ImGui.BeginPopupModal(PopupId, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
        if (!open) return;

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(vp.Pos, vp.Pos + size, (uint)Math.Round(VeilAlpha * 255f) << 24);

        float s = Hrot.UI.Common.Map3D.Director.SubtitleBand.TextSizeFor(size.Y);   // ONE size rule: the film subtitles'
        var font = ImGui.GetFont();
        var lines = Text.Split('\n');
        float step = s * 1.4f;
        float block = (lines.Length - 1) * step + s;
        float y = vp.Pos.Y + size.Y * GoldenLine - block / 2f;
        for (int i = 0; i < lines.Length; i++)
        {
            float w = font.CalcTextSizeA(s, float.MaxValue, 0f, lines[i]).X;
            dl.AddText(font, s, new System.Numerics.Vector2(vp.Pos.X + (size.X - w) / 2f, y + i * step), i == 0 ? 0xFFFFFFFFu : 0xFFC8C8C8u, lines[i]);
        }

        float? cancelTop = null;
        if (CanCancel)
        {
            const string label = "Cancel";
            float bw = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 4f;
            cancelTop = y + block + s * 0.8f;
            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(vp.Pos.X + (size.X - bw) / 2f, cancelTop.Value));
            if (ImGui.Button(label, new System.Numerics.Vector2(bw, 0))) Cancel();
        }
        LastLayout = (s, y + block / 2f - vp.Pos.Y, cancelTop is { } t ? t - vp.Pos.Y : null);
        ImGui.EndPopup();
    }
}
