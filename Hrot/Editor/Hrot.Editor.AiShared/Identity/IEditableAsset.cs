namespace Hrot.Editor.AiShared;

public interface IEditableAsset
{
    Guid AssetId { get; }
    string Name { get; }
    AssetKind Kind { get; }
    string SourceFilePath { get; }
    bool IsDirty { get; }
    bool IsEditorOwned { get; }
    /// <summary>⭐ <c>CE-1252</c> (BV-19) — projected from a C# builder (never a graph asset): shown and debugged, never edited or saved.</summary>
    bool IsCodeBuilt => false;
    event Action? Changed;
}
