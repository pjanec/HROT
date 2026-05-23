using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

// Strip that shows chips for each global transition defined in the loaded HSM asset.
public sealed class HsmGlobalsStrip
{
    private readonly HsmAsset _asset;

    public HsmGlobalsStrip(HsmAsset asset)
    {
        _asset = asset;
    }

    public void Render()
    {
        // TODO: render the globals strip (window chrome, not canvas content)
        // Shows chips for each GlobalTransitionNode in _asset.GlobalTransitions
    }
}
