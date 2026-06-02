namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Marks a static parameterless method returning <see cref="BTreeEditorLayout"/> as the layout
/// snapshot for a BTree asset. The <paramref name="assetId"/> must match the asset ID
/// used by the companion <c>[BTreeDefinition]</c> method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BTreeLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public BTreeLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
