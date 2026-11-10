using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.Import;
using Hrot.Terrain.Import.Tests;
using Hrot.UI.Common.TerrainOsm;
using Xunit;

namespace Hrot.Presentation.Tests.TerrainOsm;

/// <summary>
/// ⭐ CE-4378 (handoff D-4) — the wizard's last stage with the REAL importer (<see cref="TerrainImporter"/>, CE-4368): the fake pipeline writes a real input set (the
/// importer's own <see cref="MiniSet"/>), "Import into HROT" runs, and the terrain folder lands where the New Scenario picker lists terrains — the NAS's
/// <c>assets/terrain</c>, read through the same <see cref="TerrainCatalog.ForNode"/> the editor's picker uses. Replace lists the scenarios using it.
/// 🔴 Red-proof (compiles): the importer composed with another terrains root ⇒ the catalog does not list it.
/// </summary>
public sealed class TheWizardImportsWithTheRealImporterTests : IDisposable
{
    private readonly string _nas = Path.Combine(Path.GetTempPath(), "osm-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_nas, true); } catch (IOException) { } }

    private static void PumpUntil(OsmWizardModel m, Func<bool> done, int seconds = 60)
    {
        var sw = Stopwatch.StartNew();
        while (!done()) { if (sw.Elapsed.TotalSeconds > seconds) throw new TimeoutException("the wizard did not reach the state" + (m.Failure is { } f ? ": " + f : "")); m.Pump(); Thread.Sleep(10); }
    }

    [Fact]
    public void TheWizard_ImportsAMiniSet_AndTheNewScenarioPickersCatalog_ListsTheTerrain()
    {
        string terrains = ContentLayout.TerrainAssets(_nas), scenarios = ContentLayout.Scenarios(_nas);
        var importer = new TerrainImporter(terrains, scenarios);
        var pipeline = new FakeOsmPipeline
        {
            WriteSet = folder => new MiniSet { Buildings = { MiniSet.Rect("b1", -20, -20, 0, -5, storeys: 2) } }.Write(folder),
        };
        var m = new OsmWizardModel(new OsmWizardServices(pipeline, null, null, importer, terrains, Path.Combine(_nas, "terrain-input")));
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin-mini"; m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
        Assert.True(m.Primary());
        PumpUntil(m, () => m.ImportState is OsmRunState.Done or OsmRunState.Failed);
        Assert.True(m.ImportState == OsmRunState.Done, m.Failure);

        Assert.Equal(Path.Combine(terrains, "kolin-mini"), m.TerrainFolder);
        Assert.True(File.Exists(Path.Combine(m.TerrainFolder!, TerrainCatalog.DefinitionFileName)), "the terrain folder has its terrain.json");
        Assert.All(m.Stages, s => Assert.Equal(OsmRunState.Done, s.State));   // "Import into HROT" done too
        var picker = TerrainCatalog.ForNode(null, _nas);                         // the editor's NewScenarioTerrains()
        Assert.Contains("kolin-mini", picker.List());

        // the name now exists: the Options step shows the amber row, and Replace lists the scenarios using it (none yet)
        var again = new OsmWizardModel(m.Services) { Name = "kolin-mini" };
        Assert.True(again.NameExists);
        Assert.Empty(again.ScenariosUsingName);
    }

    [Fact]
    public void TheNameRule_IsTheImportersOne()
    {
        var m = new OsmWizardModel(new OsmWizardServices(null, null, null, null, _nas, _nas));
        foreach (var (name, ok) in new[] { ("kolin", true), ("kolin-2_b", true), ("2kolin", true), ("-kolin", false), ("kol.in", false), ("kolin/east", false), ("", false) })
        {
            m.Name = name;
            Assert.Equal(ok, m.NameRefusal == null);
            Assert.Equal(ok, Hrot.Terrain.InputSet.TerrainName.IsValid(name));
        }
    }
}
