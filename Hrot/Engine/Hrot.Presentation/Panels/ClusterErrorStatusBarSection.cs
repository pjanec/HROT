using System;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.RunMode;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ <c>CE-1442</c> (U1, <c>DESIGN_Cluster_Load_Phase.md</c> §11.7 ④; R-3073) — <b>the red error indicator</b>: while the run gate says the cluster is in its ERROR state, the status bar shows the master's reason in red;
/// only Close and Exit work (every other control is closed by the gate). Composed ONCE per process beside the toolbar by <c>LocalWindowController.ComposeRunControls</c>. ⛔ It reads only the choice's gate.
/// </summary>
public sealed class ClusterErrorStatusBarSection
{
    /// <summary>The status-bar section id.</summary>
    public const string SectionId = "cluster_error";

    /// <summary>Left of the run note (60).</summary>
    public const int SortOrder = 55;

    private static readonly Vector4 Red = new(1f, 0.3f, 0.3f, 1f);

    private readonly RunModeChoice _choice;

    /// <param name="choice">The owner host's run choice.</param>
    public ClusterErrorStatusBarSection(RunModeChoice choice) => _choice = choice ?? throw new ArgumentNullException(nameof(choice));

    /// <summary>Registers it on <paramref name="bar"/> for every perspective (an error must not hide in another one).</summary>
    public static ClusterErrorStatusBarSection Register(StatusBarManager bar, RunModeChoice choice)
    {
        var section = new ClusterErrorStatusBarSection(choice);
        bar.RegisterSection(SectionId, SortOrder, section.Render);
        return section;
    }

    /// <summary>The run choice it reads (the picked owner's).</summary>
    public RunModeChoice Choice => _choice;

    /// <summary>The red text now, or <see langword="null"/> when the cluster is not in error.</summary>
    public string? TextNow() => _choice.Gate is { InError: true } g ? $"Cluster error — {g.ErrorReason}" : null;

    /// <summary>Called each frame inside an active ImGui frame and the status-bar window.</summary>
    public void Render()
    {
        if (TextNow() is { } text) ImGui.TextColored(Red, text);
    }
}
