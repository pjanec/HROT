using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.UiFrameRail;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ AQ108 D24 (the ui half) — a REAL ImGui frame of the scenario picker's side pane with the <b>Run setup</b> expander OPEN: the variant row (<i>random (drawn at start)</i> / a number) and one typed row per declared param, at their defaults; then
/// the start passes exactly what was chosen. A scenario with no variants shows no variant row; with no params and no variants, no expander at all. Under <c>xvfb-run -a</c>; skipped without a display — ⛔ a skip is not a gate row.
/// Red-proof (compiles): <c>OpenAndGoLive</c> not passing <c>RunArgsFor</c> ⇒ the args assert fails.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheRunSetupDrawsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rs-d24-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Scenario(string name, string header, string entities)
    {
        Directory.CreateDirectory(Path.Combine(_root, name));
        var file = Path.Combine(_root, name, "scenario.json");
        File.WriteAllText(file, "{\"header\":{" + header + "},\"entities\":{" + entities + "}}");
        return file;
    }

    private sealed class Asset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public AssetKind Kind => AssetKind.Scenario;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private static PickerEntry EntryFor(IEditableAsset asset)
        => new(Id: asset.AssetId.ToString(), Name: asset.Name, Description: null, Category: null, Keywords: null, IconTextureId: null, Tag: asset, IconKey: AssetKindIcons.GetIconKey(asset.Kind));

    private sealed class NoIcons : NodeEditor.Core.Interfaces.IIconProvider
    {
        public bool TryGet(string key, out NodeEditor.Core.Interfaces.IconHandle handle) { handle = default; return false; }
    }

    private sealed class Control : Hrot.UI.Common.RunMode.IRunScenarioControl
    {
        public List<(string Name, string? Args)> Loads { get; } = new();
        public string? LoadedScenarioName => "s";
        public void LoadForLive(string scenarioName) => Loads.Add((scenarioName, null));
        public void LoadForLive(string scenarioName, string? argsJson) => Loads.Add((scenarioName, argsJson));
        public void OpenForEdit(string scenarioName) { }
        public void OpenRecording(Guid exerciseId) { }
        public void SaveCurrent() { }
        public void TakeCheckpoint() { }
    }

    [SkippableFact]
    public void TheOpenExpander_ShowsTheVariantAndTheParams_AndTheStartPassesWhatWasChosen()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var file = Scenario("bridge",
            "\"description\":\"d\",\"Params\":[{\"Name\":\"count\",\"TypeId\":\"int\",\"Default\":2},{\"Name\":\"side\",\"TypeId\":\"ForceId\",\"Default\":\"Hostile\"}]",
            "\"a\":{\"Presence\":0.6}");
        var plain = Scenario("plain", "\"description\":\"d\"", "\"a\":{}");
        var asset = new Asset { Name = "bridge", SourceFilePath = file };
        var control = new Control();
        var choice = Hrot.UI.Common.RunMode.NodeRunControls.For(new Fdp.Core.FdpEventBus(), () => null, () => 0.0, null,
            new Hrot.UI.Common.RunMode.NodeRunProfile(Hrot.UI.Common.RunMode.RunHostKind.Editor, control)).RunChoice;
        PickerRequest? request = null; Action<PickerResult>? handler = null;
        var launcher = new AssetPickerLauncher((r, h) => { request = r; handler = h; }, new Cat(asset), new AssetPickActionRouter(_ => { }, _ => { }), baseFolderResolver: _ => "",
            runChoice: () => choice, paramEditService: () => Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build());
        launcher.Open(AssetKindFilter.Scenario);
        var entry = EntryFor(asset);
        var ctx = new PickerPaneContext
        {
            Width = 520f, Theme = new NodeEditor.Core.DefaultTheme(), Icons = new NoIcons(), Images = new PickerImageCache(_ => null), DrawDefault = () => { },
        };

        using var frame = UiFrameHarness.Begin(700, 420);
        void Draw(bool forceOpen)
        {
            frame.StepN(6, () =>
            {
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                ImGuiNET.ImGui.SetNextWindowSize(new Vector2(560, 380), ImGuiNET.ImGuiCond.Always);
                ImGuiNET.ImGui.Begin("Pane");
                if (forceOpen) ImGuiNET.ImGui.GetStateStorage().SetInt(ImGuiNET.ImGui.GetID("Run setup##runsetup"), 1);   // what a click on the header does
                request!.DrawPane!(entry, ctx);
                ImGuiNET.ImGui.End();
            });
        }

        Draw(forceOpen: false);                                                  // collapsed: the defaults, nothing remembered
        Assert.Null(launcher.RunSetupOf("bridge"));
        Assert.Null(launcher.RunArgsFor("bridge"));

        Draw(forceOpen: true);
        var shots = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        frame.Screenshot(Path.Combine(shots, "d24-run-setup-open.png"));
        var setup = launcher.RunSetupOf("bridge");
        Assert.NotNull(setup);                                                   // the open expander made a setup ...
        Assert.True(setup!.UsesVariants);                                        // ... with the variant row (the file uses variants)
        Assert.True(setup.RandomVariant);                                        // random is the default
        Assert.Equal(2, launcher.RunSetupRowsDrawn);                             // one typed row per declared param
        Assert.Null(launcher.RunArgsFor("bridge"));                              // open but untouched: still the defaults

        setup.RandomVariant = false; setup.Variant = 3; setup.Set("count", "7");  // what the form's widgets do
        handler!(new PickerResult(new[] { entry }) { ActionId = AssetPickerLauncher.GoLiveActionId });
        var load = Assert.Single(control.Loads);
        Assert.Equal("bridge", load.Name);
        var args = System.Text.Json.Nodes.JsonNode.Parse(load.Args!)!.AsObject();
        Assert.Equal(3, args["$variant"]!.GetValue<int>());
        Assert.Equal(7, args["count"]!.GetValue<int>());
        Assert.Equal(2, args.Count);

        // a scenario with neither variants nor params has no Run setup at all
        var plainAsset = new Asset { Name = "plain", SourceFilePath = plain };
        var plainEntry = EntryFor(plainAsset);
        frame.StepN(4, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(560, 380), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Pane2");
            ImGuiNET.ImGui.GetStateStorage().SetInt(ImGuiNET.ImGui.GetID("Run setup##runsetup"), 1);
            request!.DrawPane!(plainEntry, ctx);
            ImGuiNET.ImGui.End();
        });
        Assert.Null(launcher.RunSetupOf("plain"));
    }

    private static void Pos(ImGuiNET.ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    /// <summary>
    /// ⭐ CE-4258 ② (crash rail) — a click on <i>reset</i> of an overridden row ends the table cleanly: the reset takes the override off and the next frames draw the
    /// rows at their defaults. The edit is made through the row's session, as the combo does (the session stays dirty after its commit). Two rows of ONE type (①: they drew widgets with the same ImGui id until each row got its own id scope). Red-proof (compiles):
    /// <c>ParamArgsTable.Draw</c> calling <c>onReset</c> from inside the row loop ⇒ this rail throws (the run setup's reset disposes every row mid-table).
    /// </summary>
    [SkippableFact]
    public void CE4258_ResetOnAnOverriddenRow_EndsTheTableCleanly_AndTakesTheOverrideOff()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var file = Scenario("outpost",
            "\"description\":\"d\",\"Params\":[{\"Name\":\"side\",\"TypeId\":\"ForceId\",\"Default\":\"Hostile\"},{\"Name\":\"other\",\"TypeId\":\"ForceId\",\"Default\":\"Friend\"}]",
            "\"a\":{}");
        var asset = new Asset { Name = "outpost", SourceFilePath = file };
        var choice = Hrot.UI.Common.RunMode.NodeRunControls.For(new Fdp.Core.FdpEventBus(), () => null, () => 0.0, null,
            new Hrot.UI.Common.RunMode.NodeRunProfile(Hrot.UI.Common.RunMode.RunHostKind.Editor, new Control())).RunChoice;
        PickerRequest? request = null;
        var launcher = new AssetPickerLauncher((r, h) => request = r, new Cat(asset), new AssetPickActionRouter(_ => { }, _ => { }), baseFolderResolver: _ => "",
            runChoice: () => choice, paramEditService: () => Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build());
        launcher.Open(AssetKindFilter.Scenario);
        var entry = EntryFor(asset);
        var ctx = new PickerPaneContext
        {
            Width = 520f, Theme = new NodeEditor.Core.DefaultTheme(), Icons = new NoIcons(), Images = new PickerImageCache(_ => null), DrawDefault = () => { },
        };

        using var frame = UiFrameHarness.Begin(700, 420);
        void Step(Action<ImGuiNET.ImGuiIOPtr>? inject, int times = 1)
        {
            for (int i = 0; i < times; i++)
                frame.Step(() =>
                {
                    inject?.Invoke(ImGuiNET.ImGui.GetIO());
                    ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                    ImGuiNET.ImGui.SetNextWindowSize(new Vector2(560, 380), ImGuiNET.ImGuiCond.Always);
                    ImGuiNET.ImGui.Begin("Pane");
                    ImGuiNET.ImGui.GetStateStorage().SetInt(ImGuiNET.ImGui.GetID("Run setup##runsetup"), 1);
                    request!.DrawPane!(entry, ctx);
                    ImGuiNET.ImGui.End();
                });
        }

        Step(null, 4);
        var setup = launcher.RunSetupOf("outpost")!;
        Assert.Equal(2, launcher.RunSetupRowsDrawn);
        // what the user did: pick a value in the row's combo — an edit through the row's own session (the combo writes the field's binding)
        var session = launcher.RunSetupTable!.SessionOf("side")!;
        Fdp.Toolkit.Scenario.ParamTypes.TryResolve("ForceId", out var forceType);
        var node = Hrot.Editor.AiShared.Variables.VariableEditModal.ScalarRowOrRoot(session.Document.Root, "side");
        node.Binding!.SetBoxed(Enum.Parse(forceType!, "Neutral"));
        Step(null, 3);
        Assert.NotNull(launcher.RunArgsFor("outpost"));                          // the edit was committed: the row is overridden
        var at = Assert.IsType<Vector2>(launcher.RunSetupResetButtonAt);         // the overridden row shows its reset button

        Step(io => Pos(io, at.X, at.Y), 2);
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, at.X, at.Y));
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });   // the click: reset (the crash was here)
        Step(null, 3);                                                           // and the table keeps drawing

        Assert.Null(launcher.RunArgsFor("outpost"));                             // the override is gone: the start passes nothing
        Assert.Null(launcher.RunSetupResetButtonAt);                             // no overridden row left
        Assert.Equal(2, launcher.RunSetupRowsDrawn);                             // both rows still draw
    }

    private sealed class Cat : IAssetCatalog
    {
        private readonly IReadOnlyList<IEditableAsset> _assets;
        public Cat(params IEditableAsset[] assets) => _assets = assets.ToList().AsReadOnly();
        public IReadOnlyList<IEditableAsset> All => _assets;
        public IEditableAsset? FindByAssetId(Guid assetId) => _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
#pragma warning disable CS0067
        public event Action<AssetKind>? Changed;
#pragma warning restore CS0067
    }
}
