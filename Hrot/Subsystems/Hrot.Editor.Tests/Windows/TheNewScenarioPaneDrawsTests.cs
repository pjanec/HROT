using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using Hrot.Editor.UiFrameRail;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ B-9 N1 pane (R-4138, docs/UX/new-scenario-visual.svg) — a REAL ImGui frame of the New Scenario picker's side pane for a terrain row: <i>Origin</i> · <i>Roads</i> · <i>Recipes on it</i> as label / value rows whose
/// values all start at ONE x (the mockup's value column, <see cref="NewAssetLauncher.PaneValueX"/> from the label), then the default text; a terrain with no origin / roads draws only the rows it has. Under <c>xvfb-run -a</c>;
/// skipped without a display — ⛔ a skip is not a gate row. ✅ Red-proof (compiles): the value drawn with <c>SameLine(PaneValueX + row * 10)</c> ⇒ the offsets differ ⇒ red.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheNewScenarioPaneDrawsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "b9pane-" + Guid.NewGuid().ToString("N"));
    private readonly string? _savedRoot = AssetRoots.ConfiguredRoot;

    public TheNewScenarioPaneDrawsTests()
    {
        Directory.CreateDirectory(_root);
        AssetRoots.Configure(_root);
    }

    public void Dispose() { AssetRoots.Configure(_savedRoot); try { Directory.Delete(_root, true); } catch { } }

    private sealed class NoIcons : NodeEditor.Core.Interfaces.IIconProvider
    {
        public bool TryGet(string key, out NodeEditor.Core.Interfaces.IconHandle handle) { handle = default; return false; }
    }

    private sealed class Svc : INewAssetService
    {
        public AssetKind Kind => AssetKind.Scenario;
        public IReadOnlyList<IEditableAsset> AvailableRecipes() => Array.Empty<IEditableAsset>();
        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath) => throw new NotSupportedException();
    }

    private string Terrain(string name, string json)
    {
        var dir = Path.Combine(_root, "terrains", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "terrain.json"), json);
        return Path.Combine(dir, "terrain.json");
    }

    [SkippableFact]
    public void ATerrainRow_DrawsItsFactsInOneValueColumn_AndOnlyTheRowsItHas()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Terrain("berlin", "{\"schemaVersion\":2,\"name\":\"berlin\",\"origin\":{\"lat\":52.52,\"lon\":13.405,\"alt\":0},\"roadNetworks\":[\"roads.json\"]}");
        Terrain("plain", "{\"schemaVersion\":2,\"name\":\"plain\"}");
        Directory.CreateDirectory(Path.Combine(AssetRoots.ScenariosRecipesRoot, "sop-demo"));
        File.WriteAllText(Path.Combine(AssetRoots.ScenariosRecipesRoot, "sop-demo", "scenario.json"), "{\"header\":{\"terrainName\":\"berlin\"},\"entities\":{}}");

        PickerRequest? request = null;
        var launcher = new NewAssetLauncher((r, _) => request = r, new Dictionary<AssetKind, INewAssetService> { [AssetKind.Scenario] = new Svc() }, (_, _) => { },
            terrains: () => new[] { "berlin", "plain" }, showNewBlankDialog: _ => { },
            terrainDefinition: t => Path.Combine(_root, "terrains", t, "terrain.json"));
        launcher.Open(AssetKind.Scenario);
        var rows = request!.ItemsProvider().Where(e => e.Tag is BlankTerrainChoice).ToDictionary(e => e.Name);
        var ctx = new PickerPaneContext
        {
            Width = 520f, Theme = new NodeEditor.Core.DefaultTheme(), Icons = new NoIcons(), Images = new PickerImageCache(_ => null), DrawDefault = () => { },
        };

        using var frame = UiFrameHarness.Begin(700, 300);
        void Draw(string terrain, string? shot)
        {
            frame.StepN(4, () =>
            {
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                ImGuiNET.ImGui.SetNextWindowSize(new Vector2(560, 260), ImGuiNET.ImGuiCond.Always);
                ImGuiNET.ImGui.Begin("Pane");
                request!.DrawPane!(rows[terrain], ctx);
                ImGuiNET.ImGui.End();
            });
            if (shot != null)
            {
                var dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
                frame.Screenshot(Path.Combine(dir, shot));
            }
        }

        Draw("berlin", "b9-new-scenario-pane-berlin.png");
        Assert.Equal(3, launcher.LastPaneValueOffsets.Count);                                    // Origin, Roads, Recipes on it
        Assert.All(launcher.LastPaneValueOffsets, o => Assert.Equal(NewAssetLauncher.PaneValueX, o, 1));   // ONE value column, the mockup's 124 px

        Draw("plain", null);
        Assert.Single(launcher.LastPaneValueOffsets);                                            // no origin, no roads: only "Recipes on it" (none)
    }
}
