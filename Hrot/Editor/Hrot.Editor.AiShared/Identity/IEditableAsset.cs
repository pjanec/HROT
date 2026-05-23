namespace Hrot.Editor.AiShared;

public interface IEditableAsset
{
    Guid AssetId { get; }
    string Name { get; }
    AssetKind Kind { get; }
    string SourceFilePath { get; }
    bool IsDirty { get; }
    bool IsEditorOwned { get; }
    event Action? Changed;
}
