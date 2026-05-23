using System;

namespace Fhsm.Kernel.Attributes;

// Marks a static method returning HsmEditorLayout as the layout snapshot for an HSM asset.
// The method must be static, return HsmEditorLayout, and take zero parameters.
// The assetId parameter must match the AssetId of the corresponding HsmDefinitionAttribute.
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HsmLayoutAttribute : Attribute
{
    public string AssetId { get; }

    public HsmLayoutAttribute(string assetId)
    {
        AssetId = assetId;
    }
}
