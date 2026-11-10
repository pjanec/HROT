using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
using Xunit;

namespace Hrot.Presentation.Tests.TerrainOsm;

/// <summary>
/// ⭐ CE-4375 W-4 ① — the wizard's STATE MACHINE (OSM §6b stateDiagram), headless, over the W-2 fakes. Each test walks one edge of the diagram and asserts what the footer would
/// offer (<see cref="OsmWizardModel.PrimaryRefusal"/>), so a mis-wired transition fails here before any frame is drawn.
/// </summary>
public sealed class OsmWizardModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "osm-wizard-" + Guid.NewGuid().ToString("N"));
    private readonly FakeOsmPipeline _pipeline = new();
    private readonly FakeTerrainImporter _importer = new();

    public OsmWizardModelTests() { Directory.CreateDirectory(_root); _importer.Root = Path.Combine(_root, "terrain"); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private OsmWizardModel Model(bool importer = true, bool pipeline = true) => new(new OsmWizardServices(
        pipeline ? _pipeline : null, new FakeTileSource(), new FakePlaceSearch(), importer ? _importer : null,
        Path.Combine(_root, "terrain"), Path.Combine(_root, "input")));

    private static void PumpUntil(OsmWizardModel m, Func<bool> done, int ms = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!done())
        {
            if (sw.ElapsedMilliseconds > ms) throw new TimeoutException("the wizard did not reach the state");
            m.Pump(); Thread.Sleep(5);
        }
    }

    /// <summary>The Area step: no box ⇒ Next refused with words; a box over 6 km ⇒ refused (the RED chip, C-7); the fake place's box ⇒ Next goes to Options.
    /// 🔴 Red-proof: drop the <c>BoxTooLarge</c> clause in <c>SetBox</c> ⇒ the 7 km box is accepted.</summary>
    [Fact]
    public void Area_NeedsABox_WithinTheLimit_ThenNextGoesToOptions()
    {
        var m = Model();
        Assert.Equal(OsmWizardStep.Area, m.Step);
        Assert.NotNull(m.PrimaryRefusal);
        Assert.False(m.Primary());

        m.SetBox(WebMercator.BoxAround(OsmFakeWorld.Lat, OsmFakeWorld.Lon, 7000, 3000));
        Assert.True(m.BoxTooLarge);
        Assert.Contains("6 × 6 km", m.PrimaryRefusal);
        Assert.False(m.Primary());

        m.SetBox(OsmFakeWorld.Box);
        Assert.Equal(4800, m.BoxWidthM, 0);
        Assert.Equal(3500, m.BoxHeightM, 0);
        Assert.Null(m.PrimaryRefusal);
        Assert.True(m.Primary());
        Assert.Equal(OsmWizardStep.Options, m.Step);
    }

    /// <summary>The search (C-6): results land on a Pump (never the frame waiting); picking one sets the place's box, and a place with no box gets a 2 km box round it.</summary>
    [Fact]
    public void Search_LandsOnAPump_AndPickingAResultSetsItsBox()
    {
        var m = Model();
        m.SearchText = "Kolín";
        m.RunSearch();
        PumpUntil(m, () => m.SearchResults.Count > 0);
        Assert.Equal(3, m.SearchResults.Count);
        Assert.Equal(("Kolín", "town, Central Bohemia, Czechia"), OsmTerrainWizardWindow.SplitPlace(m.SearchResults[0].Name));

        var box = m.PickResult(0);
        Assert.Equal(OsmFakeWorld.Box, box);
        Assert.Empty(m.SearchResults);

        m.SearchText = "Kolín"; m.RunSearch(); PumpUntil(m, () => m.SearchResults.Count > 0);
        m.PickResult(1);   // a 30 × 25 km district ⇒ clamped to the limit
        Assert.InRange(m.BoxWidthM, 5990, 6010);
        Assert.InRange(m.BoxHeightM, 5990, 6010);
        Assert.Null(m.BoxRefusal);

        m.SearchText = "Kolín"; m.RunSearch(); PumpUntil(m, () => m.SearchResults.Count > 0);
        m.PickResult(2);   // no box ⇒ 2 km round it
        Assert.InRange(m.BoxWidthM, 1990, 2010);
    }

    /// <summary>Options (C-9): the name is a folder name; an existing terrain needs Replace, and the scenarios that use it are listed.
    /// 🔴 Red-proof: drop the <c>NameExists &amp;&amp; !Replace</c> clause ⇒ Preview is offered over an existing terrain.</summary>
    [Fact]
    public void Options_TheNameIsAFolderName_AndAnExistingTerrainNeedsReplace()
    {
        var m = Model();
        m.SetBox(OsmFakeWorld.Box); m.Primary();
        Assert.Equal("a terrain needs a name", m.PrimaryRefusal);
        m.Name = "kolin/east";
        Assert.Contains("folder name", m.PrimaryRefusal);
        m.Name = "kolin";
        Assert.Null(m.PrimaryRefusal);
        Assert.Equal("assets/terrain/kolin/", m.SavedAs);

        Directory.CreateDirectory(Path.Combine(_root, "terrain", "kolin"));
        _importer.Users["kolin"] = new[] { "patrol-1", "convoy" };
        Assert.True(m.NameExists);
        Assert.Equal(new[] { "patrol-1", "convoy" }, m.ScenariosUsingName);
        Assert.Contains("Replace", m.PrimaryRefusal);
        m.Replace = true;
        Assert.Null(m.PrimaryRefusal);

        m.IncludeTrees = false; m.StoreyHeight = 3.5; m.DefaultStoreys = 3;
        var o = m.Options;
        Assert.False(o.Trees); Assert.True(o.Buildings); Assert.Equal(3.5, o.StoreyHeight); Assert.Equal(3, o.DefaultStoreys); Assert.Equal("kolin", o.Name);
    }

    /// <summary>Preview (C-10 … C-13): running ⇒ Import refused with words and the held progress shown; done ⇒ the fake place; Refused ⇒ Import greyed; a failure names the stage and Retry runs again.
    /// Back from a running preview CANCELS it (the token).</summary>
    [Fact]
    public void Preview_RunsOnAWorker_TheVerdictGatesImport_AndAFailureCanBeRetried()
    {
        var m = Model();
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin";
        _pipeline.Hold = true;
        Assert.True(m.Primary());
        Assert.Equal(OsmWizardStep.Preview, m.Step);
        PumpUntil(m, () => m.PreviewProgress.Stage == OsmStage.Buildings);
        Assert.Equal(OsmRunState.Running, m.PreviewState);
        Assert.Equal("the preview is still being made", m.PrimaryRefusal);

        m.Back();   // ⇒ cancelled
        Assert.Equal(OsmWizardStep.Options, m.Step);
        Assert.True(_pipeline.LastToken.IsCancellationRequested);

        _pipeline.Hold = false;
        _pipeline.Verdict = BudgetVerdict.Refused; _pipeline.VerdictReason = "3.4× the tuned buildings";
        m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
        Assert.Equal(OsmFakeWorld.Buildings.Count, m.Preview!.Buildings.Count);
        Assert.Equal("too heavy for HROT — pick a smaller box", m.PrimaryRefusal);
        Assert.False(m.Primary());

        _pipeline.Verdict = BudgetVerdict.Ok; _pipeline.FailPreviewWith = "Overpass answered 429";
        m.Back(); m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Failed);
        Assert.Contains("Overpass answered 429", m.Failure);
        Assert.Equal("the preview failed — Retry", m.PrimaryRefusal);

        _pipeline.FailPreviewWith = null;
        m.Retry();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
        Assert.Null(m.PrimaryRefusal);
        Assert.Equal(3, _pipeline.PreviewCalls - 1);   // held+cancelled, refused, failed, retried = 4 calls
    }

    /// <summary>The budget bar's scale (C-13): 1× at ⅓, 2× at ⅔ (green ends), 3× at ⅚ (amber ends), full past 4×.</summary>
    [Fact]
    public void TheBudgetBar_PutsTheMarksWhereTheMockupDrawsThem()
    {
        Assert.Equal(1 / 3.0, OsmWizardModel.BarPosition(1), 6);
        Assert.Equal(2 / 3.0, OsmWizardModel.BarPosition(2), 6);
        Assert.Equal(5 / 6.0, OsmWizardModel.BarPosition(3), 6);
        Assert.Equal(1.0, OsmWizardModel.BarPosition(9), 6);
        Assert.Equal(0.0, OsmWizardModel.BarPosition(0), 6);
    }

    /// <summary>Import (C-14): the stages advance from the pipeline's progress, the importer runs after the input set, the folder is offered.
    /// Cancel mid-run stops it (the token), resets the stages and goes back to Preview — and its late answer is ignored.</summary>
    [Fact]
    public void Import_RunsTheStages_ThenTheImporter_AndCancelGoesBackToPreview()
    {
        var m = Model();
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin"; m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);

        _pipeline.Hold = true;
        Assert.True(m.Primary());
        Assert.Equal(OsmWizardStep.Import, m.Step);
        PumpUntil(m, () => m.Stages[4].State == OsmRunState.Running);
        Assert.All(m.Stages.Take(4), s => Assert.Equal(OsmRunState.Done, s.State));
        Assert.Equal(0.62, m.Stages[4].Fraction, 2);
        Assert.Equal("Buildings — 880 of 1 412", m.Stages[4].Produced);
        Assert.Equal(OsmRunState.Idle, m.Stages[5].State);

        Assert.False(m.Cancel());   // Import ⇒ back to Preview, the window stays open
        Assert.Equal(OsmWizardStep.Preview, m.Step);
        Assert.True(_pipeline.LastToken.IsCancellationRequested);
        Assert.All(m.Stages, s => Assert.Equal(OsmRunState.Idle, s.State));

        _pipeline.Hold = false;
        m.Primary();
        PumpUntil(m, () => m.ImportState == OsmRunState.Done);
        Assert.All(m.Stages, s => Assert.Equal(OsmRunState.Done, s.State));
        Assert.Equal(1, _importer.Calls);
        Assert.Equal(Path.Combine(_root, "terrain", "kolin"), m.TerrainFolder);
        Assert.Equal(Path.Combine(_root, "input", "kolin"), m.InputSetFolder);
        Assert.True(m.Cancel());   // done ⇒ Close closes
    }

    /// <summary>No importer composed (the importer is the backend's): the run ends at the input set — "Import into HROT" stays not done, and the folder offered is the input set's.</summary>
    [Fact]
    public void Import_WithoutAnImporter_EndsAtTheInputSet()
    {
        var m = Model(importer: false);
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin"; m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
        m.Primary();
        PumpUntil(m, () => m.ImportState == OsmRunState.Done);
        Assert.Null(m.TerrainFolder);
        Assert.Equal(Path.Combine(_root, "input", "kolin"), m.InputSetFolder);
        Assert.NotEqual(OsmRunState.Done, m.Stages[^1].State);
        Assert.True(File.Exists(Path.Combine(m.InputSetFolder!, "manifest.json")));
    }

    /// <summary>The production host composes no pipeline (W-3): the menu says why; the model never pretends to preview.</summary>
    [Fact]
    public void WithoutAPipeline_TheMenuIsGreyedWithItsReason()
    {
        var window = new OsmTerrainWizardWindow(() => new OsmWizardServices(null, null, null, null, _root, _root));
        Assert.Equal("the pipeline is not built yet", window.DisabledReason);
        var withOne = new OsmTerrainWizardWindow(() => new OsmWizardServices(_pipeline, null, null, null, _root, _root));
        Assert.Null(withOne.DisabledReason);
    }

    /// <summary>The note severity (⚠ the contract's warnings carry none): dropped / refused / failed / skipped are warnings, the rest information.</summary>
    [Fact]
    public void NoteSeverity_ComesFromItsWords()
    {
        Assert.True(OsmTerrainWizardWindow.IsWarning("9 shapes dropped (< 12 m² or too thin)"));
        Assert.False(OsmTerrainWizardWindow.IsWarning("512 buildings: storeys from their type"));
        Assert.Equal("1 412", OsmTerrainWizardWindow.Num(1412));
        Assert.Equal("31 400", OsmTerrainWizardWindow.Num(31400));
    }

    /// <summary>W-2's fake tiles are real PNGs of the fake place (the raylib upload path is exercised by the frame rails).</summary>
    [Fact]
    public void TheFakeTiles_ArePngs()
    {
        var (x, y) = WebMercator.ToPixel(OsmFakeWorld.Lat, OsmFakeWorld.Lon, 14);
        var png = new FakeTileSource().GetTileAsync(14, (int)(x / 256), (int)(y / 256), CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png!.Take(4).ToArray());
    }
}
