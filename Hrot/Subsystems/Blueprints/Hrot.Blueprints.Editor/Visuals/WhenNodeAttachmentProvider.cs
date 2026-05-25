using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment provider for WhenNode. Creates or refreshes a ConditionSummaryAttachment.
/// </summary>
public sealed class WhenNodeAttachmentProvider : IAttachmentProvider
{
    public bool Handles(Node node) => node is WhenNode;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var when = (WhenNode)node;
        if (existing is ConditionSummaryAttachment csa)
        {
            csa.Refresh(when);
            return csa;
        }
        return new ConditionSummaryAttachment(when);
    }
}
