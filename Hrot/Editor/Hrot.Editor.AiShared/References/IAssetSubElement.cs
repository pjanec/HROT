namespace Hrot.Editor.AiShared.References;

/// <summary>A thing that can be referenced from inside an asset. Identity is by Key.</summary>
public interface IAssetSubElement
{
    string Key { get; }
    SubElementKind Kind { get; }
    string DisplayName { get; }
    Guid? SourceAssetId { get; }
}
