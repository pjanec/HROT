namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>Editor-side descriptor for a registered EQS query template.</summary>
public sealed class EqsTemplateEntry
{
    public Guid AssetId { get; init; }
    public string DisplayName { get; init; } = "";
}
