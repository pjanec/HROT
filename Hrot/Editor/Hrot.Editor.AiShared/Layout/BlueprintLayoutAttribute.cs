namespace Hrot.Editor.AiShared.Layout;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BlueprintLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public BlueprintLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
