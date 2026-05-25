namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Provides a compile-time set of known EQS template asset IDs.
/// Used by Stage 2 validators to check SpawnEqsSensorNode.TemplateAssetId.
/// </summary>
public interface IEqsTemplateCatalog
{
    /// <summary>Returns true if <paramref name="assetId"/> is a registered EQS template.</summary>
    bool Contains(Guid assetId);
}
