using Fdp.Core;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ Terrain authoring U-3 — the handlers of a placed block's menu (Re-roll · Rotate · Change recipe…) on the host's <see cref="GlobalActionRegistry"/>. <i>Unpack</i> has none: its menu row is greyed (backend's T-4). Delete is the
/// ordinary <see cref="GlobalActionIds.Delete"/> every entity has. Built by the host that shows blocks (the editor).
/// </summary>
public sealed class TerrainBlockActions
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<Action<PickerRequest, Action<PickerResult>>?> _openPicker;
    private readonly Func<IReadOnlyList<TerrainPick>> _recipes;

    public TerrainBlockActions(Func<EntityRepository?> world, Func<Action<PickerRequest, Action<PickerResult>>?> openPicker, Func<IReadOnlyList<TerrainPick>> recipes)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
    }

    /// <summary>Registers the three handlers on <paramref name="actions"/>.</summary>
    public void RegisterOn(GlobalActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        actions.Register(GlobalActionIds.TerrainReroll, (_, target) => { if (_world() is { } w) TerrainBlockOps.Reroll(w, target); });
        actions.Register(GlobalActionIds.TerrainRotate, (_, target) => { if (_world() is { } w) TerrainBlockOps.Rotate(w, target); });
        actions.Register(GlobalActionIds.TerrainChangeRecipe, (_, target) => ChangeRecipe(target));
    }

    /// <summary>The request <i>Change recipe…</i> opens: the block recipes as a plain list, the current one pre-selected.</summary>
    public PickerRequest RecipeRequest(string? currentRecipe) => new()
    {
        ContextKey = "terrain/change-recipe",
        Title = "Change recipe",
        Layout = PickerLayout.Standard,
        InitialSelectionId = currentRecipe,
        ItemsProvider = () => _recipes().Where(p => p.Kind == TerrainPickKind.Block)
            .Select(p => new PickerEntry(p.Id, p.Name, p.Line, null, null, null, p)).ToArray(),
    };

    private void ChangeRecipe(Entity target)
    {
        var world = _world(); var open = _openPicker();
        if (world is null || open is null || !world.IsAlive(target) || !world.HasComponent<Fdp.Toolkit.Terrain.TerrainBlock>(target)) return;
        string? current = world.GetComponentRO<Fdp.Toolkit.Terrain.TerrainBlock>(target).Recipe.ToString();
        open(RecipeRequest(current), result =>
        {
            if (result.First is { } pick && world.IsAlive(target)) TerrainBlockOps.ChangeRecipe(world, target, pick.Id);
        });
    }
}
