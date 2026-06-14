namespace Hrot.Editor.AiShared.Selection;

public sealed record BlueprintNodeSelection(Guid GraphId, Guid NodeId) : IAssetSubSelection;
public sealed record BTreeNodeSelection(Guid VisualId) : IAssetSubSelection;
public sealed record BTreePillSelection(Guid PillVisualId) : IAssetSubSelection;
public sealed record HsmStateSelection(Guid StableId) : IAssetSubSelection;
public sealed record HsmTransitionSelection(Guid VisualId) : IAssetSubSelection;
public sealed record HsmRegionSelection(Guid StableId, int RegionIndex) : IAssetSubSelection;
public sealed record UtilityConsiderationSelection(
    int OptionIndex,
    int ConsiderationIndex) : IAssetSubSelection;
