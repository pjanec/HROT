using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;

namespace Hrot.ExCon.Observer;

/// <summary>
/// ⭐ CE-277(c3) — the read/proof surface for ExCon's observer state. ExCon has no ECS world, so
/// <c>GET /entities</c> proves nothing; this panel publishes the observer state to <see cref="PanelSnapshot"/>
/// so <c>GET /panels/excon_observer</c> shows the restored camera + marker after a scenario load (§4d T-C).
/// </summary>
public sealed class ExConObserverPanelViewModel : IPanelViewModel
{
    public const string Kind = "ExConObserver";

    public string PanelId   => "excon_observer";
    public string PanelKind => Kind;

    private readonly ExConObserverState _state;

    public ExConObserverPanelViewModel(ExConObserverState state) => _state = state;

    public JsonNode Dump() => new JsonObject
    {
        ["CameraX"]              = _state.CameraX,
        ["CameraY"]              = _state.CameraY,
        ["CameraZ"]              = _state.CameraZ,
        ["InstanceMarker"]       = _state.InstanceMarker,
        ["RestoredFromScenario"] = _state.RestoredFromScenario,
    };
}
