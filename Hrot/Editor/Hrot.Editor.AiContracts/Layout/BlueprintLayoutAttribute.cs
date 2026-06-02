namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Marks a static parameterless method returning a Blueprint layout snapshot for a Blueprint asset.
/// The <paramref name="assetId"/> must match the asset ID of the companion
/// <c>[BlueprintDefinition]</c> method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BlueprintLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public BlueprintLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
