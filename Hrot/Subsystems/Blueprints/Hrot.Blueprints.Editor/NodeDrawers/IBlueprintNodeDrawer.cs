using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public interface IBlueprintNodeDrawer
{
    bool Handles(Node node);
    INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset);
}
