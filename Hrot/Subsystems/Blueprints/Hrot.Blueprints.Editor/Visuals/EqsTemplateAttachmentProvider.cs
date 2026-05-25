using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>Attachment provider for SpawnEqsSensorNode.</summary>
public sealed class EqsTemplateAttachmentProvider : IAttachmentProvider
{
    private readonly EqsTemplateRegistry _templates;

    public EqsTemplateAttachmentProvider(EqsTemplateRegistry templates)
        => _templates = templates;

    public bool Handles(Node node) => node is SpawnEqsSensorNode;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var spawn = (SpawnEqsSensorNode)node;
        if (existing is EqsTemplateAttachment eta)
        {
            eta.Refresh(spawn, _templates);
            return eta;
        }
        return new EqsTemplateAttachment(spawn, _templates);
    }
}
