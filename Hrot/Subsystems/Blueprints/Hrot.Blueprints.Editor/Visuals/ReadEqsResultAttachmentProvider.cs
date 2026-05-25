using NodeEditor.Core.Interfaces;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>Attachment provider for ReadEqsResultNode.</summary>
public sealed class ReadEqsResultAttachmentProvider : IAttachmentProvider
{
    public bool Handles(Node node) => node is ReadEqsResultNode;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var read = (ReadEqsResultNode)node;
        if (existing is ReadEqsResultAttachment rra)
        {
            rra.Refresh(read);
            return rra;
        }
        return new ReadEqsResultAttachment(read);
    }
}
