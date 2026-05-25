using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Creates or refreshes attachment pills for a specific node type.
/// Providers are registered in the blueprint editor host.
/// </summary>
public interface IAttachmentProvider
{
    /// <summary>True when this provider can handle the given node.</summary>
    bool Handles(Node node);

    /// <summary>
    /// Returns a new or updated attachment for the node.
    /// If <paramref name="existing"/> is non-null and the same concrete type,
    /// providers should mutate and return it to avoid allocation churn.
    /// Returns null when no attachment should be shown (e.g., no template selected).
    /// </summary>
    IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing);
}
