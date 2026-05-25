using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>Palette descriptor for a node kind.</summary>
public sealed class NodeKindDescriptor
{
    public string Kind { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public string Icon { get; init; } = "";
    public required Func<Node> CreateInstance { get; init; }
}
