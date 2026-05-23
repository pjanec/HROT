using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// Adapts a TransitionNode to NodeEditor's ILinkModel interface.
// Links are pin-based: from the source state's hidden output pin
// to the target state's hidden input pin.
// VisualId is used as the LinkId for stable identity.
internal sealed class HsmTransitionLink : ILinkModel
{
    private readonly TransitionNode _transition;

    internal HsmTransitionLink(TransitionNode transition)
    {
        _transition = transition;
    }

    public LinkId Id     => new LinkId(_transition.VisualId);
    public PinId FromPin => new PinId(_transition.Source.HiddenOutputPinId);
    public PinId ToPin   => new PinId(_transition.Target.HiddenInputPinId);

    public LinkStyle Style => _transition.Kind == TransitionKind.Internal
        ? LinkStyle.Hidden
        : LinkStyle.Solid;

    public IReadOnlyList<Vector2> Waypoints => _transition.Waypoints;
}
