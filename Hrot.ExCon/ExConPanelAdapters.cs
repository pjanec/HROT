using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.UI.Common.Facades;
using UiMissionCommitResult = Hrot.UI.Common.Models.MissionCommitResult;

namespace Hrot.ExCon;

/// <summary>
/// Temporary shim that delegates <see cref="ISpawnController"/> calls to
/// <see cref="IExConLogic"/> until Phase 6 proper adapters are wired.
/// </summary>
internal sealed class ExConSpawnShim : ISpawnController
{
    private readonly IExConLogic _logic;

    internal ExConSpawnShim(IExConLogic logic) => _logic = logic;

    /// <inheritdoc/>
    public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
        => _logic.StartPlacementMode(tkbType, initialPropertiesJson);

    /// <inheritdoc/>
    public void StartAreaAuthoringMode(string styleOverrideJson = "")
        => _logic.StartAreaAuthoringMode(styleOverrideJson);

    /// <inheritdoc/>
    public void StartRouteAuthoringMode()
        => _logic.StartRouteAuthoringMode();
}

/// <summary>
/// Temporary shim that adapts <see cref="IExConLogic.SendConfigPatch"/> into the
/// <see cref="IMapConfigController"/> port until the proper Phase 6 adapter is wired.
/// </summary>
internal sealed class ExConMapConfigShim : IMapConfigController
{
    private readonly IExConLogic _logic;

    internal ExConMapConfigShim(IExConLogic logic) => _logic = logic;

    /// <inheritdoc/>
    public UI.Common.Models.MapLayerState GetCurrentConfig()
        => new(true, true, true, true, true, true, false); // defaults until Phase 6 reads actual IG state

    /// <inheritdoc/>
    public void ApplyConfig(UI.Common.Models.MapLayerState config)
    {
        // TODO Phase 6: replace with proper ExConMapConfigAdapter
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            view = new
            {
                layers = new
                {
                    satellite         = config.Satellite,
                    units_ground      = config.GroundUnits,
                    units_air         = config.AirUnits,
                    vehicles          = config.Vehicles,
                    tactical_graphics = config.TacticalGraphics,
                    road_graphs       = config.RoadGraphs,
                    grid              = config.Grid
                }
            }
        });
        _logic.SendConfigPatch(json);
    }
}

/// <summary>
/// Temporary shim that wraps ExCon's internal <see cref="Services.IMissionEditorService"/>
/// into the shared <see cref="IMissionEditorService"/> port until Phase 6.
/// </summary>
internal sealed class ExConMissionShim : IMissionEditorService
{
    private readonly Services.IMissionEditorService _inner;

    internal ExConMissionShim(Services.IMissionEditorService inner) => _inner = inner;

    /// <inheritdoc/>
    /// <remarks>Delegates to the inner <see cref="Services.IMissionEditorService"/> which uses <see cref="DoctrineCatalog"/>.</remarks>
    public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
        => _inner.GetAvailableBehaviors(entityId);

    /// <inheritdoc/>
    public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
        => _inner.GetMissionSnapshot(entityId);

    /// <inheritdoc/>
    public async Task<UiMissionCommitResult> CommitMissionAsync(
        long entityId, MissionPlan plan, long baseVersion)
    {
        var r = await _inner.CommitMissionAsync(entityId, plan, baseVersion).ConfigureAwait(false);
        return new UiMissionCommitResult(r.Success, r.NewVersion, r.ErrorMessage);
    }

    /// <inheritdoc/>
    public async Task<UiMissionCommitResult> SendControlCommandAsync(
        long entityId, eMissionCommandType type, Guid taskId)
    {
        var r = await _inner.SendControlCommandAsync(entityId, type, taskId).ConfigureAwait(false);
        return new UiMissionCommitResult(r.Success, r.NewVersion, r.ErrorMessage);
    }
}

/// <summary>
/// Temporary shim that wraps ExCon's internal <see cref="Services.IMapPickService"/>
/// into the shared <see cref="IMapPickService"/> port until Phase 6.
/// </summary>
internal sealed class ExConMapPickShim : IMapPickService
{
    private readonly Services.IMapPickService _inner;

    internal ExConMapPickShim(Services.IMapPickService inner) => _inner = inner;

    /// <inheritdoc/>
    public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
        => _inner.PickLocationAsync(ct);

    /// <inheritdoc/>
    public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
        => _inner.PickEntityAsync(filterPresets, ct);

    /// <inheritdoc/>
    /// <remarks>Area pick not yet supported by ExCon; always returns empty list.</remarks>
    public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(
        string[]? filterPresets = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
}
