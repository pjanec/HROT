using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Mutable container node model for the demo.</summary>
public sealed class FakeContainerModel : IContainerNodeModel
{
    private readonly List<NodeId>           _childIds = new();
    private readonly List<RegionDescriptor> _regions  = new();
    private readonly Dictionary<NodeId, int> _childRegion = new();

    public NodeId        Id               { get; }
    public NodeKindKey   Kind             { get; }
    public string        Title            { get; set; }
    public string?       Subtitle         { get; set; }
    public NodeCategory  Category         { get; set; } = NodeCategory.Function;
    public Vector2       Position         { get; set; }
    public Vector2?      SizeOverride     { get; set; }
    public NodeState     State            { get; set; } = NodeState.Normal;
    public string?       StatusTooltip    { get; set; }
    public bool          IsCollapsed      { get; set; }
    public bool          ShowAdvancedPins { get; set; }
    public NodeId?       ParentContainerId { get; set; }
    public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

    public bool IsContainer => true;
    public IReadOnlyList<NodeId> ChildNodeIds => _childIds;
    public IReadOnlyList<RegionDescriptor> Regions => _regions;
    public ContainerPadding Padding { get; set; } = ContainerPadding.Default;
    public RegionLayoutOrientation RegionOrientation { get; set; } = RegionLayoutOrientation.VerticalStack;
    public Vector2 MinimumInteriorSize { get; set; } = new(200f, 100f);

    public FakeContainerModel(NodeId id, string title, Vector2 position, NodeKindKey? kind = null)
    {
        Id       = id;
        Kind     = kind ?? new NodeKindKey("container");
        Title    = title;
        Position = position;
    }

    public int GetRegionIndexForChild(NodeId childId) =>
        _childRegion.TryGetValue(childId, out var r) ? r : -1;

    /// <summary>Appends a child node ID to the container's child list.</summary>
    public void AddChild(NodeId childId, int regionIndex = -1)
    {
        if (!_childIds.Contains(childId))
            _childIds.Add(childId);
        if (regionIndex >= 0)
            _childRegion[childId] = regionIndex;
    }

    /// <summary>Adds a region descriptor to the container.</summary>
    public RegionDescriptor AddRegion(string name, int priority = 0, Vector4? customColor = null)
    {
        var rd = new RegionDescriptor(_regions.Count, name, priority, customColor);
        _regions.Add(rd);
        return rd;
    }

    /// <summary>Removes a child node from the container.</summary>
    public void RemoveChild(NodeId childId)
    {
        _childIds.Remove(childId);
        _childRegion.Remove(childId);
    }
}
