namespace Hrot.Blueprints.Core.Compiler;

/// <summary>The compiler's façade over THE asset-id hash (⭐ CE-2036: <c>Shared/BlueprintIdFnv.cs</c>, linked).</summary>
public static class BlueprintIdHash
{
    public static int Compute(Guid assetId) => global::Fdp.Toolkit.Behavior.Shared.BlueprintIdFnv.Compute(assetId);
}
