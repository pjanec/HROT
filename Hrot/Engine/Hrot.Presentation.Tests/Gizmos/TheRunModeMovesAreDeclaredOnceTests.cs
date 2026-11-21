using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Actions;
using Hrot.Common.Constants;
using Hrot.Common.Editing;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ A6 (W-3, 📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the run-mode moves on a moment are DECLARED ONCE (<see cref="RunModeActions"/>) and
/// WORDED ONCE: the timeline's clicked moment / event and the mode indicator's playhead compose them from the catalogue's Run group.
/// <para>Capture: <c>MENU_GOLDEN_CAPTURE=1 dotnet test … --filter TheRunModeMovesAreDeclaredOnceTests</c> rewrites <c>Goldens/runmode.golden.txt</c>.</para>
/// </summary>
[Collection(ImGuiTestCollection.Name)]
public sealed class TheRunModeMovesAreDeclaredOnceTests
{
    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Goldens", "runmode.golden.txt");

    private static string Render()
    {
        var sb = new StringBuilder();
        foreach (var target in new[] { ActionTarget.Moment, ActionTarget.Event, ActionTarget.Playhead })
            foreach (var mode in new[] { EditingMode.Edit, EditingMode.Preview, EditingMode.Live, EditingMode.Review })
            {
                sb.Append(target).Append(' ').Append(mode).Append(':').Append('\n');
                foreach (var row in MenuComposer.Compose(ActionContext.ForMoment(target, mode, "3:12", 0, "Fire")))
                    sb.Append("  ").Append(row.IsSeparator == true ? "---" : $"{row.Id} {row.Label}").Append('\n');
            }
        return sb.ToString();
    }

    /// <summary>The composed rows per target × mode (declarations only). 🔴 Red-proof: <i>Take checkpoint here</i> declared Live-only ⇒ the Review rows move.</summary>
    [Fact]
    public void A6_TheRunModeRowsPerTargetAndMode_AreTheGolden()
    {
        var actual = Render();
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("MENU_GOLDEN_CAPTURE") == "1") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, actual); }
        Assert.True(File.Exists(path), $"no golden at {path} — capture it with MENU_GOLDEN_CAPTURE=1");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    /// <summary>⭐ The Run group is FIRST in every mode, and no unit / graphic declaration is in it (so the A1–A3 menu goldens do not move).</summary>
    [Fact]
    public void A6_TheRunGroupLeadsEveryMode_AndHoldsOnlyMomentMoves()
    {
        foreach (var mode in new[] { EditingMode.Edit, EditingMode.Preview, EditingMode.Live, EditingMode.Review })
            Assert.Equal(ActionGroup.Run, MenuComposer.GroupOrder(mode)[0]);
        const ActionTarget moments = ActionTarget.Event | ActionTarget.Moment | ActionTarget.Range | ActionTarget.Playhead;
        Assert.All(ActionCatalogue.Shared.All.Where(d => d.Group == ActionGroup.Run), d => Assert.Equal(ActionTarget.None, d.Targets & ~moments));
    }

    /// <summary>
    /// ⭐ WORDED ONCE: the mode indicator's DAR rows are <see cref="RunModeActions.DarLast"/> / <see cref="RunModeActions.DarFromStartLabel"/>, its
    /// Run to is <see cref="RunModeActions.RunToLabel"/>, the timeline's are <see cref="RunModeActions.DarFrom"/> / <see cref="RunModeActions.AarFrom"/>.
    /// 🔴 Red-proof: the section building its own "DAR — the last …" text ⇒ a changed word in RunModeActions leaves the toolbar behind.
    /// </summary>
    [Fact]
    public void A6_TheIndicatorAndTheTimeline_SayTheDeclaredWords()
    {
        var live = new FakeTimeTransportFacade { Mode = TransportMode.Live, TotalTime = 754 };
        var labels = new MainToolbarTimeControlSection(live).ModeMenu()!.Items.Select(i => i.Label).ToArray();
        var expected = RunModeActions.DarWindows.Select(RunModeActions.DarLast).Append(RunModeActions.DarFromStartLabel).ToArray();
        Assert.Equal(expected, labels);

        var rows = MenuComposer.Compose(ActionContext.ForMoment(ActionTarget.Moment, EditingMode.Live, "0:05"));
        Assert.Equal(new[] { RunModeActions.DarFrom("0:05"), RunModeActions.AarFrom("0:05"), RunModeActions.TakeCheckpointHereLabel }, rows.Select(r => r.Label));
        Assert.Equal(RunModeActions.DarGlyph, rows[0].Icon);
        Assert.Equal(RunModeBinding.ModeOf(TransportMode.Replay), RunModeBinding.ModeOf(TransportMode.Review));
    }

    /// <summary>⭐ In an AAR the mode menu offers <i>Go live from here</i> after the arming moves (the playhead's move); a DAR's menu is unchanged.</summary>
    [Fact]
    public void A6_InAnAar_TheModeMenuOffersGoLiveFromHere()
    {
        var aar = RunChoiceRig.With(ClusterState.OperatingReplay, RunArmed.AAR);
        var items = new MainToolbarTimeControlSection(aar.F, choice: aar.Choice).ModeMenu()!.Items;
        var go = Assert.Single(items, i => i.Label.StartsWith(RunModeActions.GoLiveFrom("here")));
        Assert.True(items[items.ToList().IndexOf(go) - 1].IsSeparator);
    }
}
