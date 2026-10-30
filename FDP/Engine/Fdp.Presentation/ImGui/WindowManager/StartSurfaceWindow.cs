using System.Numerics;
using ImGuiNET;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6 / D16) — <b>the ONE start surface</b>: the only window that claims the <c>Start</c> perspective, so the
/// A0 rule holds and no other panel shows. ⛔ <b>It owns NO action</b>: in the Actions variant it draws the menu items marked allowed-in-Idle
/// (<see cref="GlobalMenuRegistry.AllowedInIdleItems"/>) as buttons and invokes the SAME bindings, so the surface and the menu cannot
/// disagree; otherwise it draws one waiting sentence.
/// </summary>
public sealed class StartSurfaceWindow : ManagedWindow
{
    /// <summary>The window id (a rail asserts exactly one window claims Start).</summary>
    public const string WindowId = "fdp_start_surface";

    public const string WaitingText = "Waiting for the server to load a scenario";
    public const string ServerLostText = "Server lost — waiting";
    public const string ActionsText = "Open a scenario or a recording to begin";

    /// <summary>One button: a menu leaf's label and the menu's own binding.</summary>
    public sealed record StartButton(string Label, bool Enabled, Action Invoke);

    /// <summary>⭐ What the window shows now — pure data, so a rail can read and "click" it without ImGui.</summary>
    public sealed record StartSurfaceModel(StartVariant Variant, string Text, IReadOnlyList<StartButton> Buttons);

    private readonly GlobalMenuRegistry _menu;
    private readonly Func<StartVariant> _variant;

    public StartSurfaceWindow(GlobalMenuRegistry menu, Func<StartVariant> variant)
        : base(WindowId, "Start", WindowManager.StartPerspectiveName, WindowScope.PerspectiveBound)
    {
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _variant = variant ?? throw new ArgumentNullException(nameof(variant));
        IsOpen = true;
        ShowInMenu = false;   // never a toggle in the Windows menu — it is the perspective, not a panel
    }

    /// <summary>The model: Actions = the allowed-in-Idle menu items (the shell's Help is always one), else one waiting sentence.</summary>
    public StartSurfaceModel Model()
    {
        var variant = _variant();
        if (variant == StartVariant.Actions)
            return new StartSurfaceModel(StartVariant.Actions, ActionsText,
                _menu.AllowedInIdleItems().Select(a => new StartButton(a.Label(), a.Enabled(), a.Invoke)).ToList());
        return new StartSurfaceModel(variant, variant == StartVariant.ServerLost ? ServerLostText : WaitingText, Array.Empty<StartButton>());
    }

    protected override ImGuiWindowFlags ExtraFlags =>
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar;

    /// <summary>Full-screen: the whole work area under the menu bar.</summary>
    protected override void BeforeBegin()
    {
        var vp = Gui.GetMainViewport();
        Gui.SetNextWindowPos(vp.WorkPos);
        Gui.SetNextWindowSize(vp.WorkSize);
    }

    protected override void DrawClientArea()
    {
        var model = Model();
        var avail = Gui.GetContentRegionAvail();
        const float buttonWidth = 320f;
        float blockHeight = Gui.GetTextLineHeightWithSpacing() * 2 + model.Buttons.Count * (Gui.GetFrameHeightWithSpacing() + 4f);
        Gui.SetCursorPosY(Gui.GetCursorPosY() + Math.Max(0f, (avail.Y - blockHeight) * 0.4f));

        CenteredText("HROT");
        CenteredText(model.Text);
        Gui.Spacing();
        foreach (var b in model.Buttons)
        {
            Gui.SetCursorPosX(Math.Max(0f, (avail.X - buttonWidth) * 0.5f));
            Gui.BeginDisabled(!b.Enabled);
            if (Gui.Button(b.Label, new Vector2(buttonWidth, 0f))) b.Invoke();
            Gui.EndDisabled();
            Gui.Spacing();
        }
    }

    private static void CenteredText(string text)
    {
        float w = Gui.CalcTextSize(text).X;
        Gui.SetCursorPosX(Math.Max(0f, (Gui.GetContentRegionAvail().X - w) * 0.5f));
        Gui.TextUnformatted(text);
    }
}
