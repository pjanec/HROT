using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// W13 — the scenario's terrain picker (docs/DESIGN_Terrain_World.md §7.3): lists the catalog, marks the
/// resident terrain, applies a different choice, ignores the same one and a cancel.
/// </summary>
public sealed class TerrainPickerLauncherTests
{
    private PickerRequest? _request;
    private Action<PickerResult>? _onChosen;
    private readonly List<string> _applied = new();

    private TerrainPickerLauncher Make(string? resident = "test-town") => new(
        openPicker:      (r, cb) => { _request = r; _onChosen = cb; },
        listTerrains:    () => new[] { "basic-desert", "eu/alpine", "test-town" },
        residentTerrain: () => resident,
        applyTerrain:    _applied.Add);

    private static PickerResult Pick(PickerEntry? e) =>
        new(e == null ? Array.Empty<PickerEntry>() : new[] { e });

    [Fact]
    public void Entries_ListEveryTerrain_AndMarkTheResidentOne()
    {
        var entries = Make().BuildEntries();
        Assert.Equal(new[] { "basic-desert", "eu/alpine", "test-town" }, entries.Select(e => e.Name));
        Assert.NotNull(entries.Single(e => e.Name == "test-town").Description);
        Assert.Null(entries.Single(e => e.Name == "basic-desert").Description);
        Assert.Equal("eu", entries.Single(e => e.Name == "eu/alpine").Category);
    }

    [Fact]
    public void Open_PreselectsTheResidentTerrain()
    {
        Make().Open();
        Assert.Equal(TerrainPickerLauncher.ContextKey, _request!.ContextKey);
        Assert.Equal("test-town", _request.InitialSelectionId);
    }

    [Fact]
    public void ConfirmingADifferentTerrain_AppliesIt()
    {
        var launcher = Make();
        launcher.Open();
        _onChosen!(Pick(launcher.BuildEntries().Single(e => e.Name == "basic-desert")));
        Assert.Equal(new[] { "basic-desert" }, _applied);
    }

    [Fact]
    public void ConfirmingTheResidentTerrain_OrCancelling_AppliesNothing()
    {
        var launcher = Make();
        launcher.Open();
        _onChosen!(Pick(launcher.BuildEntries().Single(e => e.Name == "test-town")));
        _onChosen!(Pick(null));
        Assert.Empty(_applied);
    }
}
