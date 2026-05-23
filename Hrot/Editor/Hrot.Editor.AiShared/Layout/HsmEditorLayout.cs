using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

public sealed class HsmEditorLayout
{
    public Vector2 PanOffset { get; init; }
    public float ZoomLevel { get; init; }
    public IReadOnlyDictionary<Guid, StateLayoutEntry> States { get; init; } =
        new Dictionary<Guid, StateLayoutEntry>();
    public IReadOnlyDictionary<Guid, TransitionLayoutEntry> Transitions { get; init; } =
        new Dictionary<Guid, TransitionLayoutEntry>();
    public IReadOnlyDictionary<Guid, RegionLayoutEntry> Regions { get; init; } =
        new Dictionary<Guid, RegionLayoutEntry>();
}
