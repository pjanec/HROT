using Fdp.Core;
using Hrot.Map.Common.Services;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ Terrain authoring U-3 — the handlers of a placed block's menu (Re-roll · Rotate · Unpack · Change recipe…) on the host's <see cref="GlobalActionRegistry"/>. <i>Unpack</i> is backend's <see cref="BlockOperations.Unpack"/> (T-4, CE-4431) — ONE bracket, so ONE undo step, and undo restores the block. Delete is the
/// ordinary <see cref="GlobalActionIds.Delete"/> every entity has. Built by the host that shows blocks (the editor).
/// </summary>
public sealed class TerrainBlockActions
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<Action<PickerRequest, Action<PickerResult>>?> _openPicker;
    private readonly Func<IReadOnlyList<TerrainPick>> _recipes;

    private readonly Func<BlockExpansionContext?>? _expansion;
    private readonly Action<string>? _refused;

    /// <param name="expansion">The resident terrain's expansion context (<see cref="BlockExpansionContext.Of"/>) — what Unpack expands the block over; null when none is resident.</param>
    /// <param name="refused">Told why an operation changed nothing (a replaying world, a refusing gate, no resident terrain).</param>
    public TerrainBlockActions(Func<EntityRepository?> world, Func<Action<PickerRequest, Action<PickerResult>>?> openPicker, Func<IReadOnlyList<TerrainPick>> recipes,
        Func<BlockExpansionContext?>? expansion = null, Action<string>? refused = null)
    {
        _expansion = expansion; _refused = refused;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
    }

    /// <summary>Registers the four handlers on <paramref name="actions"/>.</summary>
    public void RegisterOn(GlobalActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        actions.Register(GlobalActionIds.TerrainReroll, (_, target) => { if (_world() is { } w) Report(BlockOperations.Reroll(w, target)); });
        actions.Register(GlobalActionIds.TerrainRotate, (_, target) => { if (_world() is { } w) TerrainBlockOps.Rotate(w, target); });
        actions.Register(GlobalActionIds.TerrainUnpack, (_, target) => Unpack(target));
        actions.Register(GlobalActionIds.TerrainChangeRecipe, (_, target) => ChangeRecipe(target));
    }

    private void Report(BlockOpResult r) { if (!r.Done) _refused?.Invoke(r.Why ?? "refused"); }

    /// <summary>⭐ T-4 — the block becomes its separate <c>TerrainFeature</c> entities in ONE undo step (B-6). Returns what was done; a refusal changed nothing and is reported through the <c>refused</c> callback.</summary>
    public BlockOpResult Unpack(Entity target)
    {
        if (_world() is not { } world) return BlockOpResult.Refused("no world");
        var ctx = _expansion?.Invoke();
        var r = ctx is null ? BlockOpResult.Refused("no terrain is resident to expand the block over") : BlockOperations.Unpack(world, target, ctx);
        if (!r.Done) _refused?.Invoke("Unpack: " + r.Why);
        return r;
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
            if (result.First is { } pick && world.IsAlive(target)) Report(BlockOperations.ChangeRecipe(world, target, pick.Id));
        });
    }
}
