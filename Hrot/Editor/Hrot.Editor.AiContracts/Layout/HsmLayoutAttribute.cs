namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Marks a static parameterless method returning <see cref="HsmEditorLayout"/> as the layout
/// snapshot for an HSM asset. The <paramref name="assetId"/> must match the asset ID
/// used by the companion <c>[HsmDefinition]</c> method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HsmLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public HsmLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
