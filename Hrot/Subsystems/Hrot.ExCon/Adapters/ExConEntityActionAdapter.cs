using Hrot.UI.Common.Facades;

namespace Hrot.ExCon.Adapters;

/// <summary>
/// Implements <see cref="IEntityActionController"/> for the ExCon operator station
/// by forwarding entity-action calls to <see cref="IExConLogic"/>.
///
/// <para>Methods that have no direct ExCon equivalent are left as safe no-ops.</para>
/// </summary>
public sealed class ExConEntityActionAdapter : IEntityActionController
{
    private readonly IExConLogic _logic;

    /// <summary>Creates an <see cref="ExConEntityActionAdapter"/>.</summary>
    /// <param name="logic">ExCon logic facade used to dispatch map commands.</param>
    public ExConEntityActionAdapter(IExConLogic logic)
        => _logic = logic ?? throw new ArgumentNullException(nameof(logic));

    /// <inheritdoc/>
    public void CenterOnEntity(long entityId) => _logic.CenterOnEntity((int)entityId);

    /// <inheritdoc/>
    public void DeleteEntity(long entityId) => _logic.DeleteEntity((int)entityId);

    /// <inheritdoc/>
    public void EditOverlay(long entityId) => _logic.StartEditingMode(entityId);

    /// <inheritdoc/>
    public void EditRoute(long entityId) => _logic.StartPersonalRouteAuthoring((int)entityId);

    /// <inheritdoc/>
    /// <remarks>ExCon has no rename command — no-op until IExConLogic exposes one.</remarks>
    public void Rename(long entityId) { /* no-op: no rename command in IExConLogic */ }

    /// <inheritdoc/>
    /// <remarks>ExCon has no measure-tool activation — no-op until wired.</remarks>
    public void ActivateMeasureTool() { /* no-op: no measure tool activation in IExConLogic */ }
}
