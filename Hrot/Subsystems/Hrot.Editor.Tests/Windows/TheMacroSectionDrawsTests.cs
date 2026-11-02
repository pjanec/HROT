using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.Scenario;
using Hrot.Editor.UiFrameRail;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4210 MA-8 — a REAL ImGui frame of the Macro section of Details: the status line, the macro, one args row per header param of the picked macro, the default greyed (a "default" tag, no reset) until it is
/// overridden (then a reset button), and an instance this node does not own is read-only with its reason. Under <c>xvfb-run -a</c>; skipped without a display — ⛔ a skip is not a gate row.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheMacroSectionDrawsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macro-frame-" + Guid.NewGuid().ToString("N"));

    public TheMacroSectionDrawsTests()
    {
        var header = new ScenarioHeader("Hrot.Scenario")
        {
            Kind = ScenarioHeader.MacroKind,
            Params = new[] { new ScenarioParamDecl("side", "ForceId", JsonValue.Create("Hostile")), new ScenarioParamDecl("speed", "float", JsonValue.Create(2.5f)) },
        };
        var dir = Path.Combine(_root, "test", "fire-team");
        Directory.CreateDirectory(dir);
        var h = new JsonObject();
        ScenarioHeaderJson.Write(h, header);
        File.WriteAllText(Path.Combine(dir, "scenario.json"), new JsonObject { ["Header"] = h }.ToJsonString());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private (EntityRepository Repo, Entity E, MacroInstanceDetailsView View) Fixture(string argsJson, bool owned, MacroExpansionState state = MacroExpansionState.Done, string reason = "")
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<MacroInstance>();
        repo.RegisterComponent<MacroExpansionStatus>();
        var e = repo.CreateEntity();
        repo.SetManagedComponent(e, new MacroInstance { Macro = "test/fire-team", ArgsJson = argsJson });
        repo.SetComponent(e, new MacroExpansionStatus { State = state, Reason = new FixedString128(reason) });
        if (owned) repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(MacroInstance)), true);
        var view = new MacroInstanceDetailsView(() => repo, () => new RepositoryAdapter(repo), () => _root,
            () => Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build(), pickMacro: _ => { });
        return (repo, e, view);
    }

    private static void Frame(MacroInstanceDetailsView view, Entity e, string? screenshot = null)
    {
        var ctx = new DetailsContext(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);
        using var frame = UiFrameHarness.Begin();
        frame.StepN(4, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(620, 320), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Macro (CE-4210)");
            view.Draw(ctx, "t");
            ImGuiNET.ImGui.End();
        });
        if (screenshot is not null)
        {
            var dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
            frame.Screenshot(Path.Combine(dir, screenshot));
        }
    }

    [SkippableFact]
    public void AnOwnedInstance_ShowsItsStatus_OneRowPerParam_AndNoResetUntilOverridden()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, e, view) = Fixture("{}", owned: true);
        using var _r = repo;
        Frame(view, e, "ce4210-macro-default.png");
        Assert.Equal("Done", view.StatusText);
        Assert.Equal(2, view.RowsDrawn);
        Assert.Equal(0, view.ResetButtons);
        Assert.False(view.ReadOnly);
    }

    [SkippableFact]
    public void AnOverriddenParam_ShowsItsResetButton()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, e, view) = Fixture("{\"side\":\"Friend\"}", owned: true);
        using var _r = repo;
        Frame(view, e, "ce4210-macro-override.png");
        Assert.Equal(2, view.RowsDrawn);
        Assert.Equal(1, view.ResetButtons);
    }

    [SkippableFact]
    public void AFailedExpansion_SaysWhy_AndAnInstanceNotOwnedByThisNode_IsReadOnly()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, e, view) = Fixture("{}", owned: false, MacroExpansionState.Failed, "no spot for 'Rifleman A'");
        using var _r = repo;
        Frame(view, e, "ce4210-macro-failed-notowned.png");
        Assert.Equal("Failed: no spot for 'Rifleman A'", view.StatusText);
        Assert.True(view.ReadOnly);
    }

    [SkippableFact]
    public void AMacroMissingOnTheServer_ShowsTheReason_NoRows()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, e, view) = Fixture("{}", owned: true);
        using var _r = repo;
        repo.SetManagedComponent(e, new MacroInstance { Macro = "no/such", ArgsJson = "{}" });
        Frame(view, e);
        Assert.Equal(0, view.RowsDrawn);
    }
}
