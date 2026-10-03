using System;
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐ <b>The scenario's TERRAIN picker</b> (docs/DESIGN_Terrain_World.md §7.3 W13) — lists every terrain the
/// node's <c>TerrainCatalog</c> can resolve, marks the resident one, and on confirm hands the chosen name to the
/// host, which makes it resident through the same <c>TerrainResidency</c> its load chain commits through.
///
/// <para>⭐ ONE class for both hosts (CGF == editor, ruling 66): each passes its own catalog listing, its
/// resident name and its apply action. The <c>openPicker</c> seam is the shared <see cref="PickerRegistry"/>,
/// exactly as <c>AssetPickerLauncher</c> uses it, so this is unit-testable without ImGui.</para>
/// <para>⚠ Picking changes the terrain on THIS node at once; the other cluster nodes take it at the next
/// scenario load, because a save stamps the resident terrain into the header (CE-3015).</para>
/// </summary>
public sealed class TerrainPickerLauncher
{
    /// <summary>The picker context key — one remembered query/selection per host.</summary>
    public const string ContextKey = "scenario.terrain";

    private readonly Action<PickerRequest, Action<PickerResult>> _openPicker;
    private readonly Func<IReadOnlyList<string>> _listTerrains;
    private readonly Func<string?> _residentTerrain;
    private readonly Action<string> _applyTerrain;

    public TerrainPickerLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        Func<IReadOnlyList<string>> listTerrains,
        Func<string?> residentTerrain,
        Action<string> applyTerrain)
    {
        _openPicker      = openPicker      ?? throw new ArgumentNullException(nameof(openPicker));
        _listTerrains    = listTerrains    ?? throw new ArgumentNullException(nameof(listTerrains));
        _residentTerrain = residentTerrain ?? throw new ArgumentNullException(nameof(residentTerrain));
        _applyTerrain    = applyTerrain    ?? throw new ArgumentNullException(nameof(applyTerrain));
    }

    /// <summary>The entries the picker shows — every terrain, the resident one described as such.</summary>
    public IReadOnlyList<PickerEntry> BuildEntries()
    {
        var resident = _residentTerrain();
        return _listTerrains()
            .Select(name => new PickerEntry(
                Id:            name,
                Name:          name,
                Description:   name == resident ? "the scenario's current terrain" : null,
                Category:      name.Contains('/') ? name[..name.LastIndexOf('/')] : null,
                Keywords:      null,
                IconTextureId: null,
                Tag:           name))
            .ToList();
    }

    /// <summary>Opens the picker; a confirmed choice different from the resident terrain is applied.</summary>
    public void Open()
    {
        var request = new PickerRequest
        {
            ContextKey         = ContextKey,
            Title              = "Scenario Terrain",
            Layout             = PickerLayout.Standard,
            SelectionMode      = PickerSelectionMode.Single,
            InitialSelectionId = _residentTerrain(),
            ItemsProvider      = BuildEntries,
        };

        _openPicker(request, result =>
        {
            if (result.Cancelled) return;
            if (result.First?.Tag is string name && name != _residentTerrain())
                _applyTerrain(name);
        });
    }
}
