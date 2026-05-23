namespace Hrot.Editor.AiShared.Layout;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class HsmLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public HsmLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
