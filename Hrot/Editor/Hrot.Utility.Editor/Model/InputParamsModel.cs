namespace Hrot.Utility.Editor.Model;

// Editor-side representation of per-consideration sensor parameters.
public sealed class InputParamsModel
{
    // The EQS template's AssetId GUID (e.g. FindCoverFromTarget.AssetId) -- EQS sensor readers. Empty for non-EQS inputs.
    // ⭐ CE-2046: the ONE copy. The emitter writes In.EqsTopScore("<it>"); the preview derives the sensor id from it with
    // In.EqsTemplateId -- the same function the emitted code runs. (It replaced a never-set BlueprintId the preview read
    // and a TemplateName the emitter read.)
    public string TemplateAssetId = string.Empty;
    // Maximum range in metres -- DistanceToContext readers.
    public float MaxRange;
    // Zero-based weapon mount index -- per-mount weapon readers.
    public int   MountIndex;
}
