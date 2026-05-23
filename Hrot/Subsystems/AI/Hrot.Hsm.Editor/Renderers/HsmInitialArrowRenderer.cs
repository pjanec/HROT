using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.Hsm.Editor.Renderers;

// Custom canvas renderer that draws initial-state arrows for composite states.
// Runs in the AfterNodes pass so arrows appear above node bodies.
public sealed class HsmInitialArrowRenderer : ICustomCanvasRenderer
{
    private readonly HsmAsset _asset;

    public HsmInitialArrowRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public string Id => "hsm.initial_state_arrows";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive { get; set; } = true;

    public void Render(ICanvasRenderContext ctx)
    {
        // TODO: draw filled circle + arrow to initial child for each composite state
        // Runs in AfterNodes pass
    }
}
