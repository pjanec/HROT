using System;
using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ <c>CE-3054</c> D — the ONE "which sensor" picker of the two EQS-read nodes (<see cref="ReadEqsResultNode"/>, the
/// When node's EQS form): a sensor VARIABLE the blueprint spawned, or the UNIT's own sensor of a kind (its TKB sensors).
/// 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.8a.
/// </summary>
internal static class EqsSensorSourcePicker
{
    /// <summary>The unit sensor kinds, as their <c>SensorModality</c> values and labels.</summary>
    internal static readonly (byte Kind, string Label)[] UnitKinds =
    {
        (1, "Unit sensor: Visual"), (2, "Unit sensor: Radar"), (4, "Unit sensor: Thermal"), (8, "Unit sensor: Acoustic"),
    };

    /// <summary>The names of the asset's <c>EqsSensorHandle</c> variables.</summary>
    internal static string[] SensorVariables(BlueprintAsset asset)
        => asset.Declarations.Of(DeclarationKind.Variable)
            .Where(d => d.Type.TypeId == "FDP.Eqs.EqsSensorHandle")
            .Select(d => d.Name)
            .ToArray();

    /// <summary>The combo items: the sensor variables, then the unit sensor kinds.</summary>
    internal static string[] Items(string[] variables) => variables.Concat(UnitKinds.Select(k => k.Label)).ToArray();

    /// <summary>The selected item's index (-1 = none).</summary>
    internal static int IndexOf(string[] variables, string variableName, byte unitKind)
    {
        if (unitKind != 0)
        {
            int k = Array.FindIndex(UnitKinds, u => u.Kind == unitKind);
            return k < 0 ? -1 : variables.Length + k;
        }
        return Array.IndexOf(variables, variableName);
    }

    /// <summary>Applies a picked item: a variable clears the kind, a kind clears the variable.</summary>
    internal static void Apply(string[] variables, int index, ref string variableName, ref byte unitKind)
    {
        if (index < 0) return;
        if (index < variables.Length) { variableName = variables[index]; unitKind = 0; }
        else { unitKind = UnitKinds[index - variables.Length].Kind; variableName = ""; }
    }

    /// <summary>Draws the picker; true when the choice changed.</summary>
    internal static bool Draw(string label, BlueprintAsset asset, ref string variableName, ref byte unitKind)
    {
        var variables = SensorVariables(asset);
        var items = Items(variables);
        int idx = IndexOf(variables, variableName, unitKind);
        if (!ImGui.Combo(label, ref idx, items, items.Length)) return false;
        Apply(variables, idx, ref variableName, ref unitKind);
        return true;
    }

    /// <summary>The short label of a choice, for a node's title.</summary>
    internal static string Describe(string variableName, byte unitKind)
    {
        if (unitKind == 0) return variableName;
        int k = Array.FindIndex(UnitKinds, u => u.Kind == unitKind);
        return k < 0 ? $"unit sensor {unitKind}" : UnitKinds[k].Label.Replace("Unit sensor: ", "unit ");
    }
}
