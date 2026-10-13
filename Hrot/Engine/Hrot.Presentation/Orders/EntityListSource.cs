using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using NodeEditor.UI.Picker;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ <c>CE-1059</c> (F6, U3) — the searchable entity LIST an entity field can be filled from instead of a map click: every
/// networked entity the field's <c>[MapPickableEntity]</c> presets admit — by the SAME filter the map pick uses
/// (<see cref="Hrot.Presentation.Map.HrotEntityFilterFactory"/>, <c>CE-1060</c>) — named as the ORBAT names it
/// (<see cref="EntityInfo.Name"/>) and foldered by side, shown in the node editor's fuzzy <see cref="PickerRegistry"/>.
/// 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.12 O3, O4.
/// </summary>
public static class EntityListSource
{
    public const string ContextKey = "order-entity-list";

    /// <summary>The entities the list offers: networked, admitted by <paramref name="presets"/>, not in <paramref name="exclude"/>
    /// (the units being ordered), in name order.</summary>
    public static IReadOnlyList<PickerEntry> Entries(EntityRepository world, string[]? presets, ICollection<long>? exclude = null)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var list = new List<PickerEntry>();
        if (!world.IsComponentTypeRegistered<NetworkIdentity>()) return list;
        // ⚠ The layer filter reads MapDisplayComponent (assigned by MapLayerAssignmentSystem on every map host); no presets, or a
        //   world without it (a test), is not narrowed — the same rule as CanvasMapPickAdapter's default.
        IEntityFilter? filter = presets is { Length: > 0 } && world.IsComponentTypeRegistered<MapDisplayComponent>()
            ? new Hrot.Presentation.Map.HrotEntityFilterFactory(world).CreateFilter(presets ?? Array.Empty<string>())
            : null;
        bool info = world.IsComponentTypeRegistered<EntityInfo>();
        foreach (var e in world.Query().With<NetworkIdentity>().Build())
        {
            long id = world.GetComponentRO<NetworkIdentity>(e).Value;
            if (id <= 0 || (exclude != null && exclude.Contains(id))) continue;
            if (filter != null && !filter.IsMatch(e)) continue;
            string idText = "#" + id.ToString(CultureInfo.InvariantCulture);
            string name = idText;
            string category = "Other";
            if (info && world.HasComponent<EntityInfo>(e))
            {
                ref readonly var i = ref world.GetComponentRO<EntityInfo>(e);
                var n = i.Name.ToString();
                if (!string.IsNullOrWhiteSpace(n)) name = n;
                category = i.ForceId switch { ForceId.Friend => "Friendly", ForceId.Hostile => "Hostile", _ => "Neutral" };
            }
            list.Add(new PickerEntry(
                Id: id.ToString(CultureInfo.InvariantCulture),
                Name: name,
                Description: idText,
                Category: category,
                Keywords: new[] { idText },
                IconTextureId: null,
                Tag: id));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>The picker request for <see cref="Entries"/> (built lazily, so the list is fresh when it opens).</summary>
    public static PickerRequest Request(EntityRepository world, string[]? presets, ICollection<long>? exclude = null)
        => new()
        {
            ContextKey = ContextKey,
            Title = presets is { Length: > 0 } ? "Choose: " + string.Join(", ", presets) : "Choose an entity",
            ItemsProvider = () => Entries(world, presets, exclude),
            FoldSingleChildFolders = true,
        };

    /// <summary>
    /// ⭐ THE opener a <see cref="Hrot.Presentation.Facades.MapPickBroker.EntityList"/> takes: opens the list in the map's picker
    /// registry and calls back with the chosen network id; <c>false</c> when the host has no registry.
    /// </summary>
    public static Func<string[]?, Action<int>, bool> Opener(EntityRepository world, Func<PickerRegistry?> pickers,
        Func<ICollection<long>?>? exclude = null)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (pickers is null) throw new ArgumentNullException(nameof(pickers));
        return (presets, onChosen) =>
        {
            if (pickers() is not { } registry) return false;
            registry.OpenPicker(Request(world, presets, exclude?.Invoke()), result =>
            {
                if (result.Selection.Count > 0 && result.Selection[0].Tag is long id) onChosen((int)id);
            });
            return true;
        };
    }
}
