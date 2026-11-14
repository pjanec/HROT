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
/// ⭐ CE-3392 (📄 docs/UX/DESIGN_Scenario_Presets.md §5b — the successor of AQ108 D24's Run setup expander, which this class used to test: "its job is the presets") — the load
/// dialog's <b>Preset</b> combo: drawn under the thumbnail and the name (the picker's <c>DrawUnderHeader</c>) only when the scenario has a preset besides Default; the chosen preset's
/// args (its overrides + <c>$variant</c>) ride <i>Open and go live</i> AND <i>Open</i> (an edit open with args); Default passes nothing and Open takes the plain route.
/// The frame test runs under <c>xvfb-run -a</c>; skipped without a display — ⛔ a skip is not a gate row.
/// 🔴 Red-proof (compiles): <c>RunArgsFor</c> answering null ⇒ the args asserts fail.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheRunSetupDrawsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rs-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Scenario(string name, string header)
    {
        Directory.CreateDirectory(Path.Combine(_root, name));
        var file = Path.Combine(_root, name, "scenario.json");
        File.WriteAllText(file, "{\"header\":{" + header + "},\"entities\":{\"a\":{}}}");
        return file;
    }

    private const string WithPresets =
        "\"Params\":[{\"Name\":\"count\",\"TypeId\":\"int\",\"Default\":2}],"
        + "\"Presets\":[{\"Name\":\"Easy\",\"Variant\":\"AsAuthored\",\"Args\":{\"count\":5}},{\"Name\":\"Night attack\",\"Variant\":3}]";

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
        public List<(string Name, string? Args)> Opens { get; } = new();
        public string? LoadedScenarioName => "s";
        public void LoadForLive(string scenarioName) => Loads.Add((scenarioName, null));
        public void LoadForLive(string scenarioName, string? argsJson) => Loads.Add((scenarioName, argsJson));
        public void OpenForEdit(string scenarioName) => Opens.Add((scenarioName, null));
        public void OpenForEdit(string scenarioName, string? argsJson) => Opens.Add((scenarioName, argsJson));
        public void OpenRecording(Guid exerciseId) { }
        public void SaveCurrent() { }
        public void TakeCheckpoint() { }
    }

    private (AssetPickerLauncher Launcher, Control Control, Func<PickerRequest> Request, Action<PickerResult> Confirm, List<string> Routed) Rig(Asset asset)
    {
        var control = new Control();
        var choice = Hrot.UI.Common.RunMode.NodeRunControls.For(new Fdp.Core.FdpEventBus(), () => null, () => 0.0, null,
            new Hrot.UI.Common.RunMode.NodeRunProfile(Hrot.UI.Common.RunMode.RunHostKind.Editor, control)).RunChoice;
        PickerRequest? request = null; Action<PickerResult>? handler = null;
        var routed = new List<string>();
        var launcher = new AssetPickerLauncher((r, h) => { request = r; handler = h; }, new Cat(asset), new AssetPickActionRouter(_ => { }, name => routed.Add(name)), baseFolderResolver: _ => "",
            runChoice: () => choice);
        launcher.Open(AssetKindFilter.Scenario);
        return (launcher, control, () => request!, r => handler!(r), routed);
    }

    [Fact]
    public void ThePresetsArgs_RideOpenAndGoLive_AndOpen_DefaultPassesNothing()
    {
        var asset = new Asset { Name = "bridge", SourceFilePath = Scenario("bridge", WithPresets) };
        var (launcher, control, _, confirm, routed) = Rig(asset);
        var entry = EntryFor(asset);

        Assert.Null(launcher.RunArgsFor("bridge"));                           // Default
        confirm(new PickerResult(new[] { entry }) { ActionId = AssetPickerLauncher.OpenActionId });
        Assert.Single(routed);                                                // Default ⇒ the plain route, exactly as before
        Assert.Empty(control.Opens);

        launcher.ChoosePreset("bridge", 1);                                   // Easy
        Assert.Equal("""{"count":5}""", launcher.RunArgsFor("bridge"));
        confirm(new PickerResult(new[] { entry }) { ActionId = AssetPickerLauncher.GoLiveActionId });
        Assert.Contains(control.Loads, l => l.Name == "bridge" && l.Args == """{"count":5}""");

        launcher.ChoosePreset("bridge", 2);                                   // Night attack — variant 3
        confirm(new PickerResult(new[] { entry }) { ActionId = AssetPickerLauncher.OpenActionId });
        Assert.Contains(control.Opens, o => o.Name == "bridge" && o.Args == """{"$variant":3}""");
    }

    [Fact]
    public void TheSummary_SaysTheVariantTheChangesAndPreviewOnly()
    {
        Assert.Equal("as authored · 1 changed", AssetPickerLauncher.PresetSummary(new Fdp.Toolkit.Scenario.ScenarioPreset("E", Fdp.Toolkit.Scenario.PresetVariant.AsAuthored, new System.Text.Json.Nodes.JsonObject { ["count"] = 5 })));
        Assert.Equal("variant 3 · preview only", AssetPickerLauncher.PresetSummary(new Fdp.Toolkit.Scenario.ScenarioPreset("N", new Fdp.Toolkit.Scenario.PresetVariant(Fdp.Toolkit.Scenario.PresetVariantKind.Number, 3))));
        Assert.Equal("random each run · preview only", AssetPickerLauncher.PresetSummary(new Fdp.Toolkit.Scenario.ScenarioPreset("R", Fdp.Toolkit.Scenario.PresetVariant.RandomEachRun)));
        Assert.StartsWith(Fdp.Presentation.Fonts.IconsFontAwesome6.Eye, AssetPickerLauncher.PresetLabel(new Fdp.Toolkit.Scenario.ScenarioPreset("R", Fdp.Toolkit.Scenario.PresetVariant.RandomEachRun)));
        Assert.StartsWith(Fdp.Presentation.Fonts.IconsFontAwesome6.Pen, AssetPickerLauncher.PresetLabel(new Fdp.Toolkit.Scenario.ScenarioPreset("E", Fdp.Toolkit.Scenario.PresetVariant.AsAuthored)));
    }

    [SkippableFact]
    public void TheCombo_IsDrawnUnderTheHeader_OnlyWhenThereIsAPresetBesidesDefault()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var with = new Asset { Name = "bridge", SourceFilePath = Scenario("bridge", WithPresets) };
        var without = new Asset { Name = "plain", SourceFilePath = Scenario("plain", "\"description\":\"d\"") };
        var ctx = new PickerPaneContext { Width = 520f, Theme = new NodeEditor.Core.DefaultTheme(), Icons = new NoIcons(), Images = new PickerImageCache(_ => null), DrawDefault = () => { } };
        using var frame = UiFrameHarness.Begin(700, 420);
        foreach (var (asset, expectCombo) in new[] { (with, true), (without, false) })
        {
            var (_, _, request, _, _) = Rig(asset);
            Assert.NotNull(request().DrawUnderHeader);
            bool sawCombo = false;
            frame.StepN(4, () =>
            {
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                ImGuiNET.ImGui.SetNextWindowSize(new Vector2(560, 380), ImGuiNET.ImGuiCond.Always);
                ImGuiNET.ImGui.Begin("Pane");
                var before = ImGuiNET.ImGui.GetCursorPosY();
                request().DrawUnderHeader!(EntryFor(asset), ctx);
                sawCombo = ImGuiNET.ImGui.GetCursorPosY() > before;
                ImGuiNET.ImGui.End();
            });
            Assert.Equal(expectCombo, sawCombo);
        }
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
