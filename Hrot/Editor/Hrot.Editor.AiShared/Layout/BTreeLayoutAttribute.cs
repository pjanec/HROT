namespace Hrot.Editor.AiShared.Layout;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BTreeLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public BTreeLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
