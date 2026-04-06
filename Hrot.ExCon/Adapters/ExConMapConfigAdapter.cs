using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.ExCon.Adapters;

/// <summary>
/// Implements <see cref="IMapConfigController"/> for the ExCon operator station
/// by converting <see cref="MapLayerState"/> changes into a JSON Merge Patch
/// and forwarding them via <see cref="IExConLogic.SendConfigPatch"/>.
///
/// <para>Replaces <c>ExConMapConfigShim</c> as the Phase-6 proper adapter.</para>
/// </summary>
public sealed class ExConMapConfigAdapter : IMapConfigController
{
    private readonly IExConLogic _logic;

    /// <summary>Creates an <see cref="ExConMapConfigAdapter"/>.</summary>
    /// <param name="logic">ExCon logic facade used to dispatch config patches.</param>
    public ExConMapConfigAdapter(IExConLogic logic)
        => _logic = logic ?? throw new ArgumentNullException(nameof(logic));

    /// <inheritdoc/>
    /// <remarks>Returns a sensible default until actual IG state is mirrored back.</remarks>
    public MapLayerState GetCurrentConfig() => new(true, true, true, true, true, true, false);

    /// <inheritdoc/>
    public void ApplyConfig(MapLayerState config)
    {
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
                    grid              = config.Grid,
                }
            }
        });
        _logic.SendConfigPatch(json);
    }
}
