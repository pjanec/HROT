using System.Numerics;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Layout;

public sealed class BTreeEditorLayout
{
    public Vector2 PanOffset { get; init; }
    public float ZoomLevel { get; init; }
    public IReadOnlyDictionary<Guid, NodeLayoutEntry> Nodes { get; init; } =
        new Dictionary<Guid, NodeLayoutEntry>();

    // Sync bindings per subtree-node visual ID. Empty when none configured.
    public IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>> SyncBindings { get; init; } =
        new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>();

    public IReadOnlyList<(string VariableName, string WriterPairKey)> BlackboardConflictSuppressions { get; init; } =
        Array.Empty<(string, string)>();
    public IReadOnlyList<string> UnusedWarningSuppressions { get; init; } =
        Array.Empty<string>();
}
